using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AnalysisService.Worker.Messaging;
public sealed class ServiceBusMessageProcessor<T> : IAsyncDisposable
{
    private readonly ServiceBusProcessor _processor;
    private readonly Func<T, CancellationToken, Task> _handler;
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _json;
    private readonly bool _useTopic;

    public ServiceBusMessageProcessor(
        ServiceBusClient client,
        ILogger<ServiceBusMessageProcessor<T>> logger,
        // when subscriptionName is null => use queue; otherwise topic+subscription
        string entityName,
        string? subscriptionName,
        Func<T, CancellationToken, Task> handler,
        ServiceBusProcessorOptions? options = null,
        JsonSerializerOptions? json = null)
    {
        _logger = logger;
        _handler = handler;
        _json = json ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);

        _useTopic = subscriptionName is not null;
        _processor = _useTopic
            ? client.CreateProcessor(entityName, subscriptionName!, options ?? DefaultOptions())
            : client.CreateProcessor(entityName, options ?? DefaultOptions());

        _processor.ProcessMessageAsync += HandleAsync;
        _processor.ProcessErrorAsync += OnErrorAsync;
    }

    private static ServiceBusProcessorOptions DefaultOptions() => new()
    {
        AutoCompleteMessages = false,
        MaxConcurrentCalls = Environment.ProcessorCount,
        PrefetchCount = 50
    };

    private async Task HandleAsync(ProcessMessageEventArgs args)
    {
        try
        {
            // safer than ToString(); respects content encoding
            var body = args.Message.Body.ToStream();
            var payload = await JsonSerializer.DeserializeAsync<T>(body, _json, args.CancellationToken);
            if (payload is null)
            {
                _logger.LogWarning("Unable to deserialize {Type} - dead-lettering.", typeof(T).Name);
                await args.DeadLetterMessageAsync(args.Message, "DeserializationFailed", "Could not deserialize payload");
                return;
            }

            await _handler(payload, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        }
        catch (OperationCanceledException) when (args.CancellationToken.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling {Type}. DeliveryCount={Count}", typeof(T).Name, args.Message.DeliveryCount);

            // simple retry/DLQ policy: after N attempts, dead-letter
            if (args.Message.DeliveryCount >= 5)
                await args.DeadLetterMessageAsync(args.Message, "MaxDeliveryExceeded", ex.Message);
            else
                await args.AbandonMessageAsync(args.Message);
        }
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Service Bus processor error. Entity={Entity}, Source={Source}", args.EntityPath, args.ErrorSource);
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken stoppingToken) => _processor.StartProcessingAsync(stoppingToken);

    public Task StopAsync(CancellationToken cancellationToken) => _processor.StopProcessingAsync(cancellationToken);
    

    public async ValueTask DisposeAsync()
    {
        _processor.ProcessMessageAsync -= HandleAsync;
        _processor.ProcessErrorAsync -= OnErrorAsync;
        await _processor.DisposeAsync();
    }
}
