-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
-- Preserve immutable allocation evidence. Only PayrollAdvance uses its
-- server-resolved budget CFO; the owning CFO remains the request dimension.
IF HASHBYTES('SHA2_256',OBJECT_DEFINITION(OBJECT_ID(N'bms_web.TR_AccountingPaymentLinkLines_Immutable')))<>0xA9450023D3E98947F81CA9CABB3FB961E450959C444CB1EC5C525B768D13DA2D
 THROW 65506,'EXACT_PRIOR_ALLOCATION455_GUARD',1;
GO
ALTER TRIGGER bms_web.TR_AccountingPaymentLinkLines_Immutable ON bms_web.AccountingPaymentLinkLines AFTER INSERT,UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted) OR EXISTS(
  SELECT 1 FROM inserted i JOIN bms_web.AccountingPaymentLinks p ON p.Id=i.LinkId
   JOIN bms_web.ExpenseRequestLines l ON l.Id=i.ExpenseRequestLineId
   JOIN bms_web.ExpenseRequests r ON r.Id=p.ExpenseRequestId
   CROSS APPLY(SELECT CASE WHEN JSON_VALUE(r.PaymentOperationJson,'$.kind')=N'PayrollAdvance'
     THEN TRY_CONVERT(uniqueidentifier,JSON_VALUE(r.PaymentOperationJson,'$.budgetCostCenterId'))
     ELSE l.CostCenterId END ExpectedCostCenterId) d
  WHERE p.ReversedAtUtc IS NOT NULL OR l.ExpenseRequestId<>p.ExpenseRequestId
    OR l.CostCenterId IS NULL OR d.ExpectedCostCenterId IS NULL OR d.ExpectedCostCenterId<>i.CostCenterId
    OR l.CashFlowItemId IS NULL OR l.CashFlowItemId<>i.CashFlowItemId
    OR i.OriginalReservePaidKzt>l.AmountKzt OR l.AmountKzt IS NULL OR r.PlannedPaymentDate IS NULL
    OR i.BudgetMonth<>COALESCE(r.TreasuryBudgetMonth,DATEFROMPARTS(YEAR(r.PlannedPaymentDate),MONTH(r.PlannedPaymentDate),1)))
  THROW 65610,N'Payment allocation must match its immutable request line and month.',1;
END;
