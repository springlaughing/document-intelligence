using System.Text.Json;
using DocumentIntelligence.Contracts.Messaging;
using Azure.Messaging.ServiceBus;

namespace AnalysisService.Worker.Messaging;
//  Low-level, generic Service Bus publisher
// Sends messages (commands) to a queue/topic in Azure Service Bus or the local Service Bus Emulator.
public class AzureServiceBusPublisher : IMessagePublisher, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly Dictionary<string, ServiceBusSender> _senders = new();


    public AzureServiceBusPublisher(ServiceBusClient client)
    {
        _client = client;
    }

    public async Task PublishAsync<T>(string entityName, T message, CancellationToken ct = default)
    {
        if (!_senders.TryGetValue(entityName, out var sender))
        {
            sender = _client.CreateSender(entityName);
            _senders[entityName] = sender;
        }

        var payload = JsonSerializer.Serialize(message);
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
