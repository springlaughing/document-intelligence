using AnalysisService.Worker.Infrastructure;
using DocumentIntelligence.Contracts.Contracts;
using DocumentIntelligence.Contracts.DomainContracts;
using DocumentIntelligence.Contracts.Messaging;
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

        // demo analysis
        var extractedEntities = new[] { "InvoiceNo:12345", "Amount:99.99" };
        var summary = $"Auto summary for {cmd.FileName}";

        var blobRef = await _blobWriter.SaveAsync(cmd.DocumentId, extractedEntities, ct);

        var evt = new AnalysisCompletedEvent(cmd.DocumentId, summary, DocumentStatus.Analyzed, blobRef);
        await _resultPublisher.PublishAsync(evt, ct);
    }
}
