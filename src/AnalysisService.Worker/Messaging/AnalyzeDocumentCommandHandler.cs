using AnalysisService.Worker.Infrastructure;
using DocumentIntelligence.Contracts.Contracts;
using DocumentIntelligence.Messaging;
using Microsoft.Extensions.Logging;

namespace AnalysisService.Worker.Messaging;

public sealed class AnalyzeDocumentCommandHandler : IMessageHandler<AnalyzeDocumentCommand>
{
    private readonly IBlobWriter _blobWriter;
    private readonly IAnalysisResultEventPublisher _resultPublisher;
    private readonly ILogger<AnalyzeDocumentCommandHandler> _logger;

    public AnalyzeDocumentCommandHandler(
        IBlobWriter blobWriter,
        IAnalysisResultEventPublisher resultPublisher,
        ILogger<AnalyzeDocumentCommandHandler> logger)
    {
        _blobWriter = blobWriter;
        _resultPublisher = resultPublisher;
        _logger = logger;
    }

    public async Task HandleAsync(AnalyzeDocumentCommand cmd, CancellationToken ct = default)
    {
        if (cmd is null) throw new ArgumentNullException(nameof(cmd));

        _logger.LogInformation("Handling AnalyzeDocumentCommand for {DocumentId}", cmd.DocumentId);

        AnalysisCompletedEvent evt;
        try
        {
            // demo analysis
            var extractedEntities = new[] { "InvoiceNo:12345", "Amount:99.99" };
            var summary = $"Auto summary for {cmd.FileName}";

            var blobRef = await _blobWriter.SaveAsync(cmd.DocumentId, extractedEntities, ct);

            evt = new AnalysisCompletedEvent(cmd.DocumentId, summary, blobRef);
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
                new AnalysisFailedEvent(cmd.DocumentId, ex.Message), ct);

            return;
        }

        await _resultPublisher.PublishAsync(evt, ct);
    }
}
