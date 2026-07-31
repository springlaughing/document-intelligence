namespace DocumentIntelligence.Contracts;

// Counterpart to AnalysisCompletedEvent. Without it a failed analysis leaves the
// document stuck in its "analyzing" state forever.
//
// See AnalysisCompletedEvent for why EventId exists.
public record AnalysisFailedEvent(
    Guid EventId,
    Guid DocumentId,
    string Reason
);
