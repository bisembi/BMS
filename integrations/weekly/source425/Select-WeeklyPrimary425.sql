-- Read-only, complete global fetch inside the caller's short Serializable transaction.
-- Parameters: @ArticlePolicyJson nvarchar(max), @AsOfUtc datetimeoffset.
-- No TOP truncation. Caller enforces global/per-owner bounds and fails the entire fetch on overflow.
SELECT @AsOfUtc AS RequestedAsOfUtc,CONVERT(nvarchar(100),SERVERPROPERTY('MachineName')) AS MachineName,DB_NAME() AS DatabaseName,
 (SELECT MAX(version_no) FROM bms_web.schema_version) AS SchemaVersion,N'owner-primary-closure425-r1' AS SelectorVersion,
 (SELECT COUNT_BIG(*) FROM bms_web.ExpenseRequests WHERE Status=N'PAID') AS PaidRequestCount,
 (SELECT COUNT_BIG(*) FROM bms_web.ExpenseRequestLines l JOIN bms_web.ExpenseRequests r ON r.Id=l.ExpenseRequestId WHERE r.Status=N'PAID') AS PaidLineCount,
 (SELECT COUNT_BIG(*) FROM bms_web.BudgetOwnerCostCenterAssignments WHERE IsActive=1) AS ActiveOwnerAssignmentCount;

WITH Policy AS (
 SELECT * FROM OPENJSON(@ArticlePolicyJson,'$.articles') WITH(Id uniqueidentifier '$.id',Code nvarchar(20) '$.code',Name nvarchar(500) '$.name',IsGroup bit '$.isGroup',Classification nvarchar(50) '$.classification',Flag nvarchar(80) '$.flag')
), Paid AS (
 SELECT r.*,CASE WHEN r.PaymentOperationJson IS NULL THEN N'SupplierNull'
 WHEN ISJSON(r.PaymentOperationJson)<>1 THEN N'InvalidJson'
 WHEN JSON_VALUE(CASE WHEN ISJSON(r.PaymentOperationJson)=1 THEN r.PaymentOperationJson ELSE N'{}' END,'$.kind') COLLATE Latin1_General_100_BIN2 IN(N'Tax',N'Payroll',N'Accountable',N'PayrollAdvance',N'BusinessTrip')
 THEN JSON_VALUE(r.PaymentOperationJson,'$.kind') ELSE N'UnknownKind' END AS OperationKind
 FROM bms_web.ExpenseRequests r WHERE r.Status=N'PAID'
)
SELECT r.Id AS RequestId,r.RequestNumber,r.Status,r.CurrencyCode,r.FullyPaidAtUtc,r.ClosedPartially,r.LatestClosingDocumentsReceivedDate,r.ConfirmedClosingControlAmount,
 r.DefaultCostCenterId,r.OperationKind,
 JSON_VALUE(CASE WHEN ISJSON(r.PaymentOperationJson)=1 THEN r.PaymentOperationJson ELSE N'{}' END,'$.employeeName') AS OperationEmployeeName,
 JSON_QUERY(CASE WHEN ISJSON(r.PaymentOperationJson)=1 THEN r.PaymentOperationJson ELSE N'{}' END,'$.employeeNames') AS OperationEmployeeNamesJson,
 JSON_VALUE(CASE WHEN ISJSON(r.PaymentOperationJson)=1 THEN r.PaymentOperationJson ELSE N'{}' END,'$.recipientName') AS OperationRecipientName,
 JSON_VALUE(CASE WHEN ISJSON(r.PaymentOperationJson)=1 THEN r.PaymentOperationJson ELSE N'{}' END,'$.payrollMode') AS OperationPayrollMode,
 JSON_VALUE(CASE WHEN ISJSON(r.PaymentOperationJson)=1 THEN r.PaymentOperationJson ELSE N'{}' END,'$.registerNumber') AS OperationRegisterNumber,
 l.Id AS LineId,l.SortOrder,l.CashFlowItemId,
 c.Code AS CashFlowCode,c.Name AS CashFlowName,c.IsGroup AS ArticleIsGroup,
 CASE WHEN p.Id IS NULL OR c.Id IS NULL OR p.Code COLLATE Latin1_General_100_BIN2<>c.Code COLLATE Latin1_General_100_BIN2
 OR DATALENGTH(p.Code)<>DATALENGTH(c.Code) OR p.Name COLLATE Latin1_General_100_BIN2<>c.Name COLLATE Latin1_General_100_BIN2
 OR DATALENGTH(p.Name)<>DATALENGTH(c.Name) OR p.IsGroup<>c.IsGroup THEN N'HOLD_UNKNOWN_OR_CHANGED_ARTICLE' ELSE p.Classification END AS ArticleClassification,p.Flag AS ArticleFlag,
 l.Amount,l.AmountKzt,l.ConfirmedClosedAmount,l.ConfirmedClosedAmountKzt,l.AccrualSnapshotFormsManagerialAccrual,
 l.CostCenterId AS ExplicitLineCostCenterId,COALESCE(l.CostCenterId,r.DefaultCostCenterId) AS EffectiveCostCenterId,
 cc.Code AS CostCenterCode,cc.Name AS CostCenterName,cp.Name AS CounterpartyName,
 e.Id AS InitiatorEmployeeId,e.Name AS InitiatorName,e.IsActive AS InitiatorActive,m.IsActive AS InitiatorMappingActive,
 (SELECT COUNT_BIG(*) FROM bms_web.EmployeeActorMappings x WHERE x.ActorExternalId=r.InitiatorExternalId) AS InitiatorActorMatchCount
