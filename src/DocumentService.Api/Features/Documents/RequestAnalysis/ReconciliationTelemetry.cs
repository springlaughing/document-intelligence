using System.Diagnostics.Metrics;

namespace DocumentService.Api.Features.Documents.RequestAnalysis;

// Counters for the reconciliation sweep.
//
// These matter more than the repair itself. A loop that quietly fixes a rising number of
// documents looks identical, from the outside, to a loop with nothing to do - which is how
// a systemic upstream break stays invisible for a month. The point of publishing these is
// that "requeued" trending upward is an alert, not a success story.
public static class ReconciliationTelemetry
{
    public const string MeterName = "DocumentService.Api.Reconciliation";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Requeued = Meter.CreateCounter<long>(
        "reconciliation.documents.requeued",
        unit: "{document}",
        description: "Stuck documents whose analysis was queued again.");

    public static readonly Counter<long> Abandoned = Meter.CreateCounter<long>(
        "reconciliation.documents.abandoned",
        unit: "{document}",
        description: "Documents moved to Failed after exhausting their analysis attempts.");

    public static readonly Counter<long> Contended = Meter.CreateCounter<long>(
        "reconciliation.documents.contended",
        unit: "{document}",
        description: "Candidates another writer had already handled. Expected with several replicas.");
}
