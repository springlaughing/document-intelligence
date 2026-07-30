using Microsoft.Extensions.Configuration;
using DocumentIntelligence.Contracts;
using DocumentIntelligence.Messaging;

namespace AnalysisService.Worker.Messaging
{
    public class AnalysisResultEventPublisher : IAnalysisResultEventPublisher
    {
        private readonly IMessagePublisher _bus;
        private readonly string _completedTopic;
        private readonly string _failedTopic;

        public AnalysisResultEventPublisher(
            IConfiguration config,
            IMessagePublisher bus)
        {
            _bus = bus;

            _completedTopic = config["AzureServiceBus:AnalysisCompletedTopic"] ?? "analysis-completed";
            _failedTopic = config["AzureServiceBus:AnalysisFailedTopic"] ?? "analysis-failed";
        }

        public Task PublishAsync(AnalysisCompletedEvent evt, CancellationToken ct = default)
        {
            return _bus.PublishAsync(_completedTopic, evt, ct);
        }

        public Task PublishAsync(AnalysisFailedEvent evt, CancellationToken ct = default)
        {
            return _bus.PublishAsync(_failedTopic, evt, ct);
        }
    }
}
