using DocumentIntelligence.Contracts.DomainContracts;

namespace DocumentService.Api.Endpoints.GetDocumentResult;

public record GetDocumentResultResponse(
    Guid DocumentId,
    string FileName,
    DocumentStatus Status,
    string? AnalysisSummary,
    string[]? ExtractedEntities
);