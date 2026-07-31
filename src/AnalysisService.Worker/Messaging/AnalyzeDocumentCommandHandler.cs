using AnalysisService.Worker.Infrastructure;
using DocumentIntelligence.Contracts;
using DocumentIntelligence.Messaging;
using Microsoft.Extensions.Logging;

namespace AnalysisService.Worker.Messaging;

public sealed class AnalyzeDocumentCommandHandler : IMessageHandler<AnalyzeDocumentCommand>
{
    private readonly IAnalysisResultStore _results;
    private readonly IAnalysisResultEventPublisher _resultPublisher;
    private readonly ILogger<AnalyzeDocumentCommandHandler> _logger;

    public AnalyzeDocumentCommandHandler(
        IAnalysisResultStore results,
        IAnalysisResultEventPublisher resultPublisher,
        ILogger<AnalyzeDocumentCommandHandler> logger)
    {
        _results = results;
        _resultPublisher = resultPublisher;
        _logger = logger;
    }

    public async Task HandleAsync(AnalyzeDocumentCommand cmd, CancellationToken ct = default)
    {
        if (cmd is null) throw new ArgumentNullException(nameof(cmd));

        _logger.LogInformation("Handling AnalyzeDocumentCommand for {DocumentId}", cmd.DocumentId);

        StoredAnalysis analysis;
        try
        {
            // Ask the output store whether this command has already been analysed. The
            // broker delivers at least once and this service keeps no state of its own,
            // so the store is what stands in for a memory - and analysis is the expensive
            // part, which is exactly what should not be repeated.
            var existing = await _results.TryGetAsync(cmd.CommandId, ct);

            if (existing is not null)
            {
                _logger.LogInformation(
                    "Command {CommandId} was already analysed; republishing the stored result.",
                    cmd.CommandId);

                analysis = existing;
            }
            else
            {
                // demo analysis
                var extractedEntities = new[] { "InvoiceNo:12345", "Amount:99.99" };
                var summary = $"Auto summary for {cmd.FileName}";

                analysis = await _results.SaveAsync(
                    cmd.CommandId, cmd.DocumentId, summary, extractedEntities, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down - not an analysis failure. Let the message be redelivered.
            throw;
        }
        catch (Exception ex)
        {
            // The outcome is a failure, and a failure is still a result: report it so the
            // document leaves its "analyzing" state. Analysis is terminal here - retrying
            // is the caller's decision, made by triggering analysis again.
            _logger.LogError(ex, "Analysis failed for {DocumentId}", cmd.DocumentId);

            await _resultPublisher.PublishAsync(
                new AnalysisFailedEvent(
                    DeterministicId.From(cmd.CommandId, "analysis-failed"),
                    cmd.DocumentId,
                    ex.Message),
                ct);

            return;
        }

        // Published whether the analysis just ran or was skipped: skipping the work does
        // not mean the result was ever successfully announced. The id is derived from the
        // command, so a redelivery republishes something the consumer's inbox discards.
        await _resultPublisher.PublishAsync(
            new AnalysisCompletedEvent(
                DeterministicId.From(cmd.CommandId, "analysis-completed"),
                cmd.DocumentId,
                analysis.Summary,
                analysis.BlobReference),
            ct);
    }
}
