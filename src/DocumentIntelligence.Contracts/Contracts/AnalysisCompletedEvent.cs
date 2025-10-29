using DocumentIntelligence.Contracts.DomainContracts;

namespace DocumentIntelligence.Contracts.Contracts;

public record AnalysisCompletedEvent(
    Guid DocumentId,
    string Summary,
    DocumentStatus Status,
    string BlobReference // points to where detailed results are stored
);
