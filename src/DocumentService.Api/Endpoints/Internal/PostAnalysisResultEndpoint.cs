using DocumentService.Api.Infrastructure.Repositories;

namespace DocumentService.Api.Endpoints.Internal;

public static class PostAnalysisResultEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // internal callback used by AnalysisService.Worker
        app.MapPost("/internal/documents/{documentId:guid}/analysisResult",
            async (
                Guid documentId,
                PostAnalysisResultRequest body,
                IDocumentRepository repo,
                CancellationToken ct) =>
            {
                // apply result
                await repo.ApplyAnalysisResultAsync(
                    documentId,
                    body.Summary,
                    body.ExtractedEntities,
                    ct
                );

                return Results.NoContent();
            })

            .WithName("PostAnalysisResult")
            .WithSummary("Internal endpoint for worker to push analysis results")
            .WithDescription("This endpoint is not for end-users. The AnalysisService.Worker calls it after finishing document analysis.");
    }
}
