using System.Globalization;
using Bms.Infrastructure.Mail;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed partial class TaskMailSource
{
    // Only the latest, unchanged, pending document and its captured current
    // participants. Role inventory is rechecked independently of any UI role.
    // The read-only SQL is also exercised against session-local test tables.
    internal const string BudgetMailSql391 = """
        SELECT DISTINCT budget.Id AS RequestId, review.Id AS ReviewId, stage.StageNumber,
            employee.Id AS EmployeeId, stage.RoleCode AS Role, mapping.ActorExternalId,
            employee.Name AS EmployeeName, budget.Number, center.Name AS CostCenter,
            budget.ValidFrom, budget.ValidTo,
            COALESCE((SELECT SUM(line.LimitAmountKzt) FROM bms_web.BudgetInstanceLines line
                WHERE line.BudgetInstanceId=budget.Id AND line.FlowDirection=N'Payment'),0) AS Amount
        FROM bms_web.BudgetApprovalSubmissions review
        JOIN bms_web.BudgetInstances budget ON budget.Id=review.BudgetInstanceId
        JOIN bms_web.CostCenters center ON center.Id=budget.CostCenterId
        CROSS APPLY OPENJSON(CASE WHEN ISJSON(review.StagesJson)=1 THEN review.StagesJson ELSE N'[]' END)
            WITH (StageNumber int '$.stageNumber', RoleCode nvarchar(100) '$.roleCode',
                  EmployeeIds nvarchar(max) '$.employeeIds' AS JSON) stage
        CROSS APPLY OPENJSON(CASE WHEN ISJSON(stage.EmployeeIds)=1 THEN stage.EmployeeIds ELSE N'[]' END) member
        JOIN bms_web.Employees employee ON employee.Id=TRY_CONVERT(uniqueidentifier,member.value)
        JOIN bms_web.EmployeeActorMappings mapping ON mapping.EmployeeId=employee.Id
        WHERE review.State=N'Pending' AND review.CurrentStageNumber=stage.StageNumber
            AND budget.Status=N'Draft' AND budget.ConcurrencyStamp=review.ProposalConcurrencyStamp
            AND budget.CostCenterId=review.CostCenterId AND center.IsActive=1 AND center.IsGroup=0
            AND employee.IsActive=1 AND mapping.IsActive=1 AND employee.Id<>'00000000-0000-0000-0000-000000000000'
            AND DATALENGTH(mapping.ActorExternalId)>0 AND DATALENGTH(mapping.ActorExternalId)=DATALENGTH(LTRIM(RTRIM(mapping.ActorExternalId)))
            AND DATALENGTH(stage.RoleCode)=LEN(stage.RoleCode)*2
            AND stage.RoleCode COLLATE Latin1_General_100_BIN2 IN(N'BUDGET_OWNER',N'FIN_CONTROLLER',N'FIN_DIRECTOR',N'GENERAL_DIRECTOR')
            AND (SELECT COUNT(*) FROM bms_web.EmployeeActorMappings m WHERE m.EmployeeId=employee.Id AND m.IsActive=1)=1
            AND (SELECT COUNT(*) FROM bms_web.EmployeeActorMappings m WHERE m.ActorExternalId=mapping.ActorExternalId)=1
            AND EXISTS(SELECT 1 FROM bms_web.EmployeeFunctionalRoles assignment
                JOIN bms_web.FunctionalRoles role ON role.Id=assignment.FunctionalRoleId
                WHERE assignment.EmployeeId=employee.Id AND assignment.IsActive=1 AND role.IsActive=1
                    AND role.Code COLLATE Latin1_General_100_BIN2=stage.RoleCode COLLATE Latin1_General_100_BIN2
                    AND DATALENGTH(role.Code)=DATALENGTH(stage.RoleCode))
            AND NOT EXISTS(SELECT 1 FROM bms_web.EmployeeFunctionalRoles assignment
                JOIN bms_web.FunctionalRoles role ON role.Id=assignment.FunctionalRoleId
                WHERE assignment.EmployeeId=employee.Id AND assignment.IsActive=1
                    AND (role.IsActive=0 OR role.Code IN(N'BMS_АдминистраторИнтеграции',N'BMS.IntegrationAdministrator')))
            AND (stage.RoleCode<>N'BUDGET_OWNER' OR (review.SubmittedByEmployeeId<>employee.Id
                AND EXISTS(SELECT 1 FROM bms_web.BudgetOwnerCostCenterAssignments assignment
                    WHERE assignment.EmployeeId=employee.Id AND assignment.CostCenterId=center.Id AND assignment.IsActive=1)))
            AND NOT EXISTS(SELECT 1 FROM bms_web.BudgetApprovalSubmissions later
                WHERE later.BudgetInstanceId=review.BudgetInstanceId AND later.SubmissionNumber>review.SubmissionNumber)
        """;

    public async Task<IReadOnlyList<TaskMailTask>> ReadBudgets391Async(Guid? budgetId,Guid? employeeId,CancellationToken token)
    {
        var query=db.Database.SqlQueryRaw<BudgetMailRow391>(BudgetMailSql391);
        if(budgetId.HasValue)query=query.Where(r=>r.RequestId==budgetId.Value);
        if(employeeId.HasValue)query=query.Where(r=>r.EmployeeId==employeeId.Value);
        return (await query.ToArrayAsync(token)).Select(ToBudgetTask391).ToArray();
    }

    internal static TaskMailTask ToBudgetTask391(BudgetMailRow391 row)=>new(row.RequestId,row.EmployeeId,row.Role,
        $"review:{row.ReviewId:N}:stage:{row.StageNumber}",row.ActorExternalId,row.EmployeeName,row.Number,"",row.CostCenter,
        row.Amount,"KZT",row.ValidFrom.ToString("dd.MM.yyyy",CultureInfo.InvariantCulture)+" — "+row.ValidTo.ToString("dd.MM.yyyy",CultureInfo.InvariantCulture),
        DocumentKind:TaskMailDocumentKinds.BudgetInstance);

    internal sealed class BudgetMailRow391
    {
        public Guid RequestId {get;set;}
        public Guid ReviewId {get;set;}
        public int StageNumber {get;set;}
        public Guid EmployeeId {get;set;}
        public string Role {get;set;}="";
        public string ActorExternalId {get;set;}="";
        public string EmployeeName {get;set;}="";
        public string Number {get;set;}="";
        public string CostCenter {get;set;}="";
        public DateOnly ValidFrom {get;set;}
        public DateOnly ValidTo {get;set;}
        public decimal Amount {get;set;}
    }
}
