namespace DocumentIntelligence.Contracts.Contracts;

public record AnalyzeDocumentCommand(Guid DocumentId, string FileName);
