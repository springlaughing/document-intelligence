using System.Text.Json;
using Azure.Messaging.ServiceBus;
using DocumentIntelligence.Contracts.Contracts;
using AnalysisService.Worker.Outbound;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AnalysisService.Worker.Messaging;

// Reads AnalyzeDocumentCommand messages, performs analysis, and POSTs results back to the API.
public class AzureServiceBusConsumer : BackgroundService
{
    private readonly ServiceBusProcessor _processor;
    private readonly ILogger<AzureServiceBusConsumer> _logger;
    private readonly AnalysisResultPublisher _publisher;

    public AzureServiceBusConsumer(
        IConfiguration config,
        ILogger<AzureServiceBusConsumer> logger,
        AnalysisResultPublisher publisher)
    {
        _logger = logger;
        _publisher = publisher;

        var connectionString = config["ServiceBus:ConnectionString"]
            ?? throw new InvalidOperationException("Missing ServiceBus:ConnectionString");

        var queueName = config["ServiceBus:QueueName"]
            ?? "analyze-document";

        var client = new ServiceBusClient(connectionString);

        _processor = client.CreateProcessor(queueName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false
        });

        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            var json = args.Message.Body.ToString();
            var cmd = JsonSerializer.Deserialize<AnalyzeDocumentCommand>(json);

            if (cmd != null)
            {
                _logger.LogInformation("Handling AnalyzeDocumentCommand for {DocumentId}", cmd.DocumentId);

                var result = new AnalysisResultDto(
                    DocumentId: cmd.DocumentId,
                    Summary: $"Auto summary for {cmd.FileName}",
                    ExtractedEntities: new[] { "InvoiceNo:12345", "Amount:99.99" }
                );

                await _publisher.PublishResultAsync(result, CancellationToken.None);

                _logger.LogInformation("Published result for {DocumentId}", cmd.DocumentId);
            }

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message");
            // TODO: dead-letter handling etc.
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Service Bus processing error");
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Service Bus consumer...");
        await _processor.StartProcessingAsync(stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);

        _logger.LogInformation("Stopping Service Bus consumer...");
        await _processor.StopProcessingAsync(stoppingToken);
    }
}
