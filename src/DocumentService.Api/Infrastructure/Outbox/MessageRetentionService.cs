namespace DocumentService.Api.Infrastructure.Outbox;

// Runs MessageRetentionSweeper on a long interval. Scheduling only.
public sealed class MessageRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MessageRetentionService> _logger;

    private readonly TimeSpan _interval;
    private readonly TimeSpan _retention;

    public MessageRetentionService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<MessageRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        _interval = TimeSpan.FromHours(config.GetValue("Messaging:SweepIntervalHours", 6));

        // Default is days against a message TTL of one hour, so the guard long outlives
        // any message that could still arrive.
        _retention = TimeSpan.FromDays(config.GetValue("Messaging:RetentionDays", 7));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Message retention sweep every {Interval}, keeping {Retention}.", _interval, _retention);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sweeper = scope.ServiceProvider.GetRequiredService<MessageRetentionSweeper>();

                await sweeper.SweepAsync(_retention, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Housekeeping failing is not worth taking anything else down for.
                _logger.LogError(ex, "Message retention sweep failed; will retry next cycle.");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
