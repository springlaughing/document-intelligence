using DocumentIntelligence.Contracts.Contracts;
using DocumentIntelligence.Contracts.DomainContracts;
using DocumentIntelligence.Contracts.Messaging;
using DocumentService.Api.Infrastructure.Repositories;

namespace DocumentService.Api.Endpoints.AnalyzeDocument;

public static class AnalyzeDocumentEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/documents/{documentId:guid}:analyze",
            async (
                Guid documentId,
                AnalyzeDocumentRequest request,
                IDocumentRepository repo,
                IMessageBus bus,
                CancellationToken ct) =>
            {
                // ensure doc exists (idempotent-ish create)
                await repo.CreateIfNotExistsAsync(documentId, request.FileName, ct);

                // mark status -> Analyzing
                await repo.SetStatusAsync(documentId, DocumentStatus.Analyzing, ct);

                // publish command to the "queue"
                var command = new AnalyzeDocumentCommand(
                    DocumentId: documentId,
                    FileName: request.FileName
                );

                await bus.PublishAsync(command, ct);

                // tell client we accepted the job
                return Results.Accepted($"/api/documents/{documentId}/status");
            })
            .WithName("AnalyzeDocument")
            .WithSummary("Triggers background analysis for a document")
            .WithDescription("Marks the document as 'Analyzing', enqueues a AnalyzeDocumentCommand for the worker, and returns 202 Accepted.");
    }
}
