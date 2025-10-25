namespace DocumentService.Api.Endpoints.AnalyzeDocument;

// what the client sends (body). documentId steht bereits in der URL als Route-Parameter.
public record AnalyzeDocumentRequest(string FileName);
