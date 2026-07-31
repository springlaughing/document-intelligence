using System.Security.Cryptography;
using System.Text;

namespace AnalysisService.Worker.Messaging;

// Derives a stable id from another id.
//
// This service is stateless by design: it has no database, which is what lets any
// instance take any message and lets it scale by simply running more copies. The cost is
// that it cannot remember which commands it has already handled.
//
// Rather than adding a store to prevent duplicate work, it makes duplicate work
// harmless - the same command always yields the same event id, so the consumer's inbox
// recognises the second one and applies nothing. The work is repeated; the effect is not.
//
// That trade holds while analysis is cheap. If it ever becomes expensive, repeating it
// stops being acceptable and this service needs a real inbox of its own.
internal static class DeterministicId
{
    public static Guid From(Guid source, string purpose)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{source:N}:{purpose}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
