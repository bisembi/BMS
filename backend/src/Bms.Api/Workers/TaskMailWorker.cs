using System.Security.Principal;
using Bms.Infrastructure.Mail;
using Bms.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace Bms.Api.Workers;

public sealed class TaskMailWorker(IServiceScopeFactory scopes,IConfiguration configuration,IHostEnvironment environment,
    ILogger<TaskMailWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        if(!configuration.GetValue<bool>("Bms:TaskMail:CaptureEnabled"))return;
        if(!OperatingSystem.IsWindows()||!WindowsServiceHelpers.IsWindowsService()
            ||environment.EnvironmentName is not("Staging" or "Production")
            ||WindowsIdentity.GetCurrent().User?.Value!=ProtectedTaskMailSettings.ServiceSid)
        {logger.LogWarning("Task mail capture requires the approved BMS Windows service identity; no capture or delivery was started.");return;}
        while(!token.IsCancellationRequested)
        {
            try
            {
                var settings=ProtectedTaskMailSettings.Read();
                await using var scope=scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TaskMailQueue>().RunAsync(settings,ProtectedTaskMailSettings.Read,token);
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){return;}
            catch(Exception error)when(!Bms.Api.Diagnostics.ApiProcessFatalExceptionClassifier.IsFatal(error))
            {logger.LogWarning("Task mail processing is unavailable. No unknown SMTP outcome is retried automatically. Check protected configuration and the outbox monitor.");}
            await Task.Delay(TimeSpan.FromMinutes(1),token);
        }
    }
}
