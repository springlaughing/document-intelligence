using System.Threading;
using System.Threading.Tasks;
using DocumentIntelligence.Contracts;

namespace DocumentService.Api.Features.Documents.RequestAnalysis
{
    public interface IAnalyzeDocumentCommandPublisher
    {
        Task PublishAsync(AnalyzeDocumentCommand cmd, CancellationToken ct = default);
    }
}
