using System.Diagnostics;

namespace DocumentService.Api.Infrastructure.Outbox;

// The outbox deliberately breaks the chain between the request that queued a message and
// the moment it is published - that decoupling is the point. It also breaks the trace,
// because the poller runs on its own schedule in its own ambient context.
//
// So the traceparent is stored with the message and restored here, which is what lets a
// single trace run from the HTTP request, through the queued command, across the broker,
// and into the worker.
public static class OutboxTelemetry
{
    public const string ActivitySourceName = "DocumentService.Api.Outbox";

    public static readonly ActivitySource Source = new(ActivitySourceName);
}
