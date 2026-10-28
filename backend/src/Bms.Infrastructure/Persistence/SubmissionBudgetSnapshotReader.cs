using System.Data;
using System.Data.Common;
using Bms.Application.Abstractions;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Budgeting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed class SubmissionBudgetSnapshotReader(BmsDbContext dbContext,
    IBudgetCashConsumptionReader? cashConsumption = null) :
    ISubmissionBudgetSnapshotReader
{
    private const int ApplicationLockTimeoutMilliseconds = 15_000;

    public async Task<SubmissionBudgetSnapshot> ReadAsync(
        Guid expenseRequestId,
        IReadOnlyCollection<BudgetRequestProjection> desiredProjections,
        CancellationToken cancellationToken)
    {
        if (expenseRequestId == Guid.Empty)
        {
            throw new ArgumentException("Expense request id is required.", nameof(expenseRequestId));
        }

        ArgumentNullException.ThrowIfNull(desiredProjections);
        if (desiredProjections.Any(projection => projection.ExpenseRequestId != expenseRequestId))
        {
            throw new ArgumentException(
                "Every desired budget projection must belong to the evaluated request.",
                nameof(desiredProjections));
        }

        try
        {
            return await ReadCoreAsync(
                expenseRequestId,
                desiredProjections,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SubmissionPreflightUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new SubmissionPreflightUnavailableException(
                "A consistent submission preflight snapshot could not be read.",
                exception);
        }
    }

    internal static BudgetEffectiveVersion ToEffectiveVersion(BudgetInstance instance) =>
        new(
            instance.Id,
            instance.CostCenterId,
            instance.ValidFrom,
            instance.ValidTo,
            instance.ApprovalOrdinal
                ?? throw new InvalidOperationException("Approved budget has no approval ordinal."),
            instance.ApprovedAtUtc
                ?? throw new InvalidOperationException("Approved budget has no approval timestamp."),
            instance.Lines.Where(line => line.FlowDirection == BudgetFlowDirection.Payment)
                .Select(line => new BudgetEffectiveLine(
                line.CashFlowItemId,
                line.BudgetMonth,
                line.LimitAmountKzt)).ToArray());

    private async Task<SubmissionBudgetSnapshot> ReadCoreAsync(
        Guid expenseRequestId,
        IReadOnlyCollection<BudgetRequestProjection> desiredProjections,
        CancellationToken cancellationToken)
    {

        EnsureSerializableTransaction();
        await CompanyBudgetPlanReader.ActiveAsync(dbContext, cancellationToken);
        await AcquireSharedApplicationLockAsync(
            BusinessManagedLockResources.ExpenseRequest(expenseRequestId),
            cancellationToken);

        var existing = await dbContext.BudgetRequestProjections
            .AsNoTracking()
            .Where(projection => projection.ExpenseRequestId == expenseRequestId)
            .OrderBy(projection => projection.ExpenseRequestLineId)
            .ToListAsync(cancellationToken);

        var affectedKeys = existing
            .Select(projection => projection.Key)
            .Concat(desiredProjections.Select(projection => projection.Key))
            .Distinct()
            .OrderBy(key => key.CostCenterId.ToString("N"), StringComparer.Ordinal)
            .ThenBy(key => key.CashFlowItemId.ToString("N"), StringComparer.Ordinal)
            .ThenBy(key => key.BudgetMonth)
            .ToArray();

        var budgetLockResources = affectedKeys
            .Select(BusinessManagedLockResources.Budget)
            .ToArray();
        foreach (var resource in budgetLockResources.Length == 0
                     ? []
                     : BusinessManagedLockResources.Order(budgetLockResources))
        {
            await AcquireSharedApplicationLockAsync(resource, cancellationToken);
        }

        var existingByKey = SumByKey(existing);
        var desiredByKey = SumByKey(desiredProjections);
        var approvedVersions = Array.Empty<BudgetEffectiveVersion>();
        if (affectedKeys.Length > 0)
        {
            var costCenterIds = affectedKeys
                .Select(key => key.CostCenterId)
                .Distinct()
                .ToArray();
            approvedVersions = await CompanyBudgetPlanReader.ReadActiveLimitsAsync(dbContext, costCenterIds, cancellationToken);
        }

        var positions = new List<SubmissionBudgetPositionSnapshot>(affectedKeys.Length);
        var consumption = cashConsumption is null || dbContext.BudgetRegisterEnabled343 ? null
            : await cashConsumption.ReadAsync(affectedKeys, cancellationToken);
        foreach (var key in affectedKeys)
        {
            var position = await dbContext.BudgetPositions
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate =>
                    candidate.CostCenterId == key.CostCenterId &&
                    candidate.CashFlowItemId == key.CashFlowItemId &&
                    candidate.BudgetMonth == key.BudgetMonth,
                    cancellationToken);
            var allPersistedForKey = await dbContext.BudgetRequestProjections
                .AsNoTracking()
                .Where(projection =>
                    projection.CostCenterId == key.CostCenterId &&
                    projection.CashFlowItemId == key.CashFlowItemId &&
                    projection.BudgetMonth == key.BudgetMonth)
                .ToListAsync(cancellationToken);
            var persistedProjectionTotal = allPersistedForKey.Sum(
                projection => projection.ReservedAmountKzt);
            var aggregateIsConsistent = position is null
                ? persistedProjectionTotal == 0m
                : position.ReservedAmountKzt == persistedProjectionTotal;
            var effectiveLimit = BudgetEffectiveLimitResolver.Resolve(
                position?.LimitAmountKzt,
                key.CostCenterId,
                key.CashFlowItemId,
                key.BudgetMonth,
                approvedVersions);

            positions.Add(new SubmissionBudgetPositionSnapshot(
                key,
                effectiveLimit,
                position?.ReservedAmountKzt ?? 0m,
                consumption is null ? position?.PaidAmountKzt ?? 0m
                    : consumption[key].EffectivePaid(position?.PaidAmountKzt ?? 0m),
                existingByKey.GetValueOrDefault(key),
                desiredByKey.GetValueOrDefault(key),
                aggregateIsConsistent));
        }

        if(dbContext.BudgetRegisterEnabled343)
        {
            var currentRequest=await dbContext.ExpenseRequests.AsNoTracking().SingleOrDefaultAsync(r=>r.Id==expenseRequestId,cancellationToken);
            var submitted=currentRequest is null||currentRequest.Status is not(Bms.Domain.ExpenseRequests.ExpenseRequestStatus.Draft or Bms.Domain.ExpenseRequests.ExpenseRequestStatus.Returned)||!dbContext.BudgetRegisterReadOnly343;
            var frame=await new BudgetExecutionFrameReader343(dbContext).ReadAsync(expenseRequestId,desiredProjections,submitted,cancellationToken,requestedKeys:affectedKeys);
            if(!frame.Evaluation.Decisions.TryGetValue(expenseRequestId,out var decision))
                throw new SubmissionPreflightUnavailableException("Budget register candidate decision unavailable.");
            var opening=frame.Openings.ToDictionary(p=>p.Key);var lines=decision.Lines.ToDictionary(p=>p.Key);
            positions=positions.Select(p=>
            {
                var value=opening.GetValueOrDefault(p.Key)??throw new SubmissionPreflightUnavailableException("Budget register key unavailable.");
                var earlier=lines.TryGetValue(p.Key,out var line)?line.ReservedBeforeKzt:0m;
                if(earlier<0m)throw new SubmissionPreflightUnavailableException("Budget register ordering drift.");
                return p with{LimitAmountKzt=value.LimitAmountKzt,PaidAmountKzt=line?.PaidBeforeKzt??value.PaidAmountKzt,
                    CurrentReservedAmountKzt=checked(earlier+p.ExistingRequestProjectionAmountKzt)};
            }).ToList();
            return new SubmissionBudgetSnapshot(RequestProjectionMatches(existing,desiredProjections),positions.AsReadOnly())
                {ApprovedDeficit343=decision.Frozen?decision.PositiveDeficitKzt:null};
        }
        return new SubmissionBudgetSnapshot(
            RequestProjectionMatches(existing, desiredProjections),
            positions.AsReadOnly());
    }

    private void EnsureSerializableTransaction()
    {
        var transaction = dbContext.Database.CurrentTransaction;
        if (transaction is null ||
            transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
        {
            throw new SubmissionPreflightUnavailableException(
                "A consistent submission preflight snapshot requires a Serializable transaction.");
        }
    }

    private async Task AcquireSharedApplicationLockAsync(
        string resource,
        CancellationToken cancellationToken)
    {
        var transaction = dbContext.Database.CurrentTransaction
            ?? throw new SubmissionPreflightUnavailableException(
                "A consistent submission preflight snapshot requires an active transaction.");
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @lock_result int;
            EXEC @lock_result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = N'Shared',
                @LockOwner = N'Transaction',
                @LockTimeout = @lock_timeout;
            SELECT @lock_result;
            """;
        AddParameter(command, "@resource", DbType.String, resource);
        AddParameter(
            command,
            "@lock_timeout",
            DbType.Int32,
            ApplicationLockTimeoutMilliseconds);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null || result is DBNull || Convert.ToInt32(result) < 0)
        {
            throw new SubmissionPreflightUnavailableException(
                "A consistent submission preflight snapshot could not be acquired.");
        }
    }

    internal static bool RequestProjectionMatches(
        IReadOnlyCollection<BudgetRequestProjection> existing,
        IReadOnlyCollection<BudgetRequestProjection> desired)
    {
        if (existing.Count != desired.Count)
        {
            return false;
        }

        // The projection table has a unique line constraint, but the desired
        // collection is an in-memory boundary. Refuse duplicate or empty line
        // identities before SumByKey can aggregate one request line twice.
        if (existing.Any(projection => projection.ExpenseRequestLineId == Guid.Empty) ||
            desired.Any(projection => projection.ExpenseRequestLineId == Guid.Empty) ||
            existing.Select(projection => projection.ExpenseRequestLineId).Distinct().Count() !=
                existing.Count ||
            desired.Select(projection => projection.ExpenseRequestLineId).Distinct().Count() !=
                desired.Count)
        {
            return false;
        }

        var existingByLine = existing.ToDictionary(
            projection => projection.ExpenseRequestLineId);
        return desired.All(projection =>
            existingByLine.TryGetValue(projection.ExpenseRequestLineId, out var persisted) &&
            persisted.Key == projection.Key &&
            persisted.ReservedAmountKzt == projection.ReservedAmountKzt);
    }

    private static Dictionary<BudgetKey, decimal> SumByKey(
        IEnumerable<BudgetRequestProjection> projections) => projections
        .GroupBy(projection => projection.Key)
        .ToDictionary(
            group => group.Key,
            group => group.Sum(projection => projection.ReservedAmountKzt));

    private static void AddParameter(
        DbCommand command,
        string name,
        DbType type,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

}
