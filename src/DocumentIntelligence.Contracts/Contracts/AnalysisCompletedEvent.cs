namespace DocumentIntelligence.Contracts.Contracts;

// An outcome, not a lifecycle state: this says *what happened* in the analysis
// service. Mapping the outcome onto a document status is the API's own decision,
// so no DocumentStatus travels over the wire.
public record AnalysisCompletedEvent(
    Guid DocumentId,
    string Summary,
    string BlobReference // points to where detailed results are stored
);
