using DocumentIntelligence.Contracts.Contracts;
using DocumentIntelligence.Contracts.DomainContracts;
using DocumentService.Api.Infrastructure.Ef;
using Microsoft.EntityFrameworkCore;


namespace DocumentService.Api.Messaging;
public class AnalysisCompletedEventHandler
{
    private readonly DocumentApiDbContext _db;

    public AnalysisCompletedEventHandler(DocumentApiDbContext db)
    {
        _db = db;
    }

    public async Task HandleAsync(AnalysisCompletedEvent evt, CancellationToken ct)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == evt.DocumentId, ct);
        if (doc == null)
        {
            // optional: log warning
            return;
        }

        doc.Status = DocumentStatus.Analyzed;
        doc.AnalysisSummary = evt.Summary;
        doc.AnalysisBlobRef = evt.BlobReference;

        await _db.SaveChangesAsync(ct);
    }
}
