using Azure.Messaging.ServiceBus;
using DocumentIntelligence.Contracts.Contracts;

namespace AnalysisService.Worker.Messaging;

public class ServiceBusAnalysisResultEventPublisher : IAnalysisResultEventPublisher
{
    private readonly ServiceBusSender _sender;

    public ServiceBusAnalysisResultEventPublisher(ServiceBusClient client)
    {
        // e.g. topic or queue name for analysis-completed events
        _sender = client.CreateSender("analysis-completed");
    }

    public async Task PublishAsync(AnalysisCompletedEvent evt, CancellationToken ct)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(evt);
        var message = new ServiceBusMessage(payload)
        {
            ContentType = "application/json"
        };

        await _sender.SendMessageAsync(message, ct);
    }
        public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();

    }
}
