using DocumentIntelligence.Contracts.Contracts;

namespace DocumentService.Api.Endpoints.Internal;

// payload that the worker sends back to the API
public record PostAnalysisResultRequest(
    string Summary,
    string[] ExtractedEntities
)
{
    public AnalysisResultDto ToDto(Guid documentId) =>
        new(
            DocumentId: documentId,
            Summary: Summary,
            ExtractedEntities: ExtractedEntities
        );
}
