using System.Net.Http.Json;
using DocumentIntelligence.Contracts.Contracts;

namespace AnalysisService.Worker.Outbound;

// Responsible for sending the analysis result from the worker back to the API.
public class AnalysisResultPublisher
{
    private readonly HttpClient _httpClient;

    public AnalysisResultPublisher(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task PublishResultAsync(AnalysisResultDto result, CancellationToken ct)
    {
        // construct request to API
        var url = $"/internal/documents/{result.DocumentId}/analysisResult";

        var body = new
        {
            summary = result.Summary,
            extractedEntities = result.ExtractedEntities
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };

        // In dev we protect internal endpoints with a simple shared secret header.
        request.Headers.Add("X-Internal-Token", "dev-internal");

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
