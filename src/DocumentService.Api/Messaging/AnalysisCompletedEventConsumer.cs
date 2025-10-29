using System.Text.Json;
using Azure.Messaging.ServiceBus;
using DocumentIntelligence.Contracts.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DocumentService.Api.Messaging;

public class AnalysisCompletedEventConsumer : BackgroundService
{
    private readonly ServiceBusProcessor _processor;
    private readonly AnalysisCompletedEventHandler _handler;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnalysisCompletedEventConsumer> _logger;

    public AnalysisCompletedEventConsumer(
        IConfiguration config,
        ServiceBusClient client,
        AnalysisCompletedEventHandler handler,
         IServiceScopeFactory scopeFactory,
        ILogger<AnalysisCompletedEventConsumer> logger)
    {
        _handler = handler;
        _logger = logger;
        _scopeFactory = scopeFactory;

        // z.B. Topic "analysis-completed", Subscription "document-api"
        var topicName = config["ServiceBus:AnalysisCompletedTopic"] ?? "analysis-completed";
        var subscriptionName = config["ServiceBus:AnalysisCompletedSubscription"] ?? "document-api";

        _processor = client.CreateProcessor(topicName, subscriptionName);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        return _processor.StartProcessingAsync(stoppingToken);
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            var body = args.Message.Body.ToString();
            var evt = JsonSerializer.Deserialize<AnalysisCompletedEvent>(body);

            if (evt != null)
            {
                using var scope = _scopeFactory.CreateScope();

                var handler = scope.ServiceProvider
                    .GetRequiredService<AnalysisCompletedEventHandler>();

                await handler.HandleAsync(evt, CancellationToken.None);
            }

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process AnalysisCompletedEvent");
            // wenn du willst: args.AbandonMessageAsync(...) für Retry
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Service Bus error in AnalysisCompletedEventConsumer");
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _processor.StopProcessingAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}
