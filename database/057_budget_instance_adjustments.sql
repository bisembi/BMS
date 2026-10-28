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
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
IF DB_NAME()<>CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase')) AND NOT(DB_NAME() IN(N'BmsGoLive258_the explicitly owned BMS database',N'BmsGoLive258r2_the explicitly owned BMS database',N'BmsBudget273_the explicitly owned BMS database')
 AND EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BmsGoLive258Owner'
 AND CONVERT(nvarchar(128),value)=N'BMS_GO_LIVE_258_ISOLATED'))
 THROW 65700,N'Exact the explicitly owned BMS database or owned acceptance restore required.',1;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lock int;
 EXEC @lock=sys.sp_getapplock @Resource=N'bms-web:company-budget-plan',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
 IF @lock<0 THROW 65701,N'Company plan lock unavailable.',1;
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=56 AND script_name=N'056_accounting_payment_links_and_treasury_month.sql')
 OR EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no>57) THROW 65702,N'Exact SQL056 predecessor required.',1;
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=57)
 BEGIN
  IF OBJECT_ID(N'bms_web.BudgetAdjustments') IS NOT NULL OR OBJECT_ID(N'bms_web.v_BudgetInstanceEffectiveLines') IS NOT NULL
    THROW 65703,N'Unregistered adjustment objects refused.',1;
  CREATE TABLE bms_web.BudgetAdjustments(
   Id uniqueidentifier NOT NULL CONSTRAINT PK_BudgetAdjustments PRIMARY KEY,
   BaseBudgetId uniqueidentifier NOT NULL,CostCenterId uniqueidentifier NOT NULL,SequenceNo int NOT NULL,
   Number nvarchar(80) COLLATE Latin1_General_100_BIN2 NOT NULL,
   State nvarchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,Stamp uniqueidentifier NOT NULL,
   Reason nvarchar(2000) NOT NULL,TransfersJson nvarchar(max) NOT NULL,StagesJson nvarchar(max) NOT NULL,
   DecisionsJson nvarchar(max) NOT NULL,BasisHash nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL,SubmissionNo int NOT NULL,
   AuthorEmployeeId uniqueidentifier NOT NULL,AuthorExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
   CreatedAtUtc datetimeoffset(3) NOT NULL,UpdatedAtUtc datetimeoffset(3) NOT NULL,ApprovedAtUtc datetimeoffset(3) NULL,
   ApprovedByEmployeeId uniqueidentifier NULL,ApprovalOrder int NULL,
   CONSTRAINT FK_BudgetAdjustments_Base FOREIGN KEY(BaseBudgetId) REFERENCES bms_web.BudgetInstances(Id),
   CONSTRAINT FK_BudgetAdjustments_Cfo FOREIGN KEY(CostCenterId) REFERENCES bms_web.CostCenters(Id),
   CONSTRAINT FK_BudgetAdjustments_Author FOREIGN KEY(AuthorEmployeeId) REFERENCES bms_web.Employees(Id),
   CONSTRAINT FK_BudgetAdjustments_Approver FOREIGN KEY(ApprovedByEmployeeId) REFERENCES bms_web.Employees(Id),
   CONSTRAINT CK_BudgetAdjustments_State CHECK(State IN(N'Draft',N'Returned',N'PendingFd',N'PendingGd',N'Approved',N'Rejected')),
   CONSTRAINT CK_BudgetAdjustments_Approval CHECK((State=N'Approved' AND ApprovedAtUtc IS NOT NULL AND ApprovedByEmployeeId IS NOT NULL AND ApprovalOrder>0)
     OR(State<>N'Approved' AND ApprovedAtUtc IS NULL AND ApprovedByEmployeeId IS NULL AND ApprovalOrder IS NULL)),
   CONSTRAINT CK_BudgetAdjustments_Json CHECK(ISJSON(TransfersJson)=1 AND ISJSON(StagesJson)=1 AND ISJSON(DecisionsJson)=1),
   CONSTRAINT CK_BudgetAdjustments_Ordinal CHECK(SequenceNo>0 AND SubmissionNo>=0),
   CONSTRAINT CK_BudgetAdjustments_Utc CHECK(DATEPART(TZOFFSET,CreatedAtUtc)=0 AND DATEPART(TZOFFSET,UpdatedAtUtc)=0
     AND(ApprovedAtUtc IS NULL OR DATEPART(TZOFFSET,ApprovedAtUtc)=0))
  );
  CREATE UNIQUE INDEX UX_BudgetAdjustments_Base_Sequence ON bms_web.BudgetAdjustments(BaseBudgetId,SequenceNo);
  CREATE UNIQUE INDEX UX_BudgetAdjustments_Base_Approval ON bms_web.BudgetAdjustments(BaseBudgetId,ApprovalOrder) WHERE ApprovalOrder IS NOT NULL;
  CREATE INDEX IX_BudgetAdjustments_Cfo_Created ON bms_web.BudgetAdjustments(CostCenterId,CreatedAtUtc);
  EXEC(N'CREATE TRIGGER bms_web.TR_BudgetAdjustments_Immutable ON bms_web.BudgetAdjustments AFTER INSERT,UPDATE,DELETE AS
  BEGIN
   SET NOCOUNT ON;
   IF EXISTS(SELECT 1 FROM deleted d WHERE d.State=N''Approved'') THROW 65704,N''Approved adjustments are immutable.'',1;
   IF EXISTS(SELECT 1 FROM inserted i JOIN bms_web.BudgetInstances b ON b.Id=i.BaseBudgetId
      WHERE b.Status<>N''Approved'' OR b.CostCenterId<>i.CostCenterId)
      THROW 65705,N''Adjustment requires its original approved budget and CFO.'',1;
  END');
 END;
 EXEC(N'CREATE OR ALTER VIEW bms_web.v_BudgetInstanceEffectiveLines AS
 WITH movements AS
 (SELECT l.BudgetInstanceId,l.CashFlowItemId,l.BudgetMonth,l.FlowDirection,l.LimitAmountKzt FROM bms_web.BudgetInstanceLines l
  UNION ALL
  SELECT a.BaseBudgetId,m.SourceArticleId,m.SourceMonth,CASE m.FlowDirection WHEN 0 THEN N''Payment'' ELSE N''Receipt'' END,-m.AmountKzt
   FROM bms_web.BudgetAdjustments a CROSS APPLY OPENJSON(a.TransfersJson)
   WITH(SourceArticleId uniqueidentifier ''$.sourceArticleId'',SourceMonth date ''$.sourceMonth'',
        AmountKzt decimal(18,2) ''$.amountKzt'',FlowDirection int ''$.flowDirection'') m WHERE a.State=N''Approved''
  UNION ALL
  SELECT a.BaseBudgetId,m.TargetArticleId,m.TargetMonth,CASE m.FlowDirection WHEN 0 THEN N''Payment'' ELSE N''Receipt'' END,m.AmountKzt
   FROM bms_web.BudgetAdjustments a CROSS APPLY OPENJSON(a.TransfersJson)
   WITH(TargetArticleId uniqueidentifier ''$.targetArticleId'',TargetMonth date ''$.targetMonth'',
        AmountKzt decimal(18,2) ''$.amountKzt'',FlowDirection int ''$.flowDirection'') m WHERE a.State=N''Approved'')
 SELECT BudgetInstanceId,CashFlowItemId,BudgetMonth,FlowDirection,CONVERT(decimal(18,2),SUM(LimitAmountKzt)) LimitAmountKzt
 FROM movements GROUP BY BudgetInstanceId,CashFlowItemId,BudgetMonth,FlowDirection');
 EXEC(N'CREATE OR ALTER VIEW bms_web.v_CompanyBudgetExecutionPositions AS
 WITH keys AS
 (SELECT CostCenterId,CashFlowItemId,BudgetMonth FROM bms_web.BudgetPositions
  UNION SELECT b.CostCenterId,l.CashFlowItemId,l.BudgetMonth
    FROM bms_web.CompanyBudgetActiveVersion a JOIN bms_web.CompanyBudgetVersionInstances m ON m.VersionId=a.VersionId
    JOIN bms_web.BudgetInstances b ON b.Id=m.BudgetInstanceId JOIN bms_web.v_BudgetInstanceEffectiveLines l ON l.BudgetInstanceId=b.Id
    WHERE l.FlowDirection COLLATE Latin1_General_100_BIN2=N''Payment'')
 SELECT k.CostCenterId,k.CashFlowItemId,k.BudgetMonth,
    CONVERT(decimal(18,2),ISNULL(limits.AmountKzt,0)) LimitAmountKzt,
    CONVERT(decimal(18,2),ISNULL(p.ReservedAmountKzt,0)) ReservedAmountKzt,
    CONVERT(decimal(18,2),ISNULL(p.PaidAmountKzt,0)) PaidAmountKzt
 FROM keys k CROSS JOIN bms_web.CompanyBudgetActiveVersion a
 LEFT JOIN bms_web.BudgetPositions p ON p.CostCenterId=k.CostCenterId AND p.CashFlowItemId=k.CashFlowItemId AND p.BudgetMonth=k.BudgetMonth
 OUTER APPLY bms_web.fn_CompanyBudgetWinner(a.VersionId,k.CostCenterId,k.BudgetMonth) winner
 OUTER APPLY(SELECT SUM(l.LimitAmountKzt) AmountKzt FROM bms_web.v_BudgetInstanceEffectiveLines l
    WHERE l.BudgetInstanceId=winner.Id AND l.CashFlowItemId=k.CashFlowItemId AND l.BudgetMonth=k.BudgetMonth
      AND l.FlowDirection COLLATE Latin1_General_100_BIN2=N''Payment'') limits');
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=57)
  INSERT bms_web.schema_version(version_no,script_name)
   VALUES(57,N'057_budget_instance_adjustments.sql');
 COMMIT;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
