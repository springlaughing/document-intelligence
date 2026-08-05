using DocumentService.Api.Domain;

namespace DocumentService.Api.Infrastructure.Repositories;

public interface IDocumentRepository
{
    Task<bool> ExistsAsync(Guid documentId, CancellationToken ct = default);

    Task<bool> CreateIfNotExistsAsync(Guid documentId, string fileName, CancellationToken ct = default);

    Task<bool> SetStatusAsync(Guid documentId, DocumentStatus status, CancellationToken ct = default);

    Task<DocumentRecord?> GetAsync(Guid documentId, CancellationToken ct = default);
    // Applies an analysis outcome at most once per event.
    //
    // Service Bus delivers at least once, so these are the entry points that have to
    // tolerate seeing the same event twice. The outcome distinguishes the two ways
    // nothing happens, because they demand opposite responses: a duplicate is routine
    // and the message should be completed, while an unknown document is a symptom and
    // the message should be kept for inspection.
    //
    // Status is passed explicitly: mapping an analysis outcome onto a lifecycle state
    // is a decision for the caller, not a storage default.
    Task<ApplyOutcome> ApplyAnalysisResultAsync(
        Guid eventId, Guid documentId, string summary, string blobReference,
        DocumentStatus status, CancellationToken ct = default);

    Task<ApplyOutcome> ApplyAnalysisFailureAsync(
        Guid eventId, Guid documentId, DocumentStatus status, CancellationToken ct = default);

    // Moves the document to the given status and queues the message that announces it,
    // in one transaction. Callers must not publish separately: the whole point is that
    // there is no second write that can fail on its own.
    Task<bool> TryStartAnalysisAsync(
        Guid documentId, DocumentStatus status, OutboxEnqueue message, CancellationToken ct = default);

    // ---- reconciliation ----
    //
    // The outbox and the inbox both react to messages, so neither can notice a message
    // that never arrives. These read state instead, which is the only way to detect an
    // absence. See ADR 0004.

    // Documents that entered Analyzing before the cutoff and have not moved since.
    Task<IReadOnlyList<StuckAnalysis>> FindStuckAnalysesAsync(
        DateTimeOffset startedBeforeUtc, int limit, CancellationToken ct = default);

    // Re-queues analysis and counts the attempt, in one transaction.
    //
    // expectedAttempts is a compare-and-swap: the caller passes what it saw when it
    // decided to act, and the write only lands if that is still true. Replicas all run
    // their own sweep, so two can select the same document; without this both would
    // increment and the limit would be reached at twice the intended rate. Returns false
    // when another writer got there first - a routine outcome, not an error.
    Task<bool> TryRetryAnalysisAsync(
        Guid documentId, int expectedAttempts, OutboxEnqueue message, CancellationToken ct = default);

    // Gives up on a document whose attempts are exhausted. Same compare-and-swap.
    Task<bool> TryFailStuckAnalysisAsync(
        Guid documentId, int expectedAttempts, string reason, CancellationToken ct = default);
}

// A candidate for reconciliation: enough to decide what to do without loading the row.
public record StuckAnalysis(
    Guid Id,
    string FileName,
    int Attempts,
    DateTimeOffset? StartedAtUtc);

// A message to be published, already serialized. The repository stores it; deciding
// what to send and where belongs to the feature that wants it sent.
public record OutboxEnqueue(string EntityName, string MessageType, string Payload);

public enum ApplyOutcome
{
    Applied,

    // The event had already been applied - at-least-once delivery working as designed.
    AlreadyApplied,

    // The event refers to a document that does not exist. Retrying cannot help.
    DocumentNotFound
}

//internal projection type (not EF entity, not the HTTP DTO).
public record DocumentRecord(
    Guid Id,
    string FileName,
    DocumentStatus Status,
    string? AnalysisSummary,
    string? AnalysisBlobRef

    
);
