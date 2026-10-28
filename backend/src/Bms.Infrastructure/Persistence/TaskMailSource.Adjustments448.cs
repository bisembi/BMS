using System.Globalization;
using Bms.Infrastructure.Mail;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed partial class TaskMailSource
{
    // Shared by the inbox, notification bell and mail. Only the captured
    // current director, with a live exact role and unambiguous actor mapping.
    internal const string AdjustmentMailSql448 = """
        SELECT DISTINCT adjustment.Id AS RequestId, adjustment.SubmissionNo,
            stage.Number AS StageNumber, employee.Id AS EmployeeId,
            stage.RoleCode AS Role, mapping.ActorExternalId, employee.Name AS EmployeeName,
            adjustment.Number, budget.Number AS BaseBudgetNumber, center.Name AS CostCenter,
            center.Id AS CostCenterId, budget.ValidFrom, budget.ValidTo, adjustment.UpdatedAtUtc,
            COALESCE((SELECT SUM(move.AmountKzt)
                FROM OPENJSON(CASE WHEN ISJSON(adjustment.TransfersJson)=1 THEN adjustment.TransfersJson ELSE N'[]' END)
                    WITH (AmountKzt decimal(19,2) '$.amountKzt') move),0) AS Amount
        FROM bms_web.BudgetAdjustments adjustment
        JOIN bms_web.BudgetInstances budget ON budget.Id=adjustment.BaseBudgetId
        JOIN bms_web.CostCenters center ON center.Id=adjustment.CostCenterId
        CROSS APPLY OPENJSON(CASE WHEN ISJSON(adjustment.StagesJson)=1 THEN adjustment.StagesJson ELSE N'[]' END)
            WITH (Number int '$.number', RoleCode nvarchar(100) '$.roleCode',
                  EmployeeIds nvarchar(max) '$.employeeIds' AS JSON) stage
        CROSS APPLY OPENJSON(CASE WHEN ISJSON(stage.EmployeeIds)=1 THEN stage.EmployeeIds ELSE N'[]' END) member
        JOIN bms_web.Employees employee ON employee.Id=TRY_CONVERT(uniqueidentifier,member.value)
        JOIN bms_web.EmployeeActorMappings mapping ON mapping.EmployeeId=employee.Id
        WHERE adjustment.SubmissionNo>0 AND budget.Status=N'Approved'
            AND adjustment.CostCenterId=budget.CostCenterId AND center.IsActive=1 AND center.IsGroup=0
            AND ((adjustment.State COLLATE Latin1_General_100_BIN2=N'PendingFd' AND DATALENGTH(adjustment.State)=18
                    AND stage.Number=1 AND stage.RoleCode COLLATE Latin1_General_100_BIN2=N'FIN_DIRECTOR' AND DATALENGTH(stage.RoleCode)=24)
                OR (adjustment.State COLLATE Latin1_General_100_BIN2=N'PendingGd' AND DATALENGTH(adjustment.State)=18
                    AND stage.Number=2 AND stage.RoleCode COLLATE Latin1_General_100_BIN2=N'GENERAL_DIRECTOR' AND DATALENGTH(stage.RoleCode)=32))
            AND employee.IsActive=1 AND mapping.IsActive=1 AND employee.Id<>'00000000-0000-0000-0000-000000000000'
            AND DATALENGTH(mapping.ActorExternalId)>0 AND DATALENGTH(mapping.ActorExternalId)=DATALENGTH(LTRIM(RTRIM(mapping.ActorExternalId)))
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
        """;

    public async Task<IReadOnlyList<TaskMailTask>> ReadAdjustments448Async(Guid? adjustmentId, Guid? employeeId, CancellationToken token)
    {
        if (!AdjustmentsEnabled448) return [];
        var query = db.Database.SqlQueryRaw<AdjustmentMailRow448>(AdjustmentMailSql448);
        if (adjustmentId.HasValue) query = query.Where(row => row.RequestId == adjustmentId.Value);
        if (employeeId.HasValue) query = query.Where(row => row.EmployeeId == employeeId.Value);
        return (await query.ToArrayAsync(token)).Select(ToAdjustmentTask448).ToArray();
    }

    internal static TaskMailTask ToAdjustmentTask448(AdjustmentMailRow448 row) => new(row.RequestId, row.EmployeeId,
        row.Role, $"adjustment:{row.SubmissionNo}:stage:{row.StageNumber}", row.ActorExternalId, row.EmployeeName,
        row.Number, "", row.CostCenter, row.Amount, "KZT",
        $"{row.BaseBudgetNumber}: {row.ValidFrom.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)} — {row.ValidTo.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}",
        DocumentKind: TaskMailDocumentKinds.BudgetAdjustment);

    internal sealed class AdjustmentMailRow448
    {
        public Guid RequestId { get; set; }
        public int SubmissionNo { get; set; }
        public int StageNumber { get; set; }
        public Guid EmployeeId { get; set; }
        public string Role { get; set; } = "";
        public string ActorExternalId { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string Number { get; set; } = "";
        public string BaseBudgetNumber { get; set; } = "";
        public string CostCenter { get; set; } = "";
        public Guid CostCenterId { get; set; }
        public DateOnly ValidFrom { get; set; }
        public DateOnly ValidTo { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public decimal Amount { get; set; }
    }
}
