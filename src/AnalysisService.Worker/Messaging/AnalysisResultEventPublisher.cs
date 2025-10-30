using Microsoft.Extensions.Configuration;
using DocumentIntelligence.Contracts.Contracts;
using DocumentIntelligence.Contracts.Messaging;

namespace AnalysisService.Worker.Messaging
{
    public class AnalysisResultEventPublisher : IAnalysisResultEventPublisher
    {
        private readonly IMessagePublisher _bus;
        private readonly string _topicName;

        public AnalysisResultEventPublisher(
            IConfiguration config,
            IMessagePublisher bus)
        {
            _bus = bus;

            // Where "analysis finished" events should go
            _topicName = config["AzureServiceBus:AnalysisCompletedTopic"] ?? "analysis-completed";
        }

        public Task PublishAsync(AnalysisCompletedEvent evt, CancellationToken ct = default)
        {
            return _bus.PublishAsync(_topicName, evt, ct);
        }
    }
}
