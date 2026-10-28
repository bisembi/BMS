using Bms.Application.Abstractions;
using Bms.Domain.Workflow;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Persistence for immutable approval-route snapshots. Its caller owns a
/// Serializable business transaction and locks the parent expense request
/// before asking for the next sequence number.
/// </summary>
public sealed class ApprovalRouteSnapshotStore(BmsDbContext dbContext)
    : IApprovalRouteSnapshotStore
{
    public async Task CaptureCompanyBudgetBaselineAsync(Guid expenseRequestId, int snapshotNumber, CancellationToken token)
    {
        var active = await CompanyBudgetPlanReader.ActiveAsync(dbContext, token);
        dbContext.CompanyExpenseApprovalBaselines.Add(new() { ExpenseRequestId = expenseRequestId, SnapshotNumber = snapshotNumber,
            VersionId = active.VersionId, ActiveConcurrencyStamp = active.ConcurrencyStamp });
    }
    public async Task<bool> CompanyBudgetBaselineChangedAsync(Guid expenseRequestId, int snapshotNumber, CancellationToken token)
    {
        await CompanyBudgetPlanReader.AcquireLockAsync(dbContext, false, token);
        var active = await CompanyBudgetPlanReader.ActiveAsync(dbContext, token);
        var frozen = await dbContext.CompanyExpenseApprovalBaselines.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ExpenseRequestId == expenseRequestId && x.SnapshotNumber == snapshotNumber, token);
        // Pre-SQL048 submissions have no invented baseline. After a real activation they must be re-evaluated.
        return frozen is null
            ? await dbContext.CompanyBudgetActivations.AnyAsync(x => x.PreviousVersionId.HasValue, token)
            : frozen.VersionId != active.VersionId || frozen.ActiveConcurrencyStamp != active.ConcurrencyStamp;
    }
    public async Task<int> NextSnapshotNumberAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken)
    {
        if (expenseRequestId == Guid.Empty)
        {
            throw new InvalidOperationException("Expense request id is required.");
        }

        var last = await dbContext.ApprovalRouteSnapshots
            .Where(snapshot => snapshot.ExpenseRequestId == expenseRequestId)
            .Select(snapshot => (int?)snapshot.SnapshotNumber)
            .MaxAsync(cancellationToken);
        return checked((last ?? 0) + 1);
    }

    public void Add(ApprovalRouteSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        dbContext.ApprovalRouteSnapshots.Add(new PersistedApprovalRouteSnapshot
        {
            ExpenseRequestId = snapshot.ExpenseRequestId,
            SnapshotNumber = snapshot.SnapshotNumber,
            WorkflowDefinitionVersion = snapshot.WorkflowDefinitionVersion,
            OrganizationId = snapshot.OrganizationId,
            CostCenterId = snapshot.CostCenterId,
            PositiveDeficitKzt = snapshot.PositiveDeficitKzt,
            CapturedAtUtc = snapshot.CapturedAtUtc
        });
        foreach (var stage in snapshot.Stages)
        {
            dbContext.ApprovalRouteStages.Add(new PersistedApprovalRouteStage
            {
                ExpenseRequestId = snapshot.ExpenseRequestId,
                SnapshotNumber = snapshot.SnapshotNumber,
                StageNumber = stage.StageNumber,
                RoleCode = stage.RoleCode
            });
            foreach (var employeeId in stage.EligibleEmployeeIds)
            {
                dbContext.ApprovalRouteStageParticipants.Add(new PersistedApprovalRouteStageParticipant
                {
                    ExpenseRequestId = snapshot.ExpenseRequestId,
                    SnapshotNumber = snapshot.SnapshotNumber,
                    StageNumber = stage.StageNumber,
                    EmployeeId = employeeId
                });
            }
        }
    }

    public async Task<ApprovalRouteSnapshot?> FindAsync(
        Guid expenseRequestId,
        int snapshotNumber,
        CancellationToken cancellationToken)
    {
        if (expenseRequestId == Guid.Empty || snapshotNumber <= 0)
        {
            return null;
        }

        return await LoadAsync(expenseRequestId, snapshotNumber, cancellationToken);
    }

    public async Task<ApprovalRouteSnapshot?> FindLatestAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken)
    {
        if (expenseRequestId == Guid.Empty)
        {
            return null;
        }

        var snapshotNumber = await dbContext.ApprovalRouteSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.ExpenseRequestId == expenseRequestId)
            .Select(snapshot => (int?)snapshot.SnapshotNumber)
            .MaxAsync(cancellationToken);
        return snapshotNumber.HasValue
            ? await LoadAsync(expenseRequestId, snapshotNumber.Value, cancellationToken)
            : null;
    }

    public async Task<IReadOnlyDictionary<Guid, ApprovalRouteSnapshot>> FindLatestMany421Async(
        IReadOnlyCollection<Guid> expenseRequestIds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expenseRequestIds.Count == 0) return new Dictionary<Guid, ApprovalRouteSnapshot>();
        if (expenseRequestIds.Count > 100 || expenseRequestIds.Any(id => id == Guid.Empty)
            || expenseRequestIds.Distinct().Count() != expenseRequestIds.Count)
            throw new InvalidOperationException("Invalid latest route page.");
        var numbers = await dbContext.ApprovalRouteSnapshots.AsNoTracking()
            .Where(snapshot => expenseRequestIds.Contains(snapshot.ExpenseRequestId))
            .GroupBy(snapshot => snapshot.ExpenseRequestId)
            .Select(group => new { Id = group.Key, Number = group.Max(snapshot => snapshot.SnapshotNumber) })
            .ToDictionaryAsync(row => row.Id, row => row.Number, cancellationToken);
        return await FindActiveAsync(numbers, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, ApprovalRouteSnapshot>> FindActiveAsync(
        IReadOnlyDictionary<Guid, int> activeSnapshotNumbers, CancellationToken cancellationToken)
    {
        if (activeSnapshotNumbers.Count == 0) return new Dictionary<Guid, ApprovalRouteSnapshot>();
        if (activeSnapshotNumbers.Count > 100 || activeSnapshotNumbers.Any(p => p.Key == Guid.Empty || p.Value <= 0))
            throw new InvalidOperationException("Invalid active route page.");
        // Three bounded reads of exact pairs from the already authorized page.
        var headers = await dbContext.ApprovalRouteSnapshots.AsNoTracking()
            .Where(ExactPairs<PersistedApprovalRouteSnapshot>(activeSnapshotNumbers)).ToArrayAsync(cancellationToken);
        var stages = await dbContext.ApprovalRouteStages.AsNoTracking()
            .Where(ExactPairs<PersistedApprovalRouteStage>(activeSnapshotNumbers)).OrderBy(s => s.StageNumber).ToArrayAsync(cancellationToken);
        var participants = await dbContext.ApprovalRouteStageParticipants.AsNoTracking()
            .Where(ExactPairs<PersistedApprovalRouteStageParticipant>(activeSnapshotNumbers))
            .OrderBy(p => p.StageNumber).ThenBy(p => p.EmployeeId).ToArrayAsync(cancellationToken);
        return headers.ToDictionary(h => h.ExpenseRequestId, h => Restore(h,
            stages.Where(s => s.ExpenseRequestId == h.ExpenseRequestId && s.SnapshotNumber == h.SnapshotNumber).ToArray(),
            participants.Where(p => p.ExpenseRequestId == h.ExpenseRequestId && p.SnapshotNumber == h.SnapshotNumber).ToArray()));
    }

    internal static Expression<Func<T, bool>> ExactPairs<T>(IReadOnlyDictionary<Guid, int> pairs)
    {
        var row = Expression.Parameter(typeof(T), "row");
        Expression predicate = Expression.Constant(false);
        foreach (var pair in pairs)
        {
            var requestId = pair.Key;
            var number = pair.Value;
            Expression<Func<Guid>> getId = () => requestId;
            Expression<Func<int>> getNumber = () => number;
            predicate = Expression.OrElse(predicate, Expression.AndAlso(
                Expression.Equal(Expression.Property(row, "ExpenseRequestId"), getId.Body),
                Expression.Equal(Expression.Property(row, "SnapshotNumber"), getNumber.Body)));
        }
        return Expression.Lambda<Func<T, bool>>(predicate, row);
    }

    private async Task<ApprovalRouteSnapshot?> LoadAsync(
        Guid expenseRequestId,
        int snapshotNumber,
        CancellationToken cancellationToken)
    {
        var snapshot = await dbContext.ApprovalRouteSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.ExpenseRequestId == expenseRequestId
                && candidate.SnapshotNumber == snapshotNumber, cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        var stages = await dbContext.ApprovalRouteStages
            .AsNoTracking()
            .Where(stage => stage.ExpenseRequestId == expenseRequestId
                && stage.SnapshotNumber == snapshotNumber)
            .OrderBy(stage => stage.StageNumber)
            .ToArrayAsync(cancellationToken);
        var participants = await dbContext.ApprovalRouteStageParticipants
            .AsNoTracking()
            .Where(participant => participant.ExpenseRequestId == expenseRequestId
                && participant.SnapshotNumber == snapshotNumber)
            .OrderBy(participant => participant.StageNumber)
            .ThenBy(participant => participant.EmployeeId)
            .ToArrayAsync(cancellationToken);
        return Restore(snapshot, stages, participants);
    }

    private static ApprovalRouteSnapshot Restore(PersistedApprovalRouteSnapshot snapshot,
        PersistedApprovalRouteStage[] stages, PersistedApprovalRouteStageParticipant[] participants)
    {
        try
        {
            var expected = ApprovalRoutePlanner.Plan(snapshot.PositiveDeficitKzt, snapshot.WorkflowDefinitionVersion);
            if (stages.Length != expected.Count || stages.Any(stage => !expected.Any(e =>
                e.StageNumber == stage.StageNumber && e.RoleCode == stage.RoleCode))
                || participants.Any(p => !stages.Any(s => s.StageNumber == p.StageNumber)))
                throw new InvalidOperationException("Stored route roles or participant stages do not match its version.");
            return ApprovalRouteSnapshot.Create(
                snapshot.ExpenseRequestId,
                snapshot.SnapshotNumber,
                snapshot.WorkflowDefinitionVersion,
                snapshot.OrganizationId,
                snapshot.CostCenterId,
                snapshot.PositiveDeficitKzt,
                snapshot.CapturedAtUtc,
                stages.Select(stage => new ApprovalRouteSnapshotStageParticipants(
                    stage.StageNumber,
                    participants
                        .Where(participant => participant.StageNumber == stage.StageNumber)
                        .Select(participant => participant.EmployeeId)
                        .ToArray())));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "Stored approval-route snapshot is inconsistent.",
                exception);
        }
    }
}
