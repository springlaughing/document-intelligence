using System.Text.Json;
using DocumentIntelligence.Contracts;
using DocumentService.Api.Features.Documents.RequestAnalysis;
using Microsoft.Extensions.Configuration;

public class AnalyzeDocumentCommandQueueTests
{
    private static AnalyzeDocumentCommandQueue CreateSut(string? configuredQueue)
    {
        var settings = new Dictionary<string, string?>();
        if (configuredQueue is not null)
            settings["AzureServiceBus:AnalyzeDocumentQueueName"] = configuredQueue;

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new AnalyzeDocumentCommandQueue(
            config, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    [Fact]
    public void Prepare_targets_the_configured_queue_and_round_trips_the_command()
    {
        var cmd = new AnalyzeDocumentCommand(Guid.NewGuid(), "invoice.pdf");

        var enqueued = CreateSut("analyze-document").Prepare(cmd);

        Assert.Equal("analyze-document", enqueued.EntityName);
        Assert.Equal(nameof(AnalyzeDocumentCommand), enqueued.MessageType);

        // The payload is what the worker will actually receive, so it has to deserialize
        // back into the same command with the same serializer settings.
        var roundTripped = JsonSerializer.Deserialize<AnalyzeDocumentCommand>(
            enqueued.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(cmd, roundTripped);
    }

    [Fact]
    public void Prepare_falls_back_to_the_default_queue_name()
    {
        var enqueued = CreateSut(null).Prepare(new AnalyzeDocumentCommand(Guid.NewGuid(), "a.pdf"));

        Assert.Equal("analyze-document", enqueued.EntityName);
    }
}
