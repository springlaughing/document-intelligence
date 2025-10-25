using System.Collections.Concurrent;
using DocumentIntelligence.Contracts.Messaging;

namespace DocumentService.Api.Messaging;

// This is a super simple in-memory message queue for dev mode.
// For now it's only enqueue. The worker will have a consumer that reads from a shared queue.
// We'll refine that when we wire up the worker.
public class InMemoryMessageBus : IMessageBus
{
    // We'll keep a static queue so API and Worker (running in same process in dev)
    // COULD theoretically share. Later we will make the worker consume this.
    private static readonly ConcurrentQueue<object> _messages = new();

    public Task PublishAsync<T>(T message, CancellationToken ct = default)
    {
        _messages.Enqueue(message!);
        return Task.CompletedTask;
    }

    // Helper so the worker can poll messages.
    public static bool TryDequeue(out object? message)
    {
        return _messages.TryDequeue(out message);
    }
}
