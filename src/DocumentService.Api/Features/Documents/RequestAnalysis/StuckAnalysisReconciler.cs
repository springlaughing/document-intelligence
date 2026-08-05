using DocumentIntelligence.Contracts;
using DocumentService.Api.Infrastructure.Repositories;

namespace DocumentService.Api.Features.Documents.RequestAnalysis;

// One pass over documents stuck in Analyzing: queue the command again, or give up.
//
// Everything else in this service is edge-triggered - it reacts to a message arriving. That
// is why the outbox and the inbox, between them, still cannot close the gap: if a command
// dead-letters after MaxDeliveryCount, no message ever arrives, so no handler ever runs,
// and nothing observes that the document is stranded. You cannot write a handler for the
// message you did not get.
//
// This is level-triggered instead. It reads state and asks whether the state is wrong,
// which is why it does not care *how* a document got stuck - a dead-lettered command, a
// worker that died mid-analysis, a bug nobody has found yet. That indifference to cause is
// the whole value; see ADR 0004.
//
// Scheduling lives in StuckAnalysisScheduler. This owns the work, so a pass can be tested
// without starting a background service and waiting on a timer.
public sealed class StuckAnalysisReconciler
{
    private readonly IDocumentRepository _repo;
    private readonly IAnalyzeDocumentCommandQueue _queue;
    private readonly ILogger<StuckAnalysisReconciler> _logger;

    public StuckAnalysisReconciler(
        IDocumentRepository repo,
        IAnalyzeDocumentCommandQueue queue,
        ILogger<StuckAnalysisReconciler> logger)
    {
        _repo = repo;
        _queue = queue;
        _logger = logger;
    }

    public async Task<ReconciliationPass> RunAsync(
        TimeSpan stuckAfter,
        int maxAttempts,
        int batchSize,
        CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - stuckAfter;

        var candidates = await _repo.FindStuckAnalysesAsync(cutoff, batchSize, ct);
        if (candidates.Count == 0) return ReconciliationPass.Empty;

        var requeued = 0;
        var abandoned = 0;
        var contended = 0;

        foreach (var document in candidates)
        {
            ct.ThrowIfCancellationRequested();

            if (document.Attempts >= maxAttempts)
            {
                if (await AbandonAsync(document, ct)) abandoned++;
                else contended++;

                continue;
            }

            if (await RequeueAsync(document, maxAttempts, ct)) requeued++;
            else contended++;
        }

        ReconciliationTelemetry.Requeued.Add(requeued);
        ReconciliationTelemetry.Abandoned.Add(abandoned);
        ReconciliationTelemetry.Contended.Add(contended);

        return new ReconciliationPass(requeued, abandoned, contended);
    }

    private async Task<bool> RequeueAsync(
        StuckAnalysis document, int maxAttempts, CancellationToken ct)
    {
        // A new CommandId, not the original one. The worker skips analysis it has already
        // done by looking for the output the command id would have produced (ADR 0003), so
        // reusing the id risks the retry being recognised as done and quietly doing
        // nothing - the one outcome this whole pass exists to prevent.
        //
        // The cost is that a document whose analysis genuinely completed, and only lost its
        // result event, gets analysed a second time. Redundant work is the cheaper mistake.
        var command = new AnalyzeDocumentCommand(
            CommandId: Guid.NewGuid(),
            DocumentId: document.Id,
            FileName: document.FileName);

        var requeued = await _repo.TryRetryAnalysisAsync(
            document.Id, document.Attempts, _queue.Prepare(command), ct);

        if (!requeued) return false;

        // Warning rather than information: nothing here is routine. Every line is a
        // document the ordinary path failed to carry, and a run of them is a symptom.
        _logger.LogWarning(
            "Document {DocumentId} was still Analyzing since {StartedAt}; queued analysis "
            + "again as attempt {Attempt} of {MaxAttempts}.",
            document.Id, document.StartedAtUtc, document.Attempts + 1, maxAttempts);

        return true;
    }

    private async Task<bool> AbandonAsync(StuckAnalysis document, CancellationToken ct)
    {
        var reason =
            $"Analysis did not complete after {document.Attempts} attempts; "
            + $"last started {document.StartedAtUtc:u}.";

        if (!await _repo.TryFailStuckAnalysisAsync(document.Id, document.Attempts, reason, ct))
            return false;

        // The terminal state has to be loud. Re-queueing forever would turn one poisonous
        // document into permanent background load, and marking it Failed silently would
        // hide it just as effectively as leaving it Analyzing.
        _logger.LogError(
            "Giving up on document {DocumentId} after {Attempts} analysis attempts; "
            + "marked Failed.",
            document.Id, document.Attempts);

        return true;
    }
}

/// What one pass did. Contended means another writer had already handled the candidate.
public record ReconciliationPass(int Requeued, int Abandoned, int Contended)
{
    public static readonly ReconciliationPass Empty = new(0, 0, 0);

    public int Total => Requeued + Abandoned + Contended;
}
