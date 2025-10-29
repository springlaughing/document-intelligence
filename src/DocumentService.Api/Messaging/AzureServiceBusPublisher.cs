using System.Text.Json;
using DocumentIntelligence.Contracts.Messaging;
using Azure.Messaging.ServiceBus;

namespace DocumentService.Api.Messaging;

// Sends messages (commands) to a queue/topic in Azure Service Bus or the local Service Bus Emulator.
public class AzureServiceBusPublisher : IMessageBus, IAsyncDisposable
{
    private readonly ServiceBusSender _sender;

    public AzureServiceBusPublisher(IConfiguration config, ServiceBusClient client)
    {

        var queueName = config["ServiceBus:QueueName"]
            ?? "analyze-document";

        _sender = client.CreateSender(queueName);
    }

    public async Task PublishAsync<T>(T message, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(message);
        var busMessage = new ServiceBusMessage(payload)
        {
            ContentType = "application/json",
            Subject = typeof(T).Name
        };

        await _sender.SendMessageAsync(busMessage, ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();

    }
}
