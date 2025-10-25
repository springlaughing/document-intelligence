using DocumentService.Api.Infrastructure.Repositories;

namespace DocumentService.Api.Endpoints.GetDocumentResult;

public static class GetDocumentResultEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/documents/{documentId:guid}",
            async (
                Guid documentId,
                IDocumentRepository repo,
                CancellationToken ct) =>
            {
                var doc = await repo.GetAsync(documentId, ct);

                if (doc is null)
                {
                    return Results.NotFound(new
                    {
                        message = "Document not found",
                        documentId
                    });
                }

                var response = new GetDocumentResultResponse(
                    DocumentId: doc.Id,
                    FileName: doc.FileName,
                    Status: doc.Status,
                    AnalysisSummary: doc.AnalysisSummary,
                    ExtractedEntities: doc.ExtractedEntities
                );

                return Results.Ok(response);
            })
            .WithName("GetDocumentResult")
            .WithSummary("Get the status and analysis result of a document")
            .WithDescription("Returns current document status (e.g. Uploaded, Analyzing, Analyzed) plus any extracted info.");
    }
}
