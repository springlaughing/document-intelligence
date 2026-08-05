using DocumentIntelligence.Contracts;
using DocumentIntelligence.Messaging;
using Azure.Messaging.ServiceBus;
using System.Text.Json;


namespace AnalysisService.Worker.Messaging;
public sealed class AnalyzeDocumentCommandListener : BackgroundService, IAsyncDisposable
{
    private readonly ServiceBusMessageDispatcher<AnalyzeDocumentCommand> _dispatcher;
    private int _dispatcherDisposed;

    public AnalyzeDocumentCommandListener(
        IConfiguration config,
        ServiceBusClient client,
        ILogger<ServiceBusMessageDispatcher<AnalyzeDocumentCommand>> logger,
        IServiceScopeFactory scopeFactory,
        JsonSerializerOptions jsonOpt)
    {
        var queueName = config["AzureServiceBus:AnalyzeDocumentQueueName"] ?? "analyze-document";

        // per-message handler wired via scope
        _dispatcher = new ServiceBusMessageDispatcher<AnalyzeDocumentCommand>(
            client,
            scopeFactory,
            logger,
            entityName: queueName,
            subscriptionName: null,
            options: null,
            jsonOpt: jsonOpt
            );
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
