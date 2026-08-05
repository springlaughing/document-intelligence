namespace DocumentService.Api.Features.Documents.RequestAnalysis;

// Runs StuckAnalysisReconciler on a timer. Scheduling only - the work lives in the
// reconciler, the same split as OutboxPoller/OutboxDrainer and CleanupScheduler/
// OldMessageCleaner.
public sealed class StuckAnalysisScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StuckAnalysisScheduler> _logger;

    private readonly TimeSpan _interval;
    private readonly TimeSpan _stuckAfter;
    private readonly int _maxAttempts;
    private readonly int _batchSize;

    // Seeded per instance, so replicas do not draw the same offsets.
    private readonly Random _jitter = new();

    public StuckAnalysisScheduler(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<StuckAnalysisScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        _interval = TimeSpan.FromMinutes(config.GetValue("Reconciliation:IntervalMinutes", 5));

        // Must comfortably exceed the worst-case *legitimate* analysis time. Set it too
        // low and the sweep re-queues documents that were merely slow, manufacturing the
        // duplicate work the inbox exists to absorb - a self-inflicted version of the
        // problem this is here to solve.
        _stuckAfter = TimeSpan.FromMinutes(config.GetValue("Reconciliation:StuckAfterMinutes", 15));

        _maxAttempts = config.GetValue("Reconciliation:MaxAttempts", 3);
        _batchSize = config.GetValue("Reconciliation:BatchSize", 20);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Stuck-analysis sweep every {Interval}, for documents Analyzing longer than "
            + "{StuckAfter}, giving up after {MaxAttempts} attempts.",
            _interval, _stuckAfter, _maxAttempts);

        // Nothing can be stuck the instant the process starts, and every replica starting
        // together would otherwise sweep in lockstep forever.
        if (!await DelayAsync(RandomizedInterval(), stoppingToken)) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var reconciler = scope.ServiceProvider.GetRequiredService<StuckAnalysisReconciler>();

                var pass = await reconciler.RunAsync(
                    _stuckAfter, _maxAttempts, _batchSize, stoppingToken);

                if (pass.Total > 0)
                    _logger.LogInformation(
                        "Reconciliation pass: {Requeued} requeued, {Abandoned} abandoned, "
                        + "{Contended} already handled elsewhere.",
                        pass.Requeued, pass.Abandoned, pass.Contended);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // shutting down
            }
            catch (Exception ex)
            {
                // Never let the loop die. This is the component that notices when nothing
                // else did, so a sweep that has silently stopped sweeping is strictly
                // worse than never having had one.
                _logger.LogError(ex, "Reconciliation pass failed; retrying next cycle.");
            }

            if (!await DelayAsync(RandomizedInterval(), stoppingToken)) break;
        }

        _logger.LogInformation("Stuck-analysis sweep stopped.");
    }

    // ±20%, so replicas that started together drift apart instead of all hitting the same
    // rows on the same tick and losing the compare-and-swap to each other every pass.
    private TimeSpan RandomizedInterval() =>
        _interval * (0.8 + (_jitter.NextDouble() * 0.4));

    /// False when cancelled during the wait.
    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
