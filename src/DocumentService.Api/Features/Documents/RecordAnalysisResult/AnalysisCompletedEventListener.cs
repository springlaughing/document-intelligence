using System.Text.Json;
using Azure.Messaging.ServiceBus;
using DocumentIntelligence.Contracts;
using DocumentIntelligence.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DocumentService.Api.Features.Documents.RecordAnalysisResult;

public sealed class AnalysisCompletedEventListener : BackgroundService, IAsyncDisposable
{
    private readonly ServiceBusMessageDispatcher<AnalysisCompletedEvent> _dispatcher;
    private int _dispatcherDisposed;

    public AnalysisCompletedEventListener(
        IConfiguration config,
        ServiceBusClient client,
        ILogger<ServiceBusMessageDispatcher<AnalysisCompletedEvent>> logger,
        IServiceScopeFactory scopeFactory,
        JsonSerializerOptions jsonOpt)
    {
        var topic = config["AzureServiceBus:AnalysisCompletedTopic"] ?? "analysis-completed";
        var subscription = config["AzureServiceBus:AnalysisCompletedSubscription"] ?? "document-api";

        _dispatcher = new ServiceBusMessageDispatcher<AnalysisCompletedEvent>(
            client,
            scopeFactory,
            logger,
            entityName: topic,
            subscriptionName: subscription,
            options: null,
            jsonOpt: jsonOpt);

    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => _dispatcher.StartAsync(stoppingToken);

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop first so in-flight messages finish and settle, then release the link.
        // Disposing here keeps teardown on the asynchronous path the host always takes
        // for a graceful shutdown, which is what lets Dispose() avoid blocking on it.
        await _dispatcher.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
        await DisposeDispatcherAsync();
    }

    // Closing an AMQP link is genuinely asynchronous work. Blocking on it from a
    // synchronous Dispose risks deadlock and stalls shutdown, so this is the real
    // teardown path and the guard keeps it safe to reach twice.
    public async ValueTask DisposeAsync()
    {
        await DisposeDispatcherAsync();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeDispatcherAsync()
    {
        if (Interlocked.Exchange(ref _dispatcherDisposed, 1) == 1) return;
        await _dispatcher.DisposeAsync();
    }
}
