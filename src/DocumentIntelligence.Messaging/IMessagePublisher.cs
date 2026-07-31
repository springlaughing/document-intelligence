namespace DocumentIntelligence.Messaging;

public interface IMessagePublisher
{
    Task PublishAsync<T>(string entityName, T message, CancellationToken ct = default);

    // Publishes a body that is already serialized. An outbox stores the payload at
    // enqueue time, so by the time it is sent there is nothing left to serialize -
    // and running it through the generic overload would encode the JSON a second time.
    Task PublishRawAsync(string entityName, string payload, string subject, CancellationToken ct = default);
}
