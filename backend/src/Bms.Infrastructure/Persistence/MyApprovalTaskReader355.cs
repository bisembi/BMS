using System.Data;
using Bms.Application.Authorization;
using Bms.Application.Tasks;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Budgeting;
using Bms.Domain.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace Bms.Infrastructure.Persistence;

public sealed class MyApprovalTaskReader355(BmsDbContext db, IConfiguration? configuration = null) : IMyApprovalTaskReader355
{
    public async Task<MyApprovalTaskPage355> ReadAdjustments448Async(string actorExternalId, Guid employeeId,
        BusinessDataReadScope scope, int skip, int take, CancellationToken token)
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Task reads require a Serializable transaction.");
        var query = AdjustmentQuery448(actorExternalId, employeeId, scope);
        var total = await query.CountAsync(token);
        var items = await query.OrderByDescending(row => row.ReceivedAtUtc).ThenBy(row => row.Id)
            .Skip(skip).Take(take).ToArrayAsync(token);
        return new(items, skip, take, total);
    }

    internal IQueryable<MyApprovalTask355> AdjustmentQuery448(string actorExternalId, Guid employeeId, BusinessDataReadScope scope)
    {
        var employees = BusinessDataReadScopeQuery.EmployeeIds(db, actorExternalId).Where(id => id == employeeId);
        var roles = BusinessDataReadScopeQuery.RoleCodes(db, employees);
        var centers = scope.CostCenterIds.ToArray();
        var enabled = configuration?.GetValue<bool>("Bms:BudgetAdjustments:WriteEnabled") ?? true;
        var grantActor471=BusinessDataReadScopeQuery.GrantActor471(db,actorExternalId);
        return from task in db.Database.SqlQueryRaw<TaskMailSource.AdjustmentMailRow448>(TaskMailSource.AdjustmentMailSql448)
               where enabled && employees.Count() == 1 && task.EmployeeId == employeeId
                   && EF.Functions.Collate(task.ActorExternalId, "Latin1_General_100_BIN2") == grantActor471
                   && EF.Functions.DataLength(task.ActorExternalId) == EF.Functions.DataLength(grantActor471)
                   && roles.Contains(EF.Functions.Collate(task.Role, "Latin1_General_100_BIN2"))
                   && (scope.CanReadAll || centers.Contains(task.CostCenterId))
               select new MyApprovalTask355 { Kind = "BudgetAdjustment", Id = task.RequestId, Number = task.Number,
                   Title = EF.Functions.Collate("Корректировка бюджета " + task.BaseBudgetNumber, "DATABASE_DEFAULT"), CostCenterName = task.CostCenter,
                   RoleCode = task.Role, ReceivedAtUtc = task.UpdatedAtUtc };
    }
    public async Task<MyApprovalTaskPage355> ReadAsync(string actorExternalId, Guid employeeId,
        BusinessDataReadScope scope, int skip, int take, CancellationToken token)
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Task reads require a Serializable transaction.");
        var employeeIds = BusinessDataReadScopeQuery.EmployeeIds(db, actorExternalId).Where(id => id == employeeId);
        var roleCodes = await BusinessDataReadScopeQuery.RoleCodes(db, employeeIds).Distinct().ToArrayAsync(token);
        Guid[]? candidates = null;
        if (!roleCodes.Contains(ExpenseRequestWorkflowCodes.NsiSpecialistRoleCode)
            && !roleCodes.Contains(ExpenseRequestWorkflowCodes.TreasurerRoleCode))
        {
            // Match the established inbox optimization: unrelated drafts must
            // not accumulate Serializable row locks merely to count approvals.
            var ids = await db.ApprovalRouteStageParticipants.AsNoTracking().Where(p => p.EmployeeId == employeeId)
                .Select(p => p.ExpenseRequestId).Distinct().Take(1001).ToArrayAsync(token);
            if (ids.Length <= 1000) candidates = ids;
        }
        var query = Query(actorExternalId, employeeId, scope, candidates);
        var total = await query.CountAsync(token);
        var items = await query.OrderByDescending(row => row.ReceivedAtUtc)
            .ThenBy(row => row.Kind).ThenBy(row => row.Id).Skip(skip).Take(take).ToArrayAsync(token);
        return new(items, skip, take, total);
    }

    internal IQueryable<MyApprovalTask355> Query(string actorExternalId, Guid employeeId, BusinessDataReadScope scope, Guid[]? candidateIds = null)
    {
        // Use the same stable clustered root as the existing workflow inbox.
        // Count/page never inspect budget facts or write execution movements.
        var source = db.ExpenseRequests.FromSqlRaw("""
            SELECT [request].* FROM [bms_web].[ExpenseRequests] AS [request]
                WITH (INDEX([PK_bms_web_ExpenseRequests]))
            """).AsNoTracking();
        var employees = BusinessDataReadScopeQuery.EmployeeIds(db, actorExternalId).Where(id => id == employeeId);
        var requests = ExpenseRequestVisibilityQuery.ApplyCapturedListScope(db, source,
            actorExternalId, scope, onlyMyTasks: true).Where(request =>
                employees.Count() == 1 && (request.Status == ExpenseRequestStatus.InApproval
                || request.Status == ExpenseRequestStatus.AwaitingNsi
                || request.Status == ExpenseRequestStatus.Approved));
        if (candidateIds is not null) requests = requests.Where(request => candidateIds.Contains(request.Id));
        var expenses = from request in requests
                       join center in db.CostCenters.AsNoTracking() on request.DefaultCostCenterId equals center.Id into centers
                       from center in centers.DefaultIfEmpty()
                       select new MyApprovalTask355 { Kind = "ExpenseRequest", Id = request.Id, Number = request.RequestNumber ?? "Без номера",
                           Title = request.Purpose ?? "Расходная заявка", CostCenterName = center == null ? "" : center.Name,
                           RoleCode = request.Status == ExpenseRequestStatus.AwaitingNsi ? "NSI_SPECIALIST"
                           : request.Status == ExpenseRequestStatus.Approved ? "TREASURER"
                           : db.ApprovalRouteStages.AsNoTracking().Where(stage => stage.ExpenseRequestId == request.Id
                               && stage.SnapshotNumber == request.ActiveApprovalRouteSnapshotNumber
                               && stage.StageNumber == request.ActiveApprovalRouteStageNumber)
                               .Select(stage => stage.RoleCode).FirstOrDefault() ?? "",
                           ReceivedAtUtc = request.CreatedAtUtc };

        var roles = BusinessDataReadScopeQuery.RoleCodes(db, employees);
        var owned = BusinessDataReadScopeQuery.OwnedCostCenterIds(db, employeeId);
        var capturedIds = scope.CostCenterIds.ToArray();
        var budgetTasks = db.Database.SqlQuery<BudgetTaskStage355>($"""
            SELECT review.BudgetInstanceId, review.CostCenterId, stage.RoleCode, review.SubmittedByEmployeeId,
                   review.SubmittedAtUtc, review.ProposalConcurrencyStamp
            FROM bms_web.BudgetApprovalSubmissions review
            CROSS APPLY OPENJSON(CASE WHEN ISJSON(review.StagesJson) = 1 THEN review.StagesJson ELSE N'[]' END)
                WITH (StageNumber int '$.stageNumber', RoleCode nvarchar(100) '$.roleCode',
                      EmployeeIds nvarchar(max) '$.employeeIds' AS JSON) stage
            WHERE review.State = N'Pending' AND review.CurrentStageNumber = stage.StageNumber
              AND DATALENGTH(stage.RoleCode) = LEN(stage.RoleCode) * 2
              AND EXISTS (SELECT 1 FROM OPENJSON(stage.EmployeeIds) member
                          WHERE TRY_CONVERT(uniqueidentifier, member.value) = {employeeId})
              AND NOT EXISTS (SELECT 1 FROM bms_web.BudgetApprovalSubmissions later
                              WHERE later.BudgetInstanceId = review.BudgetInstanceId
                                AND later.SubmissionNumber > review.SubmissionNumber)
            """);
        var budgets = from task in budgetTasks
                      join budget in db.BudgetInstances.AsNoTracking() on task.BudgetInstanceId equals budget.Id
                      join center in db.CostCenters.AsNoTracking() on budget.CostCenterId equals center.Id
                      where employees.Count() == 1 && roles.Contains(EF.Functions.Collate(task.RoleCode, "Latin1_General_100_BIN2"))
                          && budget.Status == BudgetInstanceStatus.Draft && budget.ConcurrencyStamp == task.ProposalConcurrencyStamp
                          && budget.CostCenterId == task.CostCenterId
                          && center.IsActive && !center.IsGroup && (scope.CanReadAll || capturedIds.Contains(center.Id))
                          && (task.RoleCode != ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode
                              || (task.SubmittedByEmployeeId != employeeId && owned.Contains(center.Id)))
                      select new MyApprovalTask355 { Kind = "BudgetInstance", Id = budget.Id, Number = budget.Number, Title = center.Name,
                          CostCenterName = center.Name, RoleCode = task.RoleCode, ReceivedAtUtc = task.SubmittedAtUtc };
        return expenses.Concat(budgets).Concat(AdjustmentQuery448(actorExternalId, employeeId, scope));
    }

    internal sealed class BudgetTaskStage355
    {
        public Guid BudgetInstanceId { get; set; }
        public Guid CostCenterId { get; set; }
        public string RoleCode { get; set; } = "";
        public Guid SubmittedByEmployeeId { get; set; }
        public DateTimeOffset SubmittedAtUtc { get; set; }
        public Guid ProposalConcurrencyStamp { get; set; }
    }
}
