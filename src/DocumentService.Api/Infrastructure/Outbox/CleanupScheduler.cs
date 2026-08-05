namespace DocumentService.Api.Infrastructure.Outbox;

// Runs OldMessageCleaner on a long interval. Scheduling only.
public sealed class CleanupScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CleanupScheduler> _logger;

    private readonly TimeSpan _interval;
    private readonly TimeSpan _retention;

    public CleanupScheduler(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<CleanupScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        _interval = TimeSpan.FromHours(config.GetValue("Messaging:CleanupIntervalHours", 6));

        // Default is days against a message TTL of one hour, so the guard long outlives
        // any message that could still arrive.
        _retention = TimeSpan.FromDays(config.GetValue("Messaging:RetentionDays", 7));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Old message cleanup every {Interval}, keeping {Retention}.", _interval, _retention);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var cleaner = scope.ServiceProvider.GetRequiredService<OldMessageCleaner>();

                await cleaner.CleanAsync(_retention, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Housekeeping failing is not worth taking anything else down for.
                _logger.LogError(ex, "Old message cleanup failed; will retry next cycle.");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
