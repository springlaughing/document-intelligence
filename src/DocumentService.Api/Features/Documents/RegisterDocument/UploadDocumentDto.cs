namespace DocumentService.Api.Features.Documents.RegisterDocument;

public record UploadDocumentDto(
    Guid DocumentId,
    string FileName
);
