using Bms.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace Bms.Api.Workers;

/// <summary>Daily 08:00 UTC+5 job, including catch-up after restart. SQL audit deduplicates host instances.</summary>
public sealed class NationalBankCurrencyRateWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<NationalBankCurrencyRateWorker> logger, IHostEnvironment environment) : BackgroundService
{
    // VM143 intentionally uses the Staging security configuration under SCM.
    // Do not activate a staging console/test host or any development host.
    internal static bool CanRun(string environmentName, bool enabled, bool isWindowsService) =>
        enabled && (environmentName == Environments.Production
            || environmentName == Environments.Staging && isWindowsService);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!CanRun(environment.EnvironmentName, configuration.GetValue<bool>("Bms:CurrencyRates:NationalBankImportEnabled"),
            WindowsServiceHelpers.IsWindowsService())) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var rates = scope.ServiceProvider.GetRequiredService<CurrencyRateDirectoryService>();
                if (rates.BusinessNow.Hour >= 8 && !await rates.ImportAsync(true, stoppingToken))
                    logger.LogWarning("Daily currency-rate import failed. Manual rate entry is required.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) when (!Bms.Api.Diagnostics.ApiProcessFatalExceptionClassifier.IsFatal(exception))
            {
                logger.LogWarning("Daily currency-rate storage is unavailable. The next pass will retry.");
            }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
