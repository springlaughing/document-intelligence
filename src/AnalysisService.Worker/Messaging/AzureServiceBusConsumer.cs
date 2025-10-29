using System.Text.Json;
using Azure.Messaging.ServiceBus;
using DocumentIntelligence.Contracts.Contracts;
using DocumentIntelligence.Contracts.DomainContracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AnalysisService.Worker.Infrastructure;


namespace AnalysisService.Worker.Messaging;

// Reads AnalyzeDocumentCommand messages, performs analysis, 
// saves heavy data to Blob (or fake Blob) and publishes AnalysisCompletedEvent to the bus.
public class AzureServiceBusConsumer : BackgroundService
{
    private readonly ServiceBusProcessor _processor;
    private readonly ILogger<AzureServiceBusConsumer> _logger;
    private readonly IBlobWriter _blobWriter;
    private readonly IAnalysisResultEventPublisher _resultPublisher;

    public AzureServiceBusConsumer(
        IConfiguration config,
        ILogger<AzureServiceBusConsumer> logger,
        ServiceBusClient client,
        IBlobWriter blobWriter,
        IAnalysisResultEventPublisher resultPublisher)
    {
        _logger = logger;
        _blobWriter = blobWriter;
        _resultPublisher = resultPublisher;

        var queueName = config["ServiceBus:QueueName"]
            ?? "analyze-document";

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

                // here we call some service for analysis
                // Forn now, say these are entities we got from analysis 
                var extractedEntities = new[]
                {
                    "InvoiceNo:12345",
                    "Amount:99.99"
                };
                var summary = $"Auto summary for {cmd.FileName}";

                // Save extracted entities to blob/fake blob
                var blobRef = await _blobWriter.SaveAsync(cmd.DocumentId, extractedEntities, CancellationToken.None);

                // Publish AnalysisCompletedEvent (instead of HTTP callback)
                // If analysis fails, instead of publishing DocumentStatus.Analyzed I would still publish AnalysisCompletedEvent
                // but with DocumentStatus.Error and a short failure summary (e.g. ‘OCR timeout’).
                var evt = new AnalysisCompletedEvent(
                    cmd.DocumentId,
                    summary,
                    DocumentStatus.Analyzed,
                    blobRef
                );

                await _resultPublisher.PublishAsync(evt, CancellationToken.None);

                _logger.LogInformation("Published AnalysisCompletedEvent for {DocumentId}", cmd.DocumentId);
            }

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing AnalyzeDocumentCommand");
            // optionally: await args.DeadLetterMessageAsync(args.Message);
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

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException) { }

        _logger.LogInformation("Stopping Service Bus consumer...");
        await _processor.StopProcessingAsync(stoppingToken);
    }
}
