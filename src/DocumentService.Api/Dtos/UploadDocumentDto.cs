namespace DocumentService.Api.Dtos;

public record UploadDocumentDto(
    Guid DocumentId,
    string FileName
);