FROM Paid r LEFT JOIN bms_web.ExpenseRequestLines l ON l.ExpenseRequestId=r.Id
LEFT JOIN bms_web.CashFlowItems c ON c.Id=l.CashFlowItemId LEFT JOIN Policy p ON p.Id=l.CashFlowItemId
LEFT JOIN bms_web.CostCenters cc ON cc.Id=COALESCE(l.CostCenterId,r.DefaultCostCenterId)
LEFT JOIN bms_web.Counterparties cp ON cp.Id=r.CounterpartyId
LEFT JOIN bms_web.EmployeeActorMappings m ON m.ActorExternalId COLLATE Latin1_General_100_BIN2=r.InitiatorExternalId COLLATE Latin1_General_100_BIN2 AND DATALENGTH(m.ActorExternalId)=DATALENGTH(r.InitiatorExternalId)
LEFT JOIN bms_web.Employees e ON e.Id=m.EmployeeId ORDER BY r.Id,l.SortOrder,l.Id;

SELECT e.Id AS EmployeeId,e.Name AS EmployeeName,e.IsActive AS EmployeeActive,c.Id AS CostCenterId,c.IsActive AS CenterActive,c.IsGroup,
 a.IsActive AS AssignmentActive,m.ActorExternalId,m.IsActive AS MappingActive,
 (SELECT COUNT_BIG(*) FROM bms_web.EmployeeActorMappings x WHERE x.EmployeeId=e.Id) AS EmployeeMappingCount,
 (SELECT COUNT_BIG(*) FROM bms_web.EmployeeActorMappings x WHERE x.ActorExternalId=m.ActorExternalId) AS ActorMappingCount,
 CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM bms_web.EmployeeFunctionalRoles er JOIN bms_web.FunctionalRoles fr ON fr.Id=er.FunctionalRoleId WHERE er.EmployeeId=e.Id AND er.IsActive=1 AND fr.IsActive=1 AND fr.Code COLLATE Latin1_General_100_BIN2=N'BUDGET_OWNER' AND DATALENGTH(fr.Code)=24) THEN 1 ELSE 0 END) AS HasActiveOwnerRole
FROM bms_web.BudgetOwnerCostCenterAssignments a JOIN bms_web.Employees e ON e.Id=a.EmployeeId JOIN bms_web.CostCenters c ON c.Id=a.CostCenterId
LEFT JOIN bms_web.EmployeeActorMappings m ON m.EmployeeId=e.Id WHERE a.IsActive=1 ORDER BY e.Id,c.Id,m.ActorExternalId;

SELECT f.ExpenseRequestId AS RequestId,f.EventId,f.CurrencyId,c.Code AS CurrencyCode,f.Amount,f.AmountKzt,f.PaidAt,f.IsReversal,f.ReversedEventId
FROM bms_web.ExpenseRequestPaymentFacts f JOIN bms_web.ExpenseRequests r ON r.Id=f.ExpenseRequestId
LEFT JOIN bms_web.Currencies c ON c.Id=f.CurrencyId WHERE r.Status=N'PAID' ORDER BY f.ExpenseRequestId,f.PaidAt,f.EventId;

SELECT a.Id AS EvidenceId,a.ObjectId AS ApprovalId,r.Id AS RequestId,
 TRY_CONVERT(datetimeoffset,JSON_VALUE(a.AfterJson,'$.paidAtUtc')) AS EffectiveAtUtc,TRY_CONVERT(datetimeoffset,JSON_VALUE(a.AfterJson,'$.approvedAtUtc')) AS ApprovedAtUtc,
 TRY_CONVERT(uniqueidentifier,JSON_VALUE(j.value,'$.eventId')) AS EventId,TRY_CONVERT(uniqueidentifier,JSON_VALUE(j.value,'$.currencyId')) AS CurrencyId,
 TRY_CONVERT(decimal(19,4),JSON_VALUE(j.value,'$.obligationAmount')) AS ObligationAmount,
 TRY_CONVERT(decimal(19,4),JSON_VALUE(j.value,'$.cashAmount')) AS CashAmount,
 TRY_CONVERT(decimal(19,4),JSON_VALUE(j.value,'$.roundingAmount')) AS RoundingAmount,
 (SELECT COUNT_BIG(*) FROM bms_web.BusinessAudit z WHERE z.ObjectType=N'AccountingAllocation400' AND z.ObjectId=a.ObjectId AND z.Action=N'AccountingAllocationReversed') AS ReversalCount,
 (SELECT COUNT_BIG(*) FROM bms_web.BusinessAudit z WHERE z.ObjectType=N'AccountingAllocation400' AND z.ObjectId=a.ObjectId AND z.Action=N'AccountingAllocationApproved') AS ApprovalCount
FROM bms_web.BusinessAudit a CROSS APPLY OPENJSON(CASE WHEN ISJSON(a.AfterJson)=1 THEN a.AfterJson ELSE N'{}' END,'$.allocations') j
JOIN bms_web.ExpenseRequests r ON r.Id=TRY_CONVERT(uniqueidentifier,JSON_VALUE(j.value,'$.expenseRequestId'))
WHERE a.ObjectType=N'AccountingAllocation400' AND a.Action=N'AccountingAllocationApproved' AND r.Status=N'PAID'
ORDER BY r.Id,a.Id;

SELECT p.Id,p.Code,p.Name,p.Classification,c.Id AS ActualId,c.Code AS ActualCode,c.Name AS ActualName,c.IsGroup AS ActualIsGroup
FROM OPENJSON(@ArticlePolicyJson,'$.articles') WITH(Id uniqueidentifier '$.id',Code nvarchar(20) '$.code',Name nvarchar(500) '$.name',Classification nvarchar(50) '$.classification') p
LEFT JOIN bms_web.CashFlowItems c ON c.Id=p.Id WHERE p.Classification<>N'INCLUDE' ORDER BY p.Code;
