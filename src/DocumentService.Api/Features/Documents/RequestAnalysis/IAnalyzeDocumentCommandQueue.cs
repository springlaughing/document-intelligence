using DocumentIntelligence.Contracts;
using DocumentService.Api.Infrastructure.Repositories;

namespace DocumentService.Api.Features.Documents.RequestAnalysis;

// Prepares the command for the outbox rather than sending it.
//
// It deliberately does not publish: the command has to be written in the same
// transaction as the status change that justifies it, and only the repository owns that
// transaction. Publishing happens later, from the relay.
public interface IAnalyzeDocumentCommandQueue
{
    OutboxEnqueue Prepare(AnalyzeDocumentCommand cmd);
}
