namespace DocumentIntelligence.Messaging;

public interface IMessagePublisher
{
    Task PublishAsync<T>(string entityName, T message, CancellationToken ct = default);

    // Publishes a body that is already serialized. An outbox stores the payload at
    // enqueue time, so by the time it is sent there is nothing left to serialize -
    // and running it through the generic overload would encode the JSON a second time.
    //
    // messageId, when supplied, becomes the broker's MessageId. On an entity with
    // duplicate detection enabled that lets the broker reject a resend, which is worth
    // doing precisely because an outbox relay can publish and then fail before recording
    // that it published.
    Task PublishRawAsync(
        string entityName, string payload, string subject, string? messageId, CancellationToken ct = default);
}
