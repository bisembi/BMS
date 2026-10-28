using Bms.Application.Authorization;
using Bms.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// <summary>Fresh SQL evidence, shared with read filters and held through command commit.</summary>
internal static class StaffSubstitutionGuard471
{
    internal const string LockResource = "BMS:StaffSubstitution471:Mutation";
    internal static readonly string[] IntegrationRoles = ["INTEGRATION_ADMIN",
        ActiveRoleCatalog.IntegrationAdministratorAssignmentCode, ActiveRoleCatalog.IntegrationAdministratorAssignmentAlias];

    internal static IQueryable<Guid> LivePrincipal(BmsDbContext db, StaffActingRole471 proof)
    {
        var now = db.StaffNow471;
        var query = from d in db.StaffSubstitutions471.AsNoTracking()
            join principal in db.Employees.AsNoTracking() on d.PrincipalEmployeeId equals principal.Id
            join deputy in db.Employees.AsNoTracking() on d.DeputyEmployeeId equals deputy.Id
            join pm in db.EmployeeActorMappings.AsNoTracking() on principal.Id equals pm.EmployeeId
            join dm in db.EmployeeActorMappings.AsNoTracking() on deputy.Id equals dm.EmployeeId
            where d.Id == proof.DocumentId && d.Status == "CONFIRMED" && principal.IsActive && deputy.IsActive
                && d.PrincipalEmployeeId == proof.PrincipalEmployeeId && d.DeputyEmployeeId == proof.DeputyEmployeeId
                && d.PrincipalEmployeeId != d.DeputyEmployeeId && d.RoleCode == proof.RoleCode
                && d.ValidFromUtc <= now && (d.ValidBeforeUtc == null || now < d.ValidBeforeUtc)
                && pm.IsActive && dm.IsActive && pm.ActorExternalId == proof.PrincipalActorExternalId
                && dm.ActorExternalId == proof.DeputyActorExternalId
                && db.EmployeeActorMappings.Count(m => m.ActorExternalId == pm.ActorExternalId) == 1
                && db.EmployeeActorMappings.Count(m => m.ActorExternalId == dm.ActorExternalId) == 1
                && db.EmployeeActorMappings.Count(m => m.EmployeeId == principal.Id && m.IsActive) == 1
                && db.EmployeeActorMappings.Count(m => m.EmployeeId == deputy.Id && m.IsActive) == 1
                && db.EmployeeFunctionalRoles.Any(a => a.EmployeeId == principal.Id && a.IsActive
                    && db.FunctionalRoles.Any(r => r.Id == a.FunctionalRoleId && r.IsActive && r.Code == proof.RoleCode))
                && !db.EmployeeFunctionalRoles.Any(a => (a.EmployeeId == principal.Id || a.EmployeeId == deputy.Id)
                    && a.IsActive && db.FunctionalRoles.Any(r => r.Id == a.FunctionalRoleId
                        && (!r.IsActive || IntegrationRoles.Contains(r.Code))))
            select new { Id = principal.Id, pm.ActorExternalId, DeputyActor = dm.ActorExternalId };
        if (db.Database.IsSqlServer())
            query = query.Where(row => EF.Functions.DataLength(row.ActorExternalId) == EF.Functions.DataLength(proof.PrincipalActorExternalId)
                && EF.Functions.Collate(row.ActorExternalId, "Latin1_General_100_BIN2") == proof.PrincipalActorExternalId
                && EF.Functions.DataLength(row.DeputyActor) == EF.Functions.DataLength(proof.DeputyActorExternalId)
                && EF.Functions.Collate(row.DeputyActor, "Latin1_General_100_BIN2") == proof.DeputyActorExternalId);
        return query.Select(row => row.Id);
    }

    internal static async Task ValidateAsync(BmsDbContext db, CancellationToken token)
    {
        if (db.ActiveRoleSelection?.Substitution471 is not { } proof) return;
        if (db.Database.CurrentTransaction is null || !ActiveRoleCatalog.IsBusinessRole(proof.RoleCode)
            || db.ActiveRoleSelection.ActorExternalId != proof.DeputyActorExternalId
            || db.ActiveRoleSelection.ActiveRoleCode != proof.RoleCode
            || db.ActiveRoleSelection.EmployeeId != proof.PrincipalEmployeeId)
            throw new ActorForbiddenException();
        if (db.Database.IsSqlServer())
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'BMS:StaffSubstitution471:Mutation',@LockMode=N'Shared',@LockOwner=N'Transaction',@LockTimeout=10000; IF @r<0 THROW 65471,'STAFF471_BUSY',1;", token);
        if (await LivePrincipal(db, proof).CountAsync(token) != 1) throw new ActorForbiddenException();
    }
}
