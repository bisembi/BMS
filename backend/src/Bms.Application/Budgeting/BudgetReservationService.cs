using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.Budgeting;

/// <summary>Scoped reservation details; authority and data are captured in one transaction.</summary>
public sealed class BudgetReservationService(
    IBudgetReservationReader reader,
    ICurrentActor currentActor,
    IBusinessTransactionRunner transactionRunner,
    IExecutionReportReader? reportReader = null)
{
    public async Task<BudgetReservationPageDto?> ReadAsync(
        Guid costCenterId,
        Guid cashFlowItemId,
        DateOnly budgetMonth,
        int skip,
        int take,
        CancellationToken cancellationToken,
        Guid? expectedFactSnapshotId = null)
    {
        ActorIdentity.RequireBusinessDataAccess(currentActor);
        if (expectedFactSnapshotId.HasValue && reportReader is null) throw new ExecutionFactVersionConflictException();
        return await transactionRunner.ExecuteAsync(async token =>
        {
        var scope = await currentActor.ResolveBusinessDataReadScopeAsync(token);
        if (!scope.Contains(costCenterId))
        {
            throw new ActorForbiddenException();
        }

        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, 100);
        // Any day of a month means that month, exactly as the list reads it.
        var month = new DateOnly(budgetMonth.Year, budgetMonth.Month, 1);

        var slice = await reader.ReadAsync(
            costCenterId,
            cashFlowItemId,
            month,
            skip,
            take,
            scope,
            token);
        var reported = reportReader is null ? null
            : await reportReader.ReadSliceAsync(costCenterId, cashFlowItemId, month, 0, 1, scope, token, expectedFactSnapshotId);
        var position = reported?.Page.SingleOrDefault();
        if (reported is not null && slice is not null && (position is null
            || position.ReservedAmountKzt != slice.ReservedAmountKzt
            || position.ReservedAmountKzt != slice.TotalReservedAmountKzt))
            throw new BudgetProjectionDriftException("The report and reservation captures disagree.");
        if (slice is null && position is null)
        {
            return null;
        }

        return new BudgetReservationPageDto(
            slice?.Page.Select(Map).ToArray() ?? [],
            skip,
            take,
            slice?.Total ?? 0,
            slice?.TotalLineCount ?? 0,
            slice?.TotalReservedAmountKzt ?? 0m,
            BudgetPositionService.Map(
                costCenterId,
                cashFlowItemId,
                month,
                position?.LimitAmountKzt ?? slice?.LimitAmountKzt,
                position?.ReservedAmountKzt ?? slice!.ReservedAmountKzt,
                position?.PaidAmountKzt ?? slice!.PaidAmountKzt))
        {
            VisibleReservedAmountKzt = slice?.VisibleReservedAmountKzt ?? 0m,
            FactSource = reported?.FactSource
        };
        }, cancellationToken);
    }

    /// <summary>
    /// A participant is published only if its status is one the public contract
    /// knows. An unrecognised code means the projection points at a request this
    /// surface cannot describe, and describing it wrongly would be worse than
    /// refusing.
    /// </summary>
    private static BudgetReservationParticipantDto Map(BudgetReservationParticipant participant)
    {
        if (!ExpenseRequestStatusCode.TryParse(participant.Status, out _))
        {
            throw new BudgetProjectionDriftException(
                "A reserving request carries a status outside the published set.");
        }

        return new BudgetReservationParticipantDto(
            participant.Status,
            participant.Purpose,
            participant.PlannedPaymentDate,
            participant.LineCount,
            participant.ReservedAmountKzt);
    }
}
