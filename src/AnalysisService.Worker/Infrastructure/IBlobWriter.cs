
namespace AnalysisService.Worker.Infrastructure;
public interface IBlobWriter
{
    Task<string> SaveAsync(Guid documentId, object data, CancellationToken ct);
}