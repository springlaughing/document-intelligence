using System.Threading;
using System.Threading.Tasks;
using DocumentIntelligence.Contracts.Contracts;

namespace AnalysisService.Worker.Messaging;

public interface IAnalysisResultEventPublisher
{
    Task PublishAsync(AnalysisCompletedEvent evt, CancellationToken ct);
}
