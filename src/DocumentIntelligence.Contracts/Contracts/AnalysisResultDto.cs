namespace DocumentIntelligence.Contracts.Contracts;

public record AnalysisResultDto(Guid DocumentId, string Summary, string[] ExtractedEntities);
