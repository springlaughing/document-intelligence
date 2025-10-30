using System.Text.Json;
using Azure.Messaging.ServiceBus;
using DocumentIntelligence.Contracts.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DocumentService.Api.Messaging;

public sealed class AnalysisCompletedEventConsumer : BackgroundService
{
    private readonly ServiceBusMessageProcessor<AnalysisCompletedEvent> _processor;

    public AnalysisCompletedEventConsumer(
        IConfiguration config,
        ServiceBusClient client,
        ILogger<ServiceBusMessageProcessor<AnalysisCompletedEvent>> logger,
        IServiceScopeFactory scopeFactory)
    {
        var topic = config["ServiceBus:AnalysisCompletedTopic"] ?? "analysis-completed";
        var subscription = config["ServiceBus:AnalysisCompletedSubscription"] ?? "document-api";

        _processor = new ServiceBusMessageProcessor<AnalysisCompletedEvent>(
            client,
            logger,
            entityName: topic,
            subscriptionName: subscription, // topic + subscription
            handler: async (evt, ct) =>
            {
                using var scope = scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<AnalysisCompletedEventHandler>();
                await handler.HandleAsync(evt, ct);
            });
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => _processor.StartAsync(stoppingToken);
    public override Task StopAsync(CancellationToken cancellationToken) => _processor.StopAsync(cancellationToken);
    public override void Dispose() => _processor.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
