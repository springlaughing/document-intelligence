using System.Threading;
using System.Threading.Tasks;
using DocumentIntelligence.Contracts;

namespace AnalysisService.Worker.Messaging;

public interface IAnalysisResultEventPublisher
{
    Task PublishAsync(AnalysisCompletedEvent evt, CancellationToken ct);

    Task PublishAsync(AnalysisFailedEvent evt, CancellationToken ct);
}
