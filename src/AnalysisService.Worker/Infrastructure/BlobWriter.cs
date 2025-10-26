using System.Text.Json;

namespace AnalysisService.Worker.Infrastructure;

public class BlobWriter : IBlobWriter
{
    public Task<string> SaveAsync(Guid documentId, object data, CancellationToken ct)
    {
        // In production: write 'data' (entities etc.) to Blob Storage and return blob URL.
        // For this prototype: just return a stable fake reference that *looks* like a blob path.
        var fakeRef = $"blob://analysis-results/{documentId}.json";
        return Task.FromResult(fakeRef);
    }
}