namespace AnalysisService.Worker.Infrastructure;

// Where analysis output is kept - and, because of that, how this service knows what it
// has already done.
//
// The store is keyed by CommandId rather than DocumentId, which is what makes the
// distinction the service needs: a redelivered command asks for a key that already
// exists, while a fresh request to re-analyse the same document brings a new one. That
// turns "have I done this?" into a lookup against storage already in the path, with no
// database and no state held in the service itself.
//
// This is why the interface reads as well as writes. A writer that also answers
// questions about what it wrote is not a writer.
public interface IAnalysisResultStore
{
    /// Null when nothing has been stored for this command.
    Task<StoredAnalysis?> TryGetAsync(Guid commandId, CancellationToken ct = default);

    Task<StoredAnalysis> SaveAsync(
        Guid commandId, Guid documentId, string summary, string[] extractedEntities,
        CancellationToken ct = default);
}

// The stored result, plus where it lives. The summary travels with it because a skipped
// re-run still has to publish the event describing what the analysis found - and if it
// skipped the work, the store is the only place that answer exists.
public record StoredAnalysis(string BlobReference, string Summary, string[] ExtractedEntities);
