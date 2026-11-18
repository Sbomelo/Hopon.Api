using Microsoft.Extensions.Options;
namespace Hopon.Api.Services;

public sealed class RetentionService(RetentionRunner runner, TimeProvider clock, IOptions<RetentionOptions> options,
    ILogger<RetentionService> logger) : BackgroundService
{
    public static DateTimeOffset NextRun(DateTimeOffset now)
    {
        // 02:00 SAST is 00:00 UTC year-round (South Africa has no daylight saving).
        return new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        if (options.Value.RunOnStartup) await RunSafely(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = clock.GetUtcNow();
                await Task.Delay(NextRun(now) - now, clock, stoppingToken);
                await RunSafely(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
    private async Task RunSafely(CancellationToken token)
    {
        try { await runner.RunAsync(cancellationToken: token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) { logger.LogError("Retention run failed; retry will occur at the next daily run."); }
    }
}
