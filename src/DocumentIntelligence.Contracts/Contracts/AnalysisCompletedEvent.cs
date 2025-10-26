namespace DocumentIntelligence.Contracts.Contracts;

public record AnalysisCompletedEvent(
    Guid DocumentId,
    string Summary,
    string BlobReference // points to where detailed results are stored
);
