using System.Text.Json;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.ExpenseRequests;
using Bms.Infrastructure.Mail;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed partial class TaskMailSource
{
    // Creator assignment inventory is checked afresh for both worker rechecks.
    // This is not the interactive caller's selected role, and grants no rights.
    internal async Task<IReadOnlyList<TaskMailTask>> ReadReturned377Async(Guid? requestId,
        Guid? employeeId, string? actor, CancellationToken token)
    {
        var creators = from e in db.Employees.AsNoTracking()
            join m in db.EmployeeActorMappings.AsNoTracking() on e.Id equals m.EmployeeId
            where e.IsActive && e.Id != Guid.Empty && m.IsActive
              && db.EmployeeActorMappings.Count(x => x.EmployeeId == e.Id && x.IsActive) == 1
              && db.EmployeeActorMappings.Count(x => x.ActorExternalId == m.ActorExternalId) == 1
              && !(from a in db.EmployeeFunctionalRoles join f in db.FunctionalRoles on a.FunctionalRoleId equals f.Id
                   where a.EmployeeId == e.Id && a.IsActive && (!f.IsActive
                     || f.Code == ActiveRoleCatalog.IntegrationAdministratorAssignmentCode
                     || f.Code == ActiveRoleCatalog.IntegrationAdministratorAssignmentAlias) select a.EmployeeId).Any()
            select new { e.Id, e.Name, m.ActorExternalId };
        if (employeeId.HasValue) creators = creators.Where(e => e.Id == employeeId);
        if (actor is not null) creators = creators.Where(e => e.ActorExternalId == actor);
        var authority = from g in db.EmployeeFunctionalRoles.AsNoTracking()
            join f in db.FunctionalRoles.AsNoTracking() on g.FunctionalRoleId equals f.Id
            where g.IsActive && f.IsActive && EF.Functions.DataLength(f.Code) == f.Code.Length * 2
            select new { g.EmployeeId, Code = EF.Functions.Collate(f.Code, "Latin1_General_100_BIN2") };
        var owned = (from a in db.BudgetOwnerCostCenterAssignments.AsNoTracking()
                     join c in db.CostCenters.AsNoTracking() on a.CostCenterId equals c.Id
                     where a.IsActive && c.IsActive && !c.IsGroup && authority.Any(f => f.EmployeeId == a.EmployeeId && f.Code == "BUDGET_OWNER")
                     select new { a.EmployeeId, a.CostCenterId })
            .Union(from a in db.CfoInitiatorCostCenterAssignments.AsNoTracking()
                   join c in db.CostCenters.AsNoTracking() on a.CostCenterId equals c.Id
                   where a.IsActive && c.IsActive && !c.IsGroup && authority.Any(f => f.EmployeeId == a.EmployeeId && f.Code == "CFO_INITIATOR")
                   select new { a.EmployeeId, a.CostCenterId });
        var requests = db.ExpenseRequests.AsNoTracking().Where(r => r.Status == ExpenseRequestStatus.Returned);
        if (requestId.HasValue) requests = requests.Where(r => r.Id == requestId);
        var rows = await (from r in requests
            join e in creators on r.InitiatorExternalId equals e.ActorExternalId
            where EF.Functions.Collate(r.InitiatorExternalId, "Latin1_General_100_BIN2") == e.ActorExternalId
              && EF.Functions.DataLength(r.InitiatorExternalId) == EF.Functions.DataLength(e.ActorExternalId)
              && db.Organizations.Any(o => o.Id == r.OrganizationId && o.IsActive)
              && db.CostCenters.Any(c => c.Id == r.DefaultCostCenterId && c.IsActive && !c.IsGroup)
              && owned.Any(a => a.EmployeeId == e.Id && a.CostCenterId == r.DefaultCostCenterId)
              && !r.Lines.Any(l => l.CostCenterId.HasValue
                   && !owned.Any(a => a.EmployeeId == e.Id && a.CostCenterId == l.CostCenterId))
            from decision in db.BusinessAuditEntries.AsNoTracking()
                .Where(a => a.ObjectType == "ExpenseRequest" && a.ObjectId == r.Id.ToString() && a.Action == "WorkflowReturned")
                .OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id).Take(1)
            select new { RequestId = r.Id, EmployeeId = e.Id, e.Name, e.ActorExternalId, r.RequestNumber, r.CurrencyCode, r.Purpose,
                Amount = r.Lines.Sum(l => (decimal?)l.Amount) ?? 0m,
                Organization = db.Organizations.Where(o => o.Id == r.OrganizationId).Select(o => o.Name).FirstOrDefault(),
                CostCenter = db.CostCenters.Where(c => c.Id == r.DefaultCostCenterId).Select(c => c.Name).FirstOrDefault(),
                DecisionId = decision.Id, decision.OccurredAtUtc, decision.AfterJson }).ToArrayAsync(token);
        return rows.Where(r => ActorIdentity.IsCanonical(r.ActorExternalId) && !string.IsNullOrWhiteSpace(r.RequestNumber) && (actor is null || r.ActorExternalId == actor))
            .Select(r => new TaskMailTask(r.RequestId, r.EmployeeId, "REQUEST_AUTHOR", "return:" + r.DecisionId.ToString("N"),
                r.ActorExternalId, r.Name, r.RequestNumber!, r.Organization ?? "", r.CostCenter ?? "", r.Amount,
                r.CurrencyCode, r.Purpose, ReturnReason377(r.AfterJson),
                DateTimeOffset.FromUnixTimeMilliseconds(r.OccurredAtUtc.ToUnixTimeMilliseconds()))).ToArray();
    }

    internal static string? ReturnReason377(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 1_000_000) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("commandCode", out var command) && command.ValueKind == JsonValueKind.String && command.GetString() == "RETURN"
                && document.RootElement.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String)
            {
                var value = reason.GetString()?.Trim();
                if (!string.IsNullOrEmpty(value) && value.Length <= 1000
                    && !value.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))) return value;
            }
        }
        catch (JsonException) { }
        return null;
    }
}
