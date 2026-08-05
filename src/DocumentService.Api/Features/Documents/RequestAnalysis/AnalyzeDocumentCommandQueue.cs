using System.Text.Json;
using DocumentIntelligence.Contracts;
using DocumentService.Api.Infrastructure.Repositories;

namespace DocumentService.Api.Features.Documents.RequestAnalysis;

public class AnalyzeDocumentCommandQueue : IAnalyzeDocumentCommandQueue
{
    private readonly JsonSerializerOptions _jsonOpt;
    private readonly string _queueName;

    public AnalyzeDocumentCommandQueue(IConfiguration config, JsonSerializerOptions jsonOpt)
    {
        _jsonOpt = jsonOpt;

        // Which queue the API sends "analyze this document" commands to.
        _queueName =
            config["AzureServiceBus:AnalyzeDocumentQueueName"]
            ?? "analyze-document";
    }

    public OutboxEnqueue Prepare(AnalyzeDocumentCommand cmd) =>
        new(
            EntityName: _queueName,
            MessageType: nameof(AnalyzeDocumentCommand),
            Payload: JsonSerializer.Serialize(cmd, _jsonOpt));
}
