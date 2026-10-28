using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Application.Budgeting;

public sealed record BudgetRegisterMovement343(long Sequence,DateTimeOffset PostedAtUtc,DateOnly BudgetMonth,
    string CostCenter,string Article,Guid? RequestId,string? RequestNumber,string Event,
    decimal LimitDeltaKzt,decimal ReservedDeltaKzt,decimal PaidDeltaKzt,decimal AmountKzt,decimal BalanceAfterKzt,
    string SourceVersion,string Evidence);
public sealed record BudgetRegisterTotals343(decimal LimitKzt,decimal ReserveKzt,decimal PaidKzt,decimal BalanceKzt);
public sealed record BudgetRegisterPage343(IReadOnlyList<BudgetRegisterMovement343> Items,int Skip,int Take,int Total,
    long Revision,DateTimeOffset? CashCapturedAtUtc,BudgetRegisterTotals343 Totals);

public interface IBudgetExecutionRegisterReader343
{
    Task<BudgetRegisterPage343> ReadAsync(DateOnly month,Guid? costCenterId,Guid? articleId,Guid? requestId,
        int skip,int take,CancellationToken token);
}
public sealed class BudgetExecutionRegisterService343(IBudgetExecutionRegisterReader343 reader,ICurrentActor actor,IBusinessTransactionRunner transactions)
{
    public Task<BudgetRegisterPage343> ReadAsync(DateOnly month,Guid? costCenterId,Guid? articleId,Guid? requestId,
        int skip,int take,CancellationToken token)
    {
        AdministrationReportReadAccess.Require(actor);
        if(month.Day!=1||month.Year is <2000 or >2100||skip<0||take is <1 or >1000
            ||costCenterId==Guid.Empty||articleId==Guid.Empty||requestId==Guid.Empty)throw new ArgumentException("Invalid register filter.");
        return transactions.ExecuteAsync(ct=>reader.ReadAsync(month,costCenterId,articleId,requestId,skip,take,ct),token);
    }
}
