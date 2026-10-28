using Bms.Application.Abstractions;
using Bms.Application.Accruals;
using Bms.Application.Accruals.Notifications;

namespace Bms.Api.Workers;

/// <summary>
/// Opt-in in-process daily control. Restarts and multiple hosts may repeat a
/// pass; the SQL request lock and business key, not process memory, deduplicate.
/// </summary>
public sealed class AccrualNotificationWorker(
    IServiceScopeFactory scopeFactory,
    AccrualNotificationSettings settings,
    IAccrualAgingPolicy agingPolicy,
    AccrualNotificationRunState runState,
    IClock clock,
    ILogger<AccrualNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Enabled) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!settings.Enabled) { return; }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var aging = agingPolicy.GetCurrentContext()
                ?? throw new AccrualNotificationControlUnavailableException();
            if (runState.Read().LastSuccessfulBusinessDate is { } last && last >= aging.AsOfDate)
            {
                return;
            }
            runState.Started(clock.UtcNow);
            await using var scope = scopeFactory.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IAccrualNotificationSweep>()
                .RunAsync(cancellationToken);
            runState.Finished(clock.UtcNow, aging.AsOfDate, result);
            if (result.FailedRequests > 0)
            {
                logger.LogWarning("Accrual notification daily control has failed requests; a retry is pending.");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (AccrualNotificationControlUnavailableException)
        {
            runState.Failed(clock.UtcNow);
            // No exception, connection string, actor or request payload is logged.
            logger.LogWarning("Accrual notification daily control is unavailable; a retry is pending.");
        }
    }
}
