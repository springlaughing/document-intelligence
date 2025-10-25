namespace DocumentIntelligence.Contracts.Messaging;

public interface IMessageBus
{
    Task PublishAsync<T>(T message, CancellationToken ct = default);
}
