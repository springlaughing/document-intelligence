namespace DocumentService.Api.Features.Documents.GetDocument;

public record GetDocumentDto(
    Guid DocumentId,
    string FileName,
    string Status,
    string? AnalysisSummary,
    string? AnalysisBlobRef
);
