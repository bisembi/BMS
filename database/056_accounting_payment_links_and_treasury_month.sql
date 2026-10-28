-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
IF DB_NAME()<>CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase')) AND NOT(DB_NAME() IN(N'BmsGoLive258_the explicitly owned BMS database',N'BmsGoLive258r2_the explicitly owned BMS database') AND EXISTS(
 SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BmsGoLive258Owner'
  AND CONVERT(nvarchar(128),value)=N'BMS_GO_LIVE_258_ISOLATED'))
 THROW 65600,N'Exact the explicitly owned BMS database or owned acceptance restore required.',1;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lock int;
 EXEC @lock=sys.sp_getapplock @Resource=N'BMS:SQL056:AccountingPayments',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
 IF @lock<0 THROW 65601,N'Accounting payment migration lock unavailable.',1;
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=55
  AND script_name COLLATE Latin1_General_100_BIN2=N'055_director_owner_expense_route.sql')
  THROW 65602,N'Exact SQL055 predecessor required.',1;
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=56)
 BEGIN
  IF COL_LENGTH(N'bms_web.ExpenseRequests',N'TreasuryBudgetMonth') IS NOT NULL
   OR OBJECT_ID(N'bms_web.AccountingPaymentLinks') IS NOT NULL
   OR OBJECT_ID(N'bms_web.AccountingPaymentLinkLines') IS NOT NULL
   THROW 65603,N'Unregistered accounting feature objects refused.',1;
  ALTER TABLE bms_web.ExpenseRequests ADD TreasuryBudgetMonth date NULL;
  EXEC(N'ALTER TABLE bms_web.ExpenseRequests WITH CHECK ADD CONSTRAINT CK_ExpenseRequests_TreasuryBudgetMonth
   CHECK(TreasuryBudgetMonth IS NULL OR DAY(TreasuryBudgetMonth)=1)');
  CREATE TABLE bms_web.AccountingPaymentLinks(
   Id uniqueidentifier NOT NULL CONSTRAINT PK_AccountingPaymentLinks PRIMARY KEY,
   DocumentKey nvarchar(150) COLLATE Latin1_General_100_BIN2 NOT NULL,
   FinancialHash nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
   Revision int NOT NULL,SourceBatchId uniqueidentifier NOT NULL,
   ExpenseRequestId uniqueidentifier NOT NULL,EventId uniqueidentifier NOT NULL,
   CurrencyCode nvarchar(3) COLLATE Latin1_General_100_BIN2 NOT NULL,
   Amount decimal(18,2) NOT NULL,AmountKzt decimal(18,2) NOT NULL,PaymentDay date NOT NULL,
   SourceLegsJson nvarchar(max) NOT NULL,MatchedAtUtc datetimeoffset(3) NOT NULL,
   ReversedAtUtc datetimeoffset(3) NULL,ReversedByBatchId uniqueidentifier NULL,
   CONSTRAINT FK_AccountingPaymentLinks_Request FOREIGN KEY(ExpenseRequestId) REFERENCES bms_web.ExpenseRequests(Id),
   CONSTRAINT CK_AccountingPaymentLinks_Content CHECK(
    Id<>'00000000-0000-0000-0000-000000000000' AND SourceBatchId<>'00000000-0000-0000-0000-000000000000'
    AND EventId<>'00000000-0000-0000-0000-000000000000' AND Revision>0 AND Amount>0 AND AmountKzt>0
    AND ISJSON(SourceLegsJson)=1 AND LEN(DocumentKey)>0 AND LEN(FinancialHash)=64
    AND FinancialHash NOT LIKE N'%[^0-9A-F]%' COLLATE Latin1_General_100_BIN2
    AND LEN(CurrencyCode)=3 AND CurrencyCode NOT LIKE N'%[^A-Z]%' COLLATE Latin1_General_100_BIN2
    AND DATEPART(TZOFFSET,MatchedAtUtc)=0
    AND ((ReversedAtUtc IS NULL AND ReversedByBatchId IS NULL) OR (ReversedAtUtc IS NOT NULL AND ReversedAtUtc>=MatchedAtUtc
      AND DATEPART(TZOFFSET,ReversedAtUtc)=0 AND ReversedByBatchId IS NOT NULL))))
  ;
  CREATE UNIQUE INDEX UX_AccountingPaymentLinks_ActiveDocument ON bms_web.AccountingPaymentLinks(DocumentKey) WHERE ReversedAtUtc IS NULL;
  CREATE UNIQUE INDEX UX_AccountingPaymentLinks_Revision ON bms_web.AccountingPaymentLinks(DocumentKey,Revision);
  CREATE UNIQUE INDEX UX_AccountingPaymentLinks_Event ON bms_web.AccountingPaymentLinks(EventId);
  CREATE INDEX IX_AccountingPaymentLinks_Request ON bms_web.AccountingPaymentLinks(ExpenseRequestId);
  CREATE TABLE bms_web.AccountingPaymentLinkLines(
   LinkId uniqueidentifier NOT NULL,ExpenseRequestLineId uniqueidentifier NOT NULL,
   CostCenterId uniqueidentifier NOT NULL,CashFlowItemId uniqueidentifier NOT NULL,
   BudgetMonth date NOT NULL,OriginalReservePaidKzt decimal(18,2) NOT NULL,
   CONSTRAINT PK_AccountingPaymentLinkLines PRIMARY KEY(LinkId,ExpenseRequestLineId),
   CONSTRAINT FK_AccountingPaymentLinkLines_Link FOREIGN KEY(LinkId) REFERENCES bms_web.AccountingPaymentLinks(Id),
   CONSTRAINT FK_AccountingPaymentLinkLines_Line FOREIGN KEY(ExpenseRequestLineId) REFERENCES bms_web.ExpenseRequestLines(Id),
   CONSTRAINT FK_AccountingPaymentLinkLines_Position FOREIGN KEY(CostCenterId,CashFlowItemId,BudgetMonth)
      REFERENCES bms_web.BudgetPositions(CostCenterId,CashFlowItemId,BudgetMonth),
   CONSTRAINT CK_AccountingPaymentLinkLines_Amount CHECK(OriginalReservePaidKzt>=0 AND DAY(BudgetMonth)=1));
  CREATE INDEX IX_AccountingPaymentLinkLines_Budget ON bms_web.AccountingPaymentLinkLines(CostCenterId,CashFlowItemId,BudgetMonth);
  EXEC(N'CREATE TRIGGER bms_web.TR_AccountingPaymentLinkLines_Immutable ON bms_web.AccountingPaymentLinkLines AFTER INSERT,UPDATE,DELETE AS
   BEGIN SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM deleted) OR EXISTS(
     SELECT 1 FROM inserted i JOIN bms_web.AccountingPaymentLinks p ON p.Id=i.LinkId
      JOIN bms_web.ExpenseRequestLines l ON l.Id=i.ExpenseRequestLineId
      JOIN bms_web.ExpenseRequests r ON r.Id=p.ExpenseRequestId
     WHERE p.ReversedAtUtc IS NOT NULL OR l.ExpenseRequestId<>p.ExpenseRequestId
       OR l.CostCenterId IS NULL OR l.CostCenterId<>i.CostCenterId OR l.CashFlowItemId IS NULL OR l.CashFlowItemId<>i.CashFlowItemId
       OR i.OriginalReservePaidKzt>l.AmountKzt OR l.AmountKzt IS NULL OR r.PlannedPaymentDate IS NULL
       OR i.BudgetMonth<>COALESCE(r.TreasuryBudgetMonth,DATEFROMPARTS(YEAR(r.PlannedPaymentDate),MONTH(r.PlannedPaymentDate),1)))
     THROW 65610,N''Payment allocation must match its immutable request line and month.'',1;
   END');
  EXEC(N'CREATE TRIGGER bms_web.TR_AccountingPaymentLinks_Revision ON bms_web.AccountingPaymentLinks AFTER INSERT,UPDATE,DELETE AS
   BEGIN SET NOCOUNT ON;
    IF EXISTS(SELECT Id,DocumentKey,FinancialHash,Revision,SourceBatchId,ExpenseRequestId,EventId,CurrencyCode,Amount,AmountKzt,PaymentDay,SourceLegsJson,MatchedAtUtc FROM deleted
      EXCEPT SELECT Id,DocumentKey,FinancialHash,Revision,SourceBatchId,ExpenseRequestId,EventId,CurrencyCode,Amount,AmountKzt,PaymentDay,SourceLegsJson,MatchedAtUtc FROM inserted)
     OR EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id
       WHERE i.Id IS NULL OR d.ReversedAtUtc IS NOT NULL OR i.ReversedAtUtc IS NULL OR i.ReversedByBatchId IS NULL)
    THROW 65611,N''Only an explicit first reversal can change payment linkage evidence.'',1;
    IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(
      SELECT 1 FROM bms_web.ExpenseRequestPaymentFacts f JOIN bms_web.Currencies c ON c.Id=f.CurrencyId
      WHERE f.EventId=i.EventId AND f.ExpenseRequestId=i.ExpenseRequestId AND c.Code=i.CurrencyCode
       AND f.Amount=i.Amount AND f.AmountKzt=i.AmountKzt AND f.IsReversal=0
       AND f.SourceSystem=N''1C_ACCOUNTING_258''
       AND CONVERT(date,DATEADD(hour,5,f.PaidAt))=i.PaymentDay))
     THROW 65613,N''Payment link must identify its exact confirmed bank fact.'',1;
   END');
  INSERT bms_web.schema_version(version_no,script_name) VALUES(56,N'056_accounting_payment_links_and_treasury_month.sql');
 END;
 IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.ExpenseRequests')
    AND name=N'TreasuryBudgetMonth' AND system_type_id=40 AND is_nullable=1)
   OR (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.AccountingPaymentLinks'))<>15
   OR (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.AccountingPaymentLinkLines'))<>6
   OR NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=56
      AND script_name COLLATE Latin1_General_100_BIN2=N'056_accounting_payment_links_and_treasury_month.sql')
  THROW 65612,N'Accounting payment feature shape differs.',1;
 COMMIT;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
