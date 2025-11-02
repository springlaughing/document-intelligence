using System.Threading;
using System.Threading.Tasks;
using DocumentIntelligence.Contracts.Contracts; 


namespace DocumentService.Api.Messaging
{
    public interface IAnalyzeDocumentCommandPublisher
    {
        Task PublishAsync(AnalyzeDocumentCommand cmd, CancellationToken ct = default);
    }
}
