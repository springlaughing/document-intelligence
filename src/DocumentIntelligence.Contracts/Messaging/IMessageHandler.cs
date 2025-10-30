namespace DocumentIntelligence.Contracts.Messaging;

public interface IMessageHandler<T>
{
    Task HandleAsync(T message, CancellationToken ct = default);
}
