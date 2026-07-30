using System.Collections.Concurrent;
using System.Text.Json;
using DocumentIntelligence.Contracts.Messaging;
using Azure.Messaging.ServiceBus;

namespace DocumentService.Api.Messaging;
//  Low-level, generic Service Bus publisher
// Sends messages (commands) to a queue/topic in Azure Service Bus or the local Service Bus Emulator.
public class AzureServiceBusPublisher : IMessagePublisher, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly JsonSerializerOptions _jsonOpt;
    // Registered as a singleton and shared across concurrent requests,
    // so the sender cache must be thread-safe.
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();


    public AzureServiceBusPublisher(ServiceBusClient client, JsonSerializerOptions jsonOpt)
    {
        _client = client;
        _jsonOpt = jsonOpt;
    }

    public async Task PublishAsync<T>(string entityName, T message, CancellationToken ct = default)
    {
        var sender = _senders.GetOrAdd(entityName, static (name, client) => client.CreateSender(name), _client);

        var payload = JsonSerializer.Serialize(message, _jsonOpt);
        var busMessage = new ServiceBusMessage(payload)
        {
            ContentType = "application/json",
            Subject = typeof(T).Name
        };

        await sender.SendMessageAsync(busMessage, ct);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
            await sender.DisposeAsync();

    }
}
