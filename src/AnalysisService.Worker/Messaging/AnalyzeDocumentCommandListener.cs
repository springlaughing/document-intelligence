using DocumentIntelligence.Contracts.Contracts;
using DocumentIntelligence.Messaging;
using Azure.Messaging.ServiceBus;
using System.Text.Json;


namespace AnalysisService.Worker.Messaging;
public sealed class AnalyzeDocumentCommandListener : BackgroundService
{
    private readonly ServiceBusMessageProcessor<AnalyzeDocumentCommand> _processor;

    public AnalyzeDocumentCommandListener(
        IConfiguration config,
        ServiceBusClient client,
        ILogger<ServiceBusMessageProcessor<AnalyzeDocumentCommand>> logger,
        IServiceScopeFactory scopeFactory,
        JsonSerializerOptions jsonOpt)
    {
        var queueName = config["AzureServiceBus:AnalyzeDocumentQueueName"] ?? "analyze-document";

        // per-message handler wired via scope
        _processor = new ServiceBusMessageProcessor<AnalyzeDocumentCommand>(
            client,
            scopeFactory,
            logger,
            entityName: queueName,
            subscriptionName: null,
            options: null,
            jsonOpt: jsonOpt
            );
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => _processor.StartAsync(stoppingToken);
    public override Task StopAsync(CancellationToken cancellationToken) => _processor.StopAsync(cancellationToken);
    public override void Dispose() => _processor.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
