using System.Data;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

/// <summary>Removal-only synchronization, in the caller's audited transaction.
/// Explicit role-first employee creation/assignment remains a separate workflow.</summary>
public sealed class BudgetOwnerRoleSynchronizer(
    BmsDbContext db, ICurrentActor actor, IClock clock, IBusinessAuditSink audit, IOutboxSink outbox,
    Bms.Application.Authorization.BudgetDocumentAuthority authority)
{
    internal const string LockResource = "bms-web:budget-owner-role-sync";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static async Task AcquireLockAsync(BmsDbContext db, CancellationToken token)
    {
        RequireSerializable(db);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @owner_sync_lock int;
            EXEC @owner_sync_lock=sys.sp_getapplock @Resource={LockResource}, @LockMode=N'Exclusive',
                @LockOwner=N'Transaction', @LockTimeout=15000;
            IF @owner_sync_lock<0 THROW 64891,N'Budget owner synchronization lock unavailable.',1;
            """, token);
    }

    public async Task RemoveRoleWithoutScopeAsync(
        IEnumerable<Guid> affectedEmployeeIds, string cause, CancellationToken token)
    {
        RequireSerializable(db);
        // CFO changes by finance staff change scope only, never functional roles.
        if (!await authority.CanManageRoleAssignmentsAsync(actor, token)) return;
        var ids = affectedEmployeeIds.Distinct().Order().ToArray();
        if (ids.Length == 0) return;
        var persisted = await db.BudgetOwnerCostCenterAssignments.AsNoTracking()
            .Where(a => ids.Contains(a.EmployeeId)).ToArrayAsync(token);
        var links = OverlayAssignments(persisted,
            db.ChangeTracker.Entries<BudgetOwnerCostCenterAssignment>()
                .Where(e => ids.Contains(e.Entity.EmployeeId))
                .Select(e => (e.Entity, e.State == EntityState.Deleted)));
        var centerIds = links.Select(a => a.CostCenterId).Distinct().ToArray();
        var centers = (await db.CostCenters.AsNoTracking().Where(c => centerIds.Contains(c.Id))
            .ToArrayAsync(token)).ToDictionary(c => c.Id);
        foreach (var entry in db.ChangeTracker.Entries<CostCenterReference>())
        {
            if (entry.State == EntityState.Deleted) centers.Remove(entry.Entity.Id);
            else centers[entry.Entity.Id] = entry.Entity;
        }
        var role = db.FunctionalRoles.Local.SingleOrDefault(r =>
            r.Code == ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode)
            ?? await db.FunctionalRoles.SingleOrDefaultAsync(r =>
                r.Code == ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode, token);
        if (role is null || role.Code != ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode) return;
        foreach (var employeeId in ids)
        {
            token.ThrowIfCancellationRequested();
            if (HasEffectiveScope(employeeId, links, centers.Values)) continue;
            var assignment = db.EmployeeFunctionalRoles.Local.SingleOrDefault(r =>
                r.EmployeeId == employeeId && r.FunctionalRoleId == role.Id)
                ?? await db.EmployeeFunctionalRoles.SingleOrDefaultAsync(r =>
                    r.EmployeeId == employeeId && r.FunctionalRoleId == role.Id, token);
            if (assignment is null || !assignment.IsActive) continue;
            var externalId = ActorIdentity.RequireReferenceDataManagement(actor);
            var now = clock.UtcNow;
            if (now == default || now.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("Owner-role synchronization requires a UTC clock.");
            assignment.IsActive = false;
            var before = JsonSerializer.Serialize(new
                { employeeId, roleId = role.Id, roleCode = role.Code, isActive = true }, JsonOptions);
            var after = JsonSerializer.Serialize(new
                { employeeId, roleId = role.Id, roleCode = role.Code, isActive = false,
                    reason = "NoActiveOwnerCostCenter", cause }, JsonOptions);
            var correlation = Guid.NewGuid().ToString("N");
            audit.Append(Guid.NewGuid(), now, externalId, "AuthorizationDirectoryEmployee",
                employeeId.ToString("D"), "BudgetOwnerRoleAutomaticallyRemoved", before, after, correlation);
            outbox.Enqueue(Guid.NewGuid(), now, "BudgetOwnerRoleAutomaticallyRemoved", after, correlation);
        }
    }

    internal static BudgetOwnerCostCenterAssignment[] OverlayAssignments(
        IEnumerable<BudgetOwnerCostCenterAssignment> persisted,
        IEnumerable<(BudgetOwnerCostCenterAssignment Entity, bool Deleted)> tracked)
    {
        var result = persisted.ToDictionary(a => (a.EmployeeId, a.CostCenterId));
        foreach (var (entity, deleted) in tracked)
        {
            var key = (entity.EmployeeId, entity.CostCenterId);
            if (deleted) result.Remove(key);
            else result[key] = entity;
        }
        return result.Values.ToArray();
    }

    internal static bool HasEffectiveScope(Guid employeeId,
        IEnumerable<BudgetOwnerCostCenterAssignment> links, IEnumerable<CostCenterReference> centers)
    {
        var activeCenters = centers.Where(c => c.Id != Guid.Empty && c.IsActive && !c.IsGroup)
            .Select(c => c.Id).ToHashSet();
        return links.Any(a => a.EmployeeId == employeeId && a.IsActive && activeCenters.Contains(a.CostCenterId));
    }

    private static void RequireSerializable(BmsDbContext db)
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Owner-role synchronization requires a Serializable transaction.");
    }
}
