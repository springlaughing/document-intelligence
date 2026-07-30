namespace DocumentIntelligence.Contracts;

// Counterpart to AnalysisCompletedEvent. Without it a failed analysis leaves the
// document stuck in its "analyzing" state forever.
public record AnalysisFailedEvent(
    Guid DocumentId,
    string Reason
);
