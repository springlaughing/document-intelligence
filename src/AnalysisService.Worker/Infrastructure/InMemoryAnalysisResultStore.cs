using System.Collections.Concurrent;

namespace AnalysisService.Worker.Infrastructure;

// Stand-in for Azure Blob Storage.
//
// A real implementation would write to a container and answer TryGetAsync with a HEAD
// request - which is the whole point of the design, since object storage is durable,
// shared between instances, and already in the path.
//
// This one is a dictionary, so be clear about what that costs: deduplication only holds
// within a single process for as long as it runs. Restart the service, or run a second
// instance, and a redelivered command will be analysed again. That is a limitation of
// the placeholder, not of the approach.
public class InMemoryAnalysisResultStore : IAnalysisResultStore
{
    private readonly ConcurrentDictionary<Guid, StoredAnalysis> _results = new();

    public Task<StoredAnalysis?> TryGetAsync(Guid commandId, CancellationToken ct = default) =>
        Task.FromResult(_results.TryGetValue(commandId, out var stored) ? stored : null);

    public Task<StoredAnalysis> SaveAsync(
        Guid commandId,
        Guid documentId,
        string summary,
        string[] extractedEntities,
        CancellationToken ct = default)
    {
        // Partitioned by document so results for one document sit together, keyed by
        // command so each run is its own object rather than overwriting the last.
        var reference = $"blob://analysis-results/{documentId}/{commandId}.json";

        var stored = new StoredAnalysis(reference, summary, extractedEntities);
        _results[commandId] = stored;

        return Task.FromResult(stored);
    }
}
