using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Budgeting;

namespace Bms.Application.Budgeting;

public sealed record RequestBudgetReserve442(Guid RequestId,string? RequestNumber,DateOnly PlannedPaymentDate,
    string? Counterparty,decimal AmountKzt,bool IsCurrent,string? Comment=null);
public sealed record RequestBudgetExplanation442(Guid RequestId,Guid RequestVersion,Guid CashFlowItemId,Guid CostCenterId,
    DateOnly BudgetMonth,long Revision,Guid SnapshotId,string? BudgetInstanceNumber,decimal LimitKzt,
    decimal PaidIncludedKzt,decimal ReservedBeforeKzt,decimal OwnReservedKzt,decimal OwnPaidKzt,
    decimal AvailableBeforeKzt,decimal AvailableAfterKzt,decimal LaterPaidKzt,
    IReadOnlyList<RequestBudgetReserve442> Reservations,int ReserveTotal,int ReserveSkip,decimal HiddenReserveKzt,
    BddsDrillPage Facts);
public interface IRequestBudgetExplanationReader442
{
    Task<RequestBudgetExplanation442> ReadAsync(Guid requestId,Guid version,Guid articleId,int factSkip,int reserveSkip,long? revision,CancellationToken token);
}
public sealed class RequestBudgetExplanationService442(IRequestBudgetExplanationReader442 reader,ICurrentActor actor,
    IBusinessTransactionRunner transactions)
{
    public Task<RequestBudgetExplanation442> ReadAsync(Guid requestId,Guid version,Guid articleId,int factSkip,int reserveSkip,long? revision,CancellationToken token)
    {
        ActorIdentity.RequireBusinessDataAccess(actor);
        if(requestId==Guid.Empty||version==Guid.Empty||articleId==Guid.Empty||factSkip is <0 or >100000
            ||reserveSkip is <0 or >20000||revision<0)throw new ArgumentException("Invalid budget explanation scope.");
        return transactions.ExecuteAsync(ct=>reader.ReadAsync(requestId,version,articleId,factSkip,reserveSkip,revision,ct),token);
    }
}

/// <summary>Exact same FIFO admission arithmetic as the request control, not
/// the final global balance after later requests. Never rebalance to UI input.</summary>
public static class RequestBudgetExplanationMath442
{
    public static void Validate(decimal limit,BudgetExecutionLine343 line,decimal ownPaid,decimal ownReserve,decimal includedReserves)
    {
        if(ownPaid<0m||ownReserve<0m||includedReserves<0m||line.PaidBeforeKzt<0m||line.ReservedBeforeKzt<0m
            ||line.AmountKzt!=checked(ownPaid+ownReserve)
            ||includedReserves!=checked(line.ReservedBeforeKzt+ownReserve)
            ||line.AvailableBeforeKzt!=checked(limit-line.PaidBeforeKzt-line.ReservedBeforeKzt)
            ||line.AvailableAfterKzt!=checked(limit-line.PaidBeforeKzt-ownPaid-includedReserves))
            throw new BudgetProjectionDriftException("Request explanation and FIFO differ.");
    }
}
