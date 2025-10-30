using DocumentIntelligence.Contracts.Contracts;
using Azure.Messaging.ServiceBus;
using DocumentIntelligence.Contracts.DomainContracts;
using AnalysisService.Worker.Infrastructure;

namespace AnalysisService.Worker.Messaging;
public sealed class AnalyzeDocumentCommandListener : BackgroundService
{
    private readonly ServiceBusMessageProcessor<AnalyzeDocumentCommand> _processor;

    public AnalyzeDocumentCommandListener(
        IConfiguration config,
        ServiceBusClient client,
        ILogger<ServiceBusMessageProcessor<AnalyzeDocumentCommand>> logger,
        IServiceScopeFactory scopeFactory)
    {
        var queueName = config["AzureServiceBus:AnalyzeDocumentQueueName"] ?? "analyze-document";

        // wire the per-message handler via a scope
        _processor = new ServiceBusMessageProcessor<AnalyzeDocumentCommand>(
            client,
            logger,
            entityName: queueName,
            subscriptionName: null, // queue
            handler: async (cmd, ct) =>
            {
                using var scope = scopeFactory.CreateScope();
                var blobWriter = scope.ServiceProvider.GetRequiredService<IBlobWriter>();
                var resultPublisher = scope.ServiceProvider.GetRequiredService<IAnalysisResultEventPublisher>();
                var log = scope.ServiceProvider.GetRequiredService<ILogger<AnalyzeDocumentCommandListener>>();

                log.LogInformation("Handling AnalyzeDocumentCommand for {DocumentId}", cmd.DocumentId);

                var extractedEntities = new[] { "InvoiceNo:12345", "Amount:99.99" };
                var summary = $"Auto summary for {cmd.FileName}";

                var blobRef = await blobWriter.SaveAsync(cmd.DocumentId, extractedEntities, ct);

                var evt = new AnalysisCompletedEvent(cmd.DocumentId, summary, DocumentStatus.Analyzed, blobRef);
                await resultPublisher.PublishAsync(evt, ct);
            });
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => _processor.StartAsync(stoppingToken);
    public override Task StopAsync(CancellationToken cancellationToken) => _processor.StopAsync(cancellationToken);
    public override void Dispose() => _processor.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
