using System.Text.Json;
using Azure.Messaging.ServiceBus;
using DocumentIntelligence.Contracts;
using DocumentIntelligence.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DocumentService.Api.Features.Documents.RecordAnalysisResult;

public sealed class AnalysisFailedEventListener : BackgroundService
{
    private readonly ServiceBusMessageProcessor<AnalysisFailedEvent> _processor;

    public AnalysisFailedEventListener(
        IConfiguration config,
        ServiceBusClient client,
        ILogger<ServiceBusMessageProcessor<AnalysisFailedEvent>> logger,
        IServiceScopeFactory scopeFactory,
        JsonSerializerOptions jsonOpt)
    {
        var topic = config["AzureServiceBus:AnalysisFailedTopic"] ?? "analysis-failed";
        var subscription = config["AzureServiceBus:AnalysisFailedSubscription"] ?? "document-api";

        _processor = new ServiceBusMessageProcessor<AnalysisFailedEvent>(
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
