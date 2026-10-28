using System.Data;
using Bms.Application.Budgeting;
using Bms.Domain.Budgeting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

internal sealed record CompanyBudgetPlanSnapshot(CompanyBudgetVersion Version, CompanyBudgetActiveVersion Active,
    IReadOnlyList<BudgetInstance> Instances, IReadOnlyDictionary<Guid, DateTimeOffset?> CreationTimes);

internal static class CompanyBudgetPlanReader
{
    internal const string LockResource = "bms-web:company-budget-plan";
    internal static async Task AcquireLockAsync(BmsDbContext db, bool exclusive, CancellationToken token)
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new CompanyBudgetPlanUnavailableException();
        var mode = exclusive ? "Exclusive" : "Shared";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @company_budget_lock int;
            EXEC @company_budget_lock=sys.sp_getapplock @Resource={LockResource}, @LockMode={mode},
                @LockOwner=N'Transaction', @LockTimeout=15000;
            IF @company_budget_lock<0 THROW 64890,N'Company budget plan lock unavailable.',1;
            """, token);
    }
    internal static async Task<CompanyBudgetActiveVersion> ActiveAsync(BmsDbContext db, CancellationToken token)
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new CompanyBudgetPlanUnavailableException();
        var active = await db.CompanyBudgetActiveVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, token)
            ?? throw new CompanyBudgetPlanUnavailableException();
        if (!await db.CompanyBudgetVersions.AnyAsync(v => v.Id == active.VersionId && v.PublishedAtUtc.HasValue
                && v.PublishedInstanceCount == db.CompanyBudgetVersionInstances.Count(m => m.VersionId == v.Id), token)
            || await db.CompanyBudgetVersionInstances.AnyAsync(m => m.VersionId == active.VersionId
                && !db.BudgetInstances.Any(b => b.Id == m.BudgetInstanceId && b.Status == BudgetInstanceStatus.Approved
                    && b.ApprovalOrdinal.HasValue && b.ApprovedAtUtc.HasValue), token))
            throw new CompanyBudgetPlanUnavailableException();
        return active;
    }
    internal static async Task<CompanyBudgetPlanSnapshot> ReadAsync(BmsDbContext db, Guid? versionId,
        IReadOnlyCollection<Guid>? centers, CancellationToken token)
    {
        var active = await ActiveAsync(db, token);
        var id = versionId ?? active.VersionId;
        var version = await db.CompanyBudgetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new CompanyBudgetVersionNotFoundException();
        var instances = db.BudgetInstances.AsNoTracking().Include(x => x.Lines)
            .Where(x => db.CompanyBudgetVersionInstances.Any(m => m.VersionId == id && m.BudgetInstanceId == x.Id));
        if (centers is not null) instances = instances.Where(x => centers.Contains(x.CostCenterId));
        var rows = await instances.ToArrayAsync(token);
        if (id == active.VersionId && (!version.PublishedAtUtc.HasValue || rows.Any(x => x.Status != BudgetInstanceStatus.Approved)))
            throw new CompanyBudgetPlanUnavailableException();
        var rowIds = rows.Select(x => x.Id).ToArray();
        var creationTimes = await db.CompanyBudgetInstanceCreations.AsNoTracking().Where(x => rowIds.Contains(x.BudgetInstanceId))
            .ToDictionaryAsync(x => x.BudgetInstanceId, x => x.CreatedAtUtc, token);
        return new(version, active, await BudgetAdjustmentProjection.ApplyAsync(db, rows, token), creationTimes);
    }
    internal static async Task<BudgetEffectiveVersion[]> ReadActiveLimitsAsync(BmsDbContext db,
        IReadOnlyCollection<Guid>? centers, CancellationToken token) =>
        (await ReadAsync(db, null, centers, token)).Instances.Select(BudgetRepository.ToEffectiveVersion).ToArray();
}
