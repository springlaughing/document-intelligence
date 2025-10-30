namespace DocumentIntelligence.Contracts.Messaging;

public interface IMessagePublisher
{
    Task PublishAsync<T>(string entityName, T message, CancellationToken ct = default);
}
