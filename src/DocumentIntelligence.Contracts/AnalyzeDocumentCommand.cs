namespace DocumentIntelligence.Contracts;

public record AnalyzeDocumentCommand(Guid DocumentId, string FileName);
