using System.Threading;
using System.Threading.Tasks;
using DocumentIntelligence.Contracts;
using DocumentIntelligence.Messaging;
using DocumentService.Api.Features.Documents.RequestAnalysis;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

public class AnalyzeDocumentCommandPublisherTests
{
    [Fact]
    public async Task Publish_sends_to_configured_queue()
    {
        var bus = new Mock<IMessagePublisher>();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string,string?>{
                ["AzureServiceBus:AnalyzeDocumentQueueName"] = "analyze-document"
            }).Build();

        var sut = new AnalyzeDocumentCommandPublisher(cfg, bus.Object);
        var cmd = new AnalyzeDocumentCommand(Guid.NewGuid(), "invoice.pdf");

        await sut.PublishAsync(cmd, CancellationToken.None);

        bus.Verify(b => b.PublishAsync("analyze-document", cmd, It.IsAny<CancellationToken>()), Times.Once);
    }
}