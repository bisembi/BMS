using System.Security.Principal;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace Bms.Api.Workers;

/// <summary>Service-only, retryable intake. Publication and ledger identities,
/// not an in-memory timer, deduplicate money. No public bank-event endpoint.</summary>
public sealed class AccountingPaymentWorker(IServiceScopeFactory scopes,IConfiguration configuration,
    ILogger<AccountingPaymentWorker> logger,IHostEnvironment environment) : BackgroundService
{
    private const string Sid="S-1-5-80-3928983041-4098722958-1494938123-1762692221-3803012568";
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        if(!configuration.GetValue<bool>("Bms:AccountingPayments:ReconciliationEnabled"))return;
        if(!OperatingSystem.IsWindows()||!WindowsServiceHelpers.IsWindowsService()
            ||environment.EnvironmentName is not("Staging" or "Production")
            ||WindowsIdentity.GetCurrent().User?.Value!=Sid)
            throw new InvalidOperationException("Accounting intake requires the approved Windows service identity.");
        while(!token.IsCancellationRequested)
        {
            try
            {
                await using var scope=scopes.CreateAsyncScope();
                var result=await scope.ServiceProvider.GetRequiredService<AccountingPaymentReconciliation>().ReconcileAsync(token);
                await JournalAsync(scope.ServiceProvider,result,token);
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){return;}
            catch(Exception error)when(!Bms.Api.Diagnostics.ApiProcessFatalExceptionClassifier.IsFatal(error))
            {
                logger.LogWarning("Accounting payment reconciliation is unavailable; no guessed payment is confirmed. A retry is pending.");
                try{await using var scope=scopes.CreateAsyncScope();await JournalAsync(scope.ServiceProvider,null,token);}
                catch(OperationCanceledException)when(token.IsCancellationRequested){return;}
                catch(Exception failure)when(!Bms.Api.Diagnostics.ApiProcessFatalExceptionClassifier.IsFatal(failure))
                { logger.LogWarning("Accounting exchange result could not be persisted; SQL journal coverage is incomplete."); }
            }
            await Task.Delay(TimeSpan.FromMinutes(1),token);
        }
    }
    private static Task<bool> JournalAsync(IServiceProvider services,AccountingPaymentRun? run,CancellationToken token)
    {
        var db=services.GetRequiredService<BmsDbContext>();var clock=services.GetRequiredService<IClock>();
        var audit=services.GetRequiredService<IBusinessAuditSink>();
        return services.GetRequiredService<IBusinessTransactionRunner>().ExecuteAsync(async ct=>
        {
            var payload=JsonSerializer.Serialize(new{status=run is null?"failed":run.Warnings.Count>0?"warning":"success",
                sourceCaptureId=run?.PaymentCaptureId,ddsCaptureId=run?.DdsCaptureId,
                appliedDocuments=run?.AppliedDocuments,reversedDocuments=run?.ReversedDocuments,
                ambiguousRequests=run?.AmbiguousRequests,warnings=run?.Warnings.Take(100).ToArray(),
                information=run is null?"Сверка оплат недоступна. Новые оплаты не подтверждены; проверьте загрузку 1С и факта ДДС.":null});
            var previous=await db.BusinessAuditEntries.AsNoTracking().Where(a=>a.ObjectType=="AccountingPaymentExchange")
                .OrderByDescending(a=>a.OccurredAtUtc).Select(a=>new{a.AfterJson,a.OccurredAtUtc}).FirstOrDefaultAsync(ct);
            if(previous?.AfterJson==payload&&previous.OccurredAtUtc>clock.UtcNow.AddHours(-1))return false;
            audit.Append(Guid.NewGuid(),clock.UtcNow,Sid,"AccountingPaymentExchange",run?.PaymentCaptureId.ToString("D")??"unavailable",
                run is null?"AccountingPaymentExchangeFailed":run.Warnings.Count>0?"AccountingPaymentExchangeWarnings":"AccountingPaymentExchangeCompleted",null,payload,Guid.NewGuid().ToString("N"));
            await db.SaveChangesAsync(ct);return true;
        },token);
    }
}
