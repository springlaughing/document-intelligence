namespace DocumentService.Api.Infrastructure.Outbox;

// Drains the outbox on a timer: reads messages the application committed but has not yet
// published, sends them, and records that it did.
//
// Scheduling only - the work itself lives in OutboxDrainer.
public sealed class OutboxRelay : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxRelay> _logger;

    private readonly TimeSpan _pollInterval;
    private readonly int _batchSize;

    public OutboxRelay(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<OutboxRelay> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        _pollInterval = TimeSpan.FromSeconds(config.GetValue("Outbox:PollIntervalSeconds", 5));
        _batchSize = config.GetValue("Outbox:BatchSize", 20);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox relay started; polling every {Interval}.", _pollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var drainer = scope.ServiceProvider.GetRequiredService<OutboxDrainer>();

                var sent = await drainer.DrainAsync(_batchSize, stoppingToken);

                // Only wait when there was nothing left to do. A full batch probably means
                // more is queued, so come straight back for it.
                if (sent < _batchSize)
                    await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // shutting down
            }
            catch (Exception ex)
            {
                // Never let the loop die. A stalled relay is a silently stalled system:
                // documents sit in Analyzing and no command is ever sent.
                _logger.LogError(ex, "Outbox relay pass failed; retrying after {Interval}.", _pollInterval);

                try { await Task.Delay(_pollInterval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        _logger.LogInformation("Outbox relay stopped.");
    }
}
