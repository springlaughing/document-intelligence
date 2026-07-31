using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DocumentIntelligence.Messaging;
public sealed class ServiceBusMessageDispatcher<T> : IAsyncDisposable
{
    private readonly ServiceBusProcessor _processor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _jsonOpt;

    public ServiceBusMessageDispatcher(
        ServiceBusClient client,
        IServiceScopeFactory scopeFactory,
        ILogger<ServiceBusMessageDispatcher<T>> logger,
        string entityName,
        string? subscriptionName,
        ServiceBusProcessorOptions? options = null,
        JsonSerializerOptions? jsonOpt = null)
    {
          _scopeFactory = scopeFactory;
        _logger = logger;
        _jsonOpt = jsonOpt ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);

        _processor = subscriptionName is not null
            ? client.CreateProcessor(entityName, subscriptionName, options ?? DefaultOptions())
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
            await using var body = args.Message.Body.ToStream();
            var payload = await JsonSerializer.DeserializeAsync<T>(body, _jsonOpt, args.CancellationToken);
            if (payload is null)
            {
                _logger.LogWarning("Unable to deserialize {Type} - dead-lettering.", typeof(T).Name);
                await args.DeadLetterMessageAsync(
                    args.Message, "DeserializationFailed", "Could not deserialize payload", args.CancellationToken);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<IMessageHandler<T>>();
            await handler.HandleAsync(payload, args.CancellationToken);

            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        }
        catch (OperationCanceledException) when (args.CancellationToken.IsCancellationRequested)
        {
            // shutting down
        }
        catch (UnprocessableMessageException ex)
        {
            // The handler has told us retrying is pointless. Dead-letter immediately
            // rather than burning five deliveries to reach the same place with a less
            // informative reason.
            _logger.LogError(ex,
                "{Type} cannot be processed - dead-lettering immediately.", typeof(T).Name);

            await args.DeadLetterMessageAsync(
                args.Message, "Unprocessable", Truncate(ex.Message, 4000), args.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling {Type}. DeliveryCount={Count}", typeof(T).Name, args.Message.DeliveryCount);

            // retry/DLQ policy: after N attempts, dead-letter
            if (args.Message.DeliveryCount >= 5)
                await args.DeadLetterMessageAsync(
                    args.Message, "MaxDeliveryExceeded", Truncate(ex.Message, 4000), args.CancellationToken);
            else
                await args.AbandonMessageAsync(args.Message);
        }
    }

    // Service Bus rejects an over-long dead-letter description, which would turn a
    // clean dead-letter into an exception inside the error path.
    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

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
