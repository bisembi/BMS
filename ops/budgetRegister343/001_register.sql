-- Separate append-only register component. Core schema61 remains compatible.
-- Disabled until its source capture, reconciliation and application acceptance pass.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF CONVERT(nvarchar(128),SERVERPROPERTY('MachineName'))<>N'BMS'
 OR CONVERT(nvarchar(40),CONNECTIONPROPERTY('net_transport'))<>N'Shared memory'
 OR NOT ((DB_NAME()=N'DataBI' AND (SELECT MAX(version_no) FROM bms_web.schema_version)=61)
  OR (DB_NAME()=N'BmsAccrualEdit311_Test' AND EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BmsAccrualEdit311Owner' AND CONVERT(nvarchar(128),value)=N'BMS_ACCRUAL_EDIT_311_ISOLATED'))
  OR (DB_NAME()=N'BmsBudgetExecution343_Test' AND EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BmsBudgetExecution343Owner' AND CONVERT(nvarchar(128),value)=N'BMS_EXECUTION_343_ISOLATED')))
 THROW 51000,'Exact register installation database required',1;
IF SCHEMA_ID(N'bms_execution343') IS NOT NULL THROW 51000,'Register component already exists; inspect rather than overwrite',1;
BEGIN TRANSACTION;
EXEC(N'CREATE SCHEMA bms_execution343 AUTHORIZATION dbo');
CREATE TABLE bms_execution343.Component
(
 Id int NOT NULL CONSTRAINT PK_Execution343_Component PRIMARY KEY CONSTRAINT CK_Execution343_Singleton CHECK(Id=1),
 OwnerMarker uniqueidentifier NOT NULL,
 ComponentVersion int NOT NULL CONSTRAINT CK_Execution343_Version CHECK(ComponentVersion=1),
 Enabled bit NOT NULL,
 Revision bigint NOT NULL CONSTRAINT CK_Execution343_Revision CHECK(Revision>=0),
 LastPostedAtUtc datetimeoffset(7) NULL,
 CashSnapshotId uniqueidentifier NULL,
 CashGrossSHA256 char(64) COLLATE Latin1_General_100_BIN2 NULL,
 CashCapturedAtUtc datetimeoffset(7) NULL,
 CashCutoffExclusiveLocal date NULL
);
INSERT bms_execution343.Component(Id,OwnerMarker,ComponentVersion,Enabled,Revision)
VALUES(1,'2ee40663-2e51-48eb-b138-aa295e9ee343',1,0,0);

CREATE TABLE bms_execution343.SourceValues
(
 Kind varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT CK_Execution343_Kind CHECK(Kind IN('Limit','Reserve','Paid')),
 SourceId uniqueidentifier NOT NULL,
 CostCenterId uniqueidentifier NOT NULL,
 CashFlowItemId uniqueidentifier NOT NULL,
 BudgetMonth date NOT NULL CONSTRAINT CK_Execution343_SourceMonth CHECK(DAY(BudgetMonth)=1),
 AmountKzt decimal(19,2) NOT NULL CONSTRAINT CK_Execution343_SourceAmount CHECK(AmountKzt>=0),
 SourceVersion nvarchar(128) NOT NULL,
 Evidence nvarchar(4000) NOT NULL,
 LastRevision bigint NOT NULL,
 CONSTRAINT PK_Execution343_Source PRIMARY KEY(Kind,SourceId,CostCenterId,CashFlowItemId,BudgetMonth),
 CONSTRAINT FK_Execution343_SourceCfo FOREIGN KEY(CostCenterId) REFERENCES bms_web.CostCenters(Id),
 CONSTRAINT FK_Execution343_SourceArticle FOREIGN KEY(CashFlowItemId) REFERENCES bms_web.CashFlowItems(Id),
 CONSTRAINT CK_Execution343_SourceIdentity CHECK((Kind='Reserve' AND SourceId<>'00000000-0000-0000-0000-000000000000') OR Kind='Paid' OR (Kind='Limit' AND SourceId='00000000-0000-0000-0000-000000000000'))
);
CREATE TABLE bms_execution343.Movements
(
 Sequence bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Execution343_Movements PRIMARY KEY,
 BatchRevision bigint NOT NULL,
 PostedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT CK_Execution343_PostedUtc CHECK(DATEPART(TZOFFSET,PostedAtUtc)=0),
 CostCenterId uniqueidentifier NOT NULL,
 CashFlowItemId uniqueidentifier NOT NULL,
 BudgetMonth date NOT NULL CONSTRAINT CK_Execution343_MovementMonth CHECK(DAY(BudgetMonth)=1),
 RequestId uniqueidentifier NULL,
 Kind varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT CK_Execution343_MovementKind CHECK(Kind IN('Limit','Reserve','Paid')),
 MovementName nvarchar(120) NOT NULL,
 SourceVersion nvarchar(128) NOT NULL,
 Evidence nvarchar(4000) NOT NULL,
 LimitDeltaKzt decimal(19,2) NOT NULL,
 ReservedDeltaKzt decimal(19,2) NOT NULL,
 PaidDeltaKzt decimal(19,2) NOT NULL,
 AmountKzt AS CONVERT(decimal(19,2),LimitDeltaKzt-ReservedDeltaKzt-PaidDeltaKzt) PERSISTED,
 CorrelationId uniqueidentifier NOT NULL,
 CONSTRAINT FK_Execution343_MovementCfo FOREIGN KEY(CostCenterId) REFERENCES bms_web.CostCenters(Id),
 CONSTRAINT FK_Execution343_MovementArticle FOREIGN KEY(CashFlowItemId) REFERENCES bms_web.CashFlowItems(Id),
 CONSTRAINT CK_Execution343_NonzeroMovement CHECK((Kind='Limit' AND LimitDeltaKzt<>0 AND ReservedDeltaKzt=0 AND PaidDeltaKzt=0 AND RequestId IS NULL)
  OR (Kind='Reserve' AND ReservedDeltaKzt<>0 AND LimitDeltaKzt=0 AND PaidDeltaKzt=0 AND RequestId IS NOT NULL)
  OR (Kind='Paid' AND PaidDeltaKzt<>0 AND LimitDeltaKzt=0 AND ReservedDeltaKzt=0))
);
CREATE INDEX IX_Execution343_MonthKeySequence ON bms_execution343.Movements(BudgetMonth,CostCenterId,CashFlowItemId,Sequence)
 INCLUDE(AmountKzt,LimitDeltaKzt,ReservedDeltaKzt,PaidDeltaKzt,Kind,RequestId);
CREATE INDEX IX_Execution343_RequestSequence ON bms_execution343.Movements(RequestId,Sequence) WHERE RequestId IS NOT NULL;

CREATE TABLE bms_execution343.FrozenDecisions
(
 RequestId uniqueidentifier NOT NULL,
 FinancialFingerprint char(64) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT CK_Execution343_Fingerprint CHECK(FinancialFingerprint NOT LIKE '%[^0-9A-F]%'),
 ApprovedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT CK_Execution343_ApprovedUtc CHECK(DATEPART(TZOFFSET,ApprovedAtUtc)=0),
 PositiveDeficitKzt decimal(19,2) NOT NULL CONSTRAINT CK_Execution343_Deficit CHECK(PositiveDeficitKzt>=0),
 ApprovalSnapshotNumber int NULL,
 PositionsJson nvarchar(max) NOT NULL CONSTRAINT CK_Execution343_PositionsJson CHECK(ISJSON(PositionsJson)=1 AND DATALENGTH(PositionsJson)<=1000000),
 Evidence nvarchar(4000) NOT NULL,
 PostedAtUtc datetimeoffset(7) NOT NULL,
 CONSTRAINT PK_Execution343_Frozen PRIMARY KEY(RequestId,FinancialFingerprint),
 CONSTRAINT FK_Execution343_FrozenRequest FOREIGN KEY(RequestId) REFERENCES bms_web.ExpenseRequests(Id)
);
CREATE TABLE bms_execution343.CashCells
(
 CostCenterId uniqueidentifier NOT NULL,
 CashFlowItemId uniqueidentifier NOT NULL,
 BudgetMonth date NOT NULL CONSTRAINT CK_Execution343_CashMonth CHECK(DAY(BudgetMonth)=1),
 SourceCashKzt decimal(19,2) NOT NULL CONSTRAINT CK_Execution343_CashPositive CHECK(SourceCashKzt>=0),
 RepresentedBmsPaidKzt decimal(19,2) NOT NULL CONSTRAINT CK_Execution343_RepresentedPositive CHECK(RepresentedBmsPaidKzt>=0),
 SnapshotId uniqueidentifier NOT NULL,
 CONSTRAINT PK_Execution343_Cash PRIMARY KEY(CostCenterId,CashFlowItemId,BudgetMonth)
);
-- Technical current source proof, not the financial ledger. Any changed value
-- is posted as an append-only paid delta by Post in the same business commit.
CREATE TABLE bms_execution343.CashTransfers
(
 LinkId uniqueidentifier NOT NULL,
 RequestId uniqueidentifier NOT NULL,
 CostCenterId uniqueidentifier NOT NULL,
 CashFlowItemId uniqueidentifier NOT NULL,
 BudgetMonth date NOT NULL CHECK(DAY(BudgetMonth)=1),
 AmountKzt decimal(19,2) NOT NULL CHECK(AmountKzt>=0),
 OriginalReservePaidKzt decimal(19,2) NOT NULL CHECK(OriginalReservePaidKzt>0),
 SnapshotId uniqueidentifier NOT NULL,
 Evidence nvarchar(4000) NOT NULL,
 CONSTRAINT PK_Execution343_CashTransfer PRIMARY KEY(LinkId,CostCenterId,CashFlowItemId,BudgetMonth),
 CONSTRAINT FK_Execution343_TransferRequest FOREIGN KEY(RequestId) REFERENCES bms_web.ExpenseRequests(Id)
);
COMMIT;
GO
CREATE TRIGGER bms_execution343.ProtectMovements ON bms_execution343.Movements INSTEAD OF UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;THROW 51000,'Budget execution movements are append-only; post a reversal',1;END;
GO
CREATE TRIGGER bms_execution343.ProtectFrozenDecisions ON bms_execution343.FrozenDecisions INSTEAD OF UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;THROW 51000,'Approved budget decisions are immutable; use a new financial fingerprint',1;END;
GO
CREATE VIEW bms_execution343.Balances AS
 SELECT CostCenterId,CashFlowItemId,BudgetMonth,SUM(LimitDeltaKzt) LimitAmountKzt,SUM(ReservedDeltaKzt) ReservedAmountKzt,
 SUM(PaidDeltaKzt) PaidAmountKzt,SUM(AmountKzt) AvailableAmountKzt
 FROM bms_execution343.Movements GROUP BY CostCenterId,CashFlowItemId,BudgetMonth;
GO
CREATE PROCEDURE bms_execution343.Post
 @values nvarchar(max),@frozen nvarchar(max),@correlation uniqueidentifier
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;
 IF @@TRANCOUNT<>1 OR ISJSON(@values)<>1 OR ISJSON(@frozen)<>1 OR DATALENGTH(@values)>20000000 OR DATALENGTH(@frozen)>2000000
  THROW 51000,'Exact bounded register transaction required',1;
 DECLARE @lock int;
 EXEC @lock=sys.sp_getapplock @Resource=N'BMS:BudgetExecution343',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
 IF @lock<0 THROW 51000,'Budget register lock unavailable',1;
 DECLARE @revision bigint,@now datetimeoffset(7)=TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00');
 SELECT @revision=Revision+1 FROM bms_execution343.Component WITH(UPDLOCK,HOLDLOCK) WHERE Id=1 AND OwnerMarker='2ee40663-2e51-48eb-b138-aa295e9ee343' AND ComponentVersion=1;
 IF @revision IS NULL THROW 51000,'Register identity unavailable',1;
 IF EXISTS(SELECT 1 FROM OPENJSON(@values) WITH(amount nvarchar(100) '$.amountKzt') x
  WHERE TRY_CONVERT(decimal(38,6),x.amount) IS NULL OR TRY_CONVERT(decimal(38,6),x.amount)<>TRY_CONVERT(decimal(19,2),x.amount))
  THROW 51000,'Register amounts must have exact tiyn precision',1;
 IF EXISTS(SELECT 1 FROM OPENJSON(@frozen) WITH(amount nvarchar(100) '$.positiveDeficitKzt') x
  WHERE TRY_CONVERT(decimal(38,6),x.amount) IS NULL OR TRY_CONVERT(decimal(38,6),x.amount)<>TRY_CONVERT(decimal(19,2),x.amount))
  THROW 51000,'Approved deficits must have exact tiyn precision',1;
 DECLARE @input TABLE(Kind varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,SourceId uniqueidentifier NOT NULL,
  CostCenterId uniqueidentifier NOT NULL,CashFlowItemId uniqueidentifier NOT NULL,BudgetMonth date NOT NULL,
  AmountKzt decimal(19,2) NOT NULL,MovementName nvarchar(120) NOT NULL,SourceVersion nvarchar(128) NOT NULL,Evidence nvarchar(4000) NOT NULL,
  PRIMARY KEY(Kind,SourceId,CostCenterId,CashFlowItemId,BudgetMonth));
 INSERT @input SELECT kind,sourceId,costCenterId,cashFlowItemId,budgetMonth,amountKzt,movementName,sourceVersion,evidence
 FROM OPENJSON(@values) WITH(kind varchar(16),sourceId uniqueidentifier,costCenterId uniqueidentifier,cashFlowItemId uniqueidentifier,
  budgetMonth date,amountKzt decimal(19,2),movementName nvarchar(120),sourceVersion nvarchar(128),evidence nvarchar(4000));
 IF (SELECT COUNT_BIG(*) FROM @input)>100000 OR EXISTS(SELECT 1 FROM @input WHERE Kind NOT IN('Limit','Reserve','Paid') OR DAY(BudgetMonth)<>1 OR AmountKzt<0
  OR (Kind='Reserve' AND SourceId='00000000-0000-0000-0000-000000000000') OR (Kind='Limit' AND SourceId<>'00000000-0000-0000-0000-000000000000'))
  THROW 51000,'Invalid register source values',1;
 -- Full capture: absent old reserve/limit cells are explicitly released,
 -- not deleted. SourceValues is only a technical posting cache.
 INSERT @input SELECT s.Kind,s.SourceId,s.CostCenterId,s.CashFlowItemId,s.BudgetMonth,0,N'Сторно ранее учтённого движения',s.SourceVersion,s.Evidence
 FROM bms_execution343.SourceValues s WHERE s.AmountKzt<>0 AND NOT EXISTS(SELECT 1 FROM @input i WHERE i.Kind=s.Kind AND i.SourceId=s.SourceId
  AND i.CostCenterId=s.CostCenterId AND i.CashFlowItemId=s.CashFlowItemId AND i.BudgetMonth=s.BudgetMonth);
 INSERT bms_execution343.Movements(BatchRevision,PostedAtUtc,CostCenterId,CashFlowItemId,BudgetMonth,RequestId,Kind,MovementName,SourceVersion,Evidence,
  LimitDeltaKzt,ReservedDeltaKzt,PaidDeltaKzt,CorrelationId)
 SELECT @revision,@now,i.CostCenterId,i.CashFlowItemId,i.BudgetMonth,NULLIF(i.SourceId,'00000000-0000-0000-0000-000000000000'),i.Kind,i.MovementName,i.SourceVersion,i.Evidence,
  CASE WHEN i.Kind='Limit' THEN i.AmountKzt-COALESCE(s.AmountKzt,0) ELSE 0 END,
  CASE WHEN i.Kind='Reserve' THEN i.AmountKzt-COALESCE(s.AmountKzt,0) ELSE 0 END,
  CASE WHEN i.Kind='Paid' THEN i.AmountKzt-COALESCE(s.AmountKzt,0) ELSE 0 END,@correlation
 FROM @input i LEFT JOIN bms_execution343.SourceValues s ON s.Kind=i.Kind AND s.SourceId=i.SourceId AND s.CostCenterId=i.CostCenterId AND s.CashFlowItemId=i.CashFlowItemId AND s.BudgetMonth=i.BudgetMonth
 WHERE i.AmountKzt<>COALESCE(s.AmountKzt,0);
 DECLARE @posted int=@@ROWCOUNT;
 UPDATE s SET AmountKzt=i.AmountKzt,SourceVersion=i.SourceVersion,Evidence=i.Evidence,LastRevision=@revision
 FROM bms_execution343.SourceValues s JOIN @input i ON s.Kind=i.Kind AND s.SourceId=i.SourceId AND s.CostCenterId=i.CostCenterId AND s.CashFlowItemId=i.CashFlowItemId AND s.BudgetMonth=i.BudgetMonth
 WHERE s.AmountKzt<>i.AmountKzt OR s.SourceVersion<>i.SourceVersion OR s.Evidence<>i.Evidence;
 INSERT bms_execution343.SourceValues(Kind,SourceId,CostCenterId,CashFlowItemId,BudgetMonth,AmountKzt,SourceVersion,Evidence,LastRevision)
 SELECT i.Kind,i.SourceId,i.CostCenterId,i.CashFlowItemId,i.BudgetMonth,i.AmountKzt,i.SourceVersion,i.Evidence,@revision FROM @input i
 WHERE NOT EXISTS(SELECT 1 FROM bms_execution343.SourceValues s WHERE s.Kind=i.Kind AND s.SourceId=i.SourceId AND s.CostCenterId=i.CostCenterId AND s.CashFlowItemId=i.CashFlowItemId AND s.BudgetMonth=i.BudgetMonth);
 DECLARE @decisions TABLE(RequestId uniqueidentifier NOT NULL,FinancialFingerprint char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  ApprovedAtUtc datetimeoffset(7) NOT NULL,PositiveDeficitKzt decimal(19,2) NOT NULL,ApprovalSnapshotNumber int NULL,Evidence nvarchar(4000) NOT NULL,PositionsJson nvarchar(max) NOT NULL,
  PRIMARY KEY(RequestId,FinancialFingerprint));
 INSERT @decisions SELECT requestId,financialFingerprint,approvedAtUtc,positiveDeficitKzt,approvalSnapshotNumber,evidence,positionsJson FROM OPENJSON(@frozen)
 WITH(requestId uniqueidentifier,financialFingerprint char(64),approvedAtUtc datetimeoffset(7),positiveDeficitKzt decimal(19,2),approvalSnapshotNumber int,evidence nvarchar(4000),positionsJson nvarchar(max));
 IF EXISTS(SELECT 1 FROM @decisions i JOIN bms_execution343.FrozenDecisions d ON d.RequestId=i.RequestId AND d.FinancialFingerprint=i.FinancialFingerprint
  WHERE d.PositiveDeficitKzt<>i.PositiveDeficitKzt OR d.ApprovedAtUtc<>i.ApprovedAtUtc OR d.PositionsJson<>i.PositionsJson)
  THROW 51000,'Attempt to change an approved decision',1;
 INSERT bms_execution343.FrozenDecisions(RequestId,FinancialFingerprint,ApprovedAtUtc,PositiveDeficitKzt,ApprovalSnapshotNumber,Evidence,PositionsJson,PostedAtUtc)
 SELECT i.RequestId,i.FinancialFingerprint,i.ApprovedAtUtc,i.PositiveDeficitKzt,i.ApprovalSnapshotNumber,i.Evidence,i.PositionsJson,@now FROM @decisions i
 WHERE NOT EXISTS(SELECT 1 FROM bms_execution343.FrozenDecisions d WHERE d.RequestId=i.RequestId AND d.FinancialFingerprint=i.FinancialFingerprint);
 DECLARE @frozenPosted int=@@ROWCOUNT;
 IF EXISTS(SELECT 1 FROM bms_execution343.Balances WHERE LimitAmountKzt<0 OR ReservedAmountKzt<0 OR PaidAmountKzt<0)
  THROW 51000,'Register source totals cannot be negative',1;
 IF @posted+@frozenPosted>0 UPDATE bms_execution343.Component SET Revision=@revision,LastPostedAtUtc=@now WHERE Id=1;
 SELECT @posted postedMovements,@frozenPosted postedDecisions;
END;
GO
CREATE PROCEDURE bms_execution343.AcceptCash
 @snapshotId uniqueidentifier,@grossSha256 char(64),@capturedAtUtc datetimeoffset(7),@cutoffExclusiveLocal date,@cells nvarchar(max),@transfers nvarchar(max)=N'[]'
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;
 IF @@TRANCOUNT<>1 OR @snapshotId IS NULL OR @grossSha256 COLLATE Latin1_General_100_BIN2 LIKE '%[^0-9A-F]%'
  OR DATEPART(TZOFFSET,@capturedAtUtc)<>0 OR @cutoffExclusiveLocal IS NULL OR ISJSON(@cells)<>1 OR DATALENGTH(@cells)>20000000
  OR ISJSON(@transfers)<>1 OR DATALENGTH(@transfers)>20000000
  THROW 51000,'Exact verified cash capture required',1;
 DECLARE @lock int;
 EXEC @lock=sys.sp_getapplock @Resource=N'BMS:BudgetExecution343',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
 IF @lock<0 THROW 51000,'Budget register cash lock unavailable',1;
 IF EXISTS(SELECT 1 FROM bms_execution343.Component WITH(UPDLOCK,HOLDLOCK) WHERE Id=1 AND
  (CashCapturedAtUtc>@capturedAtUtc OR (CashSnapshotId=@snapshotId AND CashGrossSHA256<>@grossSha256)))
  THROW 51000,'Cash snapshot rollback or identity drift refused',1;
 IF EXISTS(SELECT 1 FROM OPENJSON(@cells) WITH(cash nvarchar(100) '$.sourceCashKzt',paid nvarchar(100) '$.representedBmsPaidKzt') x
  WHERE TRY_CONVERT(decimal(38,18),cash) IS NULL OR TRY_CONVERT(decimal(38,18),paid) IS NULL
   OR TRY_CONVERT(decimal(38,18),cash)<>TRY_CONVERT(decimal(19,2),cash) OR TRY_CONVERT(decimal(38,18),paid)<>TRY_CONVERT(decimal(19,2),paid))
  THROW 51000,'Cash capture requires exact tiyn precision',1;
 DECLARE @input TABLE(CostCenterId uniqueidentifier NOT NULL,CashFlowItemId uniqueidentifier NOT NULL,BudgetMonth date NOT NULL,
  SourceCashKzt decimal(19,2) NOT NULL,RepresentedBmsPaidKzt decimal(19,2) NOT NULL,SnapshotId uniqueidentifier NOT NULL,
  PRIMARY KEY(CostCenterId,CashFlowItemId,BudgetMonth));
 INSERT @input SELECT costCenterId,cashFlowItemId,budgetMonth,sourceCashKzt,representedBmsPaidKzt,snapshotId FROM OPENJSON(@cells)
 WITH(costCenterId uniqueidentifier,cashFlowItemId uniqueidentifier,budgetMonth date,sourceCashKzt decimal(19,2),representedBmsPaidKzt decimal(19,2),snapshotId uniqueidentifier);
 IF (SELECT COUNT(*) FROM @input)>100000 OR EXISTS(SELECT 1 FROM @input WHERE DAY(BudgetMonth)<>1 OR SourceCashKzt<0 OR RepresentedBmsPaidKzt<0 OR SnapshotId<>@snapshotId)
  THROW 51000,'Cash capture shape invalid',1;
 DECLARE @from date=DATEFROMPARTS(YEAR(DATEADD(day,-1,@cutoffExclusiveLocal)),1,1),@through date=DATEFROMPARTS(YEAR(DATEADD(day,-1,@cutoffExclusiveLocal)),MONTH(DATEADD(day,-1,@cutoffExclusiveLocal)),1);
 IF EXISTS(SELECT 1 FROM @input WHERE BudgetMonth<@from OR BudgetMonth>@through)
  THROW 51000,'Cash capture cannot silently rewrite other calendar years or future months',1;
 DECLARE @target TABLE(LinkId uniqueidentifier NOT NULL,RequestId uniqueidentifier NOT NULL,CostCenterId uniqueidentifier NOT NULL,
  CashFlowItemId uniqueidentifier NOT NULL,BudgetMonth date NOT NULL,AmountKzt decimal(19,2) NOT NULL,OriginalReservePaidKzt decimal(19,2) NOT NULL,
  SnapshotId uniqueidentifier NOT NULL,Evidence nvarchar(4000) NOT NULL,PRIMARY KEY(LinkId,CostCenterId,CashFlowItemId,BudgetMonth));
 IF EXISTS(SELECT 1 FROM OPENJSON(@transfers) WITH(amount nvarchar(100) '$.amountKzt',original nvarchar(100) '$.originalReservePaidKzt') x
  WHERE TRY_CONVERT(decimal(38,18),amount) IS NULL OR TRY_CONVERT(decimal(38,18),original) IS NULL
   OR TRY_CONVERT(decimal(38,18),amount)<>TRY_CONVERT(decimal(19,2),amount) OR TRY_CONVERT(decimal(38,18),original)<>TRY_CONVERT(decimal(19,2),original))
  THROW 51000,'Cash transfers require exact tiyn precision',1;
 INSERT @target SELECT linkId,requestId,costCenterId,cashFlowItemId,budgetMonth,amountKzt,originalReservePaidKzt,snapshotId,evidence
 FROM OPENJSON(@transfers) WITH(linkId uniqueidentifier,requestId uniqueidentifier,costCenterId uniqueidentifier,cashFlowItemId uniqueidentifier,
  budgetMonth date,amountKzt decimal(19,2),originalReservePaidKzt decimal(19,2),snapshotId uniqueidentifier,evidence nvarchar(4000));
 IF (SELECT COUNT(*) FROM @target)>100000 OR EXISTS(SELECT 1 FROM @target WHERE DAY(BudgetMonth)<>1 OR AmountKzt<0 OR OriginalReservePaidKzt<=0 OR SnapshotId<>@snapshotId)
  OR EXISTS(SELECT 1 FROM @input WHERE RepresentedBmsPaidKzt>SourceCashKzt)
  OR COALESCE((SELECT SUM(AmountKzt) FROM @target),0)<>COALESCE((SELECT SUM(RepresentedBmsPaidKzt) FROM @input),0)
  THROW 51000,'Cash reattribution must conserve proven bank KZT',1;
 -- All active linked documents in the accepted capture are supplied. Prior
 -- calendar captures remain untouched; current-capture transfers are replaced
 -- with zero rather than deleted, so corrections/reversals post explicit deltas.
 UPDATE t SET AmountKzt=COALESCE(i.AmountKzt,0),OriginalReservePaidKzt=COALESCE(i.OriginalReservePaidKzt,t.OriginalReservePaidKzt),
  SnapshotId=@snapshotId,Evidence=COALESCE(i.Evidence,t.Evidence)
 FROM bms_execution343.CashTransfers t LEFT JOIN @target i ON i.LinkId=t.LinkId AND i.CostCenterId=t.CostCenterId
  AND i.CashFlowItemId=t.CashFlowItemId AND i.BudgetMonth=t.BudgetMonth
 WHERE EXISTS(SELECT 1 FROM bms_web.AccountingPaymentLinks l WHERE l.Id=t.LinkId AND YEAR(l.PaymentDay)=YEAR(DATEADD(day,-1,@cutoffExclusiveLocal)));
 INSERT bms_execution343.CashTransfers SELECT i.* FROM @target i WHERE NOT EXISTS(SELECT 1 FROM bms_execution343.CashTransfers t
  WHERE i.LinkId=t.LinkId AND i.CostCenterId=t.CostCenterId AND i.CashFlowItemId=t.CashFlowItemId AND i.BudgetMonth=t.BudgetMonth);
 UPDATE c SET SourceCashKzt=COALESCE(i.SourceCashKzt,0),RepresentedBmsPaidKzt=COALESCE(i.RepresentedBmsPaidKzt,0),SnapshotId=@snapshotId
 FROM bms_execution343.CashCells c LEFT JOIN @input i ON i.CostCenterId=c.CostCenterId AND i.CashFlowItemId=c.CashFlowItemId AND i.BudgetMonth=c.BudgetMonth
 WHERE c.BudgetMonth>=@from AND c.BudgetMonth<=@through;
 INSERT bms_execution343.CashCells(CostCenterId,CashFlowItemId,BudgetMonth,SourceCashKzt,RepresentedBmsPaidKzt,SnapshotId)
 SELECT i.CostCenterId,i.CashFlowItemId,i.BudgetMonth,i.SourceCashKzt,i.RepresentedBmsPaidKzt,@snapshotId FROM @input i
 WHERE NOT EXISTS(SELECT 1 FROM bms_execution343.CashCells c WHERE c.CostCenterId=i.CostCenterId AND c.CashFlowItemId=i.CashFlowItemId AND c.BudgetMonth=i.BudgetMonth);
 UPDATE bms_execution343.Component SET CashSnapshotId=@snapshotId,CashGrossSHA256=@grossSha256,CashCapturedAtUtc=@capturedAtUtc,CashCutoffExclusiveLocal=@cutoffExclusiveLocal WHERE Id=1;
END;
