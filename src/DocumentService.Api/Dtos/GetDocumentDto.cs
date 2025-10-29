
namespace DocumentService.Api.Dtos;

public record GetDocumentDto(
    Guid DocumentId,
    string FileName,
    string Status,
    string? AnalysisSummary,
    string? AnalysisBlobRef
);
