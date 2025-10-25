namespace DocumentIntelligence.Contracts.Messaging;

public interface IMessageConsumer<T>
{
    Task ConsumeAsync(T message, CancellationToken ct = default);
}
