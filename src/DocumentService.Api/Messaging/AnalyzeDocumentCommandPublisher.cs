using Microsoft.Extensions.Configuration;
using DocumentIntelligence.Contracts.Contracts;
using DocumentIntelligence.Messaging;

namespace DocumentService.Api.Messaging
{
    public class AnalyzeDocumentCommandPublisher : IAnalyzeDocumentCommandPublisher
    {
        private readonly IMessagePublisher _bus;
        private readonly string _queueName;

        public AnalyzeDocumentCommandPublisher(
            IConfiguration config,
            IMessagePublisher bus)
        {
            _bus = bus;

            // Which queue/topic the API should send "analyze doc" commands to
            _queueName =
                config["AzureServiceBus:AnalyzeDocumentQueueName"]
                ?? "analyze-document";
        }

        public Task PublishAsync(AnalyzeDocumentCommand cmd, CancellationToken ct = default)
        {
            return _bus.PublishAsync(_queueName, cmd, ct);
        }
    }
}
