namespace DocumentService.Api.Dtos;

// What the client sends (body). documentId steht bereits in der URL als Route-Parameter.
public record AnalyzeDocumentDto(string FileName);
