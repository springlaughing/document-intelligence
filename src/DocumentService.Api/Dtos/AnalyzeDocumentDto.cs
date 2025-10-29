namespace DocumentService.Api.Dtos;

// what the client sends (body). documentId steht bereits in der URL als Route-Parameter.
public record AnalyzeDocumentDto(string FileName);
