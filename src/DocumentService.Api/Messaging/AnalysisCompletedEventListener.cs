using System.Text.Json;
using Azure.Messaging.ServiceBus;
using DocumentIntelligence.Contracts.Contracts;
using DocumentIntelligence.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DocumentService.Api.Messaging;

public sealed class AnalysisCompletedEventListener : BackgroundService
{
    private readonly ServiceBusMessageProcessor<AnalysisCompletedEvent> _processor;

    public AnalysisCompletedEventListener(
        IConfiguration config,
        ServiceBusClient client,
        ILogger<ServiceBusMessageProcessor<AnalysisCompletedEvent>> logger,
        IServiceScopeFactory scopeFactory,
        JsonSerializerOptions jsonOpt)
    {
        var topic = config["AzureServiceBus:AnalysisCompletedTopic"] ?? "analysis-completed";
        var subscription = config["AzureServiceBus:AnalysisCompletedSubscription"] ?? "document-api";

        _processor = new ServiceBusMessageProcessor<AnalysisCompletedEvent>(
            client,
            scopeFactory,
            logger,
            entityName: topic,
            subscriptionName: subscription,
            options: null,
            jsonOpt: jsonOpt);
            
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => _processor.StartAsync(stoppingToken);
    public override Task StopAsync(CancellationToken cancellationToken) => _processor.StopAsync(cancellationToken);
    public override void Dispose() => _processor.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
