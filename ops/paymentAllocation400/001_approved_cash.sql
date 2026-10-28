-- Additive adapter. Existing core61 tables, constraints and AcceptCash stay intact.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF CONVERT(nvarchar(128),SERVERPROPERTY('MachineName'))<>N'BMS'
 OR CONVERT(nvarchar(40),CONNECTIONPROPERTY('net_transport'))<>N'Shared memory'
 OR NOT ((DB_NAME()=N'DataBI' AND (SELECT MAX(version_no) FROM bms_web.schema_version)=61)
  OR (DB_NAME()=N'BmsPaymentAllocation400_Test' AND EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0
   AND name=N'BmsPayment400Owner' AND CONVERT(nvarchar(128),value)=N'BMS_PAYMENT_ALLOCATION_400_ISOLATED')))
 THROW 51000,'Exact local approved allocation installation database required',1;
IF OBJECT_ID(N'bms_execution343.AcceptApprovedCash400') IS NOT NULL
 THROW 51000,'Approved cash adapter already exists; verify exact bytes instead of overwriting',1;
GO
CREATE PROCEDURE bms_execution343.AcceptApprovedCash400
 @snapshotId uniqueidentifier,@grossSha256 char(64),@capturedAtUtc datetimeoffset(7),
 @cutoffExclusiveLocal date,@cells nvarchar(max),@transfers nvarchar(max),@controlledTransferIds nvarchar(max)
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;
 IF @@TRANCOUNT=0 OR (SELECT transaction_isolation_level FROM sys.dm_exec_sessions WHERE session_id=@@SPID)<>4
  OR ISJSON(@controlledTransferIds)<>1 OR DATALENGTH(@controlledTransferIds)>400000
  THROW 51000,'Serializable explicit allocation transaction required',1;
 DECLARE @ids TABLE(Id uniqueidentifier NOT NULL PRIMARY KEY);
 INSERT @ids SELECT [value] FROM OPENJSON(@controlledTransferIds) WITH([value] uniqueidentifier '$');
 IF (SELECT COUNT(*) FROM @ids) NOT BETWEEN 1 AND 2000
  OR EXISTS(SELECT 1 FROM @ids i JOIN bms_web.AccountingPaymentLinks l ON l.Id=i.Id)
  THROW 51000,'Bounded separate allocation identities required',1;
 DECLARE @known TABLE(Id uniqueidentifier NOT NULL PRIMARY KEY,RequestId uniqueidentifier NOT NULL);
 INSERT @known
 SELECT a.eventId,a.expenseRequestId
 FROM bms_web.BusinessAudit b
 CROSS APPLY OPENJSON(b.AfterJson) WITH(paymentDay date,allocations nvarchar(max) AS JSON) g
 CROSS APPLY OPENJSON(g.allocations) WITH(eventId uniqueidentifier,expenseRequestId uniqueidentifier) a
 WHERE b.ObjectType=N'AccountingAllocation400' AND b.Action=N'AccountingAllocationApproved'
  AND YEAR(g.paymentDay)=YEAR(DATEADD(day,-1,@cutoffExclusiveLocal));
 IF EXISTS(SELECT 1 FROM @ids i LEFT JOIN @known k ON k.Id=i.Id WHERE k.Id IS NULL)
  THROW 51000,'Immutable explicit allocation evidence required',1;
 DECLARE @target TABLE(LinkId uniqueidentifier NOT NULL,RequestId uniqueidentifier NOT NULL,
  CostCenterId uniqueidentifier NOT NULL,CashFlowItemId uniqueidentifier NOT NULL,BudgetMonth date NOT NULL,
  AmountKzt decimal(19,2) NOT NULL,OriginalReservePaidKzt decimal(19,2) NOT NULL,SnapshotId uniqueidentifier NOT NULL,
  Evidence nvarchar(4000) NOT NULL,PRIMARY KEY(LinkId,CostCenterId,CashFlowItemId,BudgetMonth));
 INSERT @target SELECT linkId,requestId,costCenterId,cashFlowItemId,budgetMonth,amountKzt,originalReservePaidKzt,snapshotId,evidence
 FROM OPENJSON(@transfers) WITH(linkId uniqueidentifier,requestId uniqueidentifier,costCenterId uniqueidentifier,
  cashFlowItemId uniqueidentifier,budgetMonth date,amountKzt decimal(19,2),originalReservePaidKzt decimal(19,2),
  snapshotId uniqueidentifier,evidence nvarchar(4000));
 IF EXISTS(SELECT 1 FROM @ids i WHERE NOT EXISTS(SELECT 1 FROM @target t WHERE t.LinkId=i.Id))
  OR EXISTS(SELECT 1 FROM @target t JOIN @ids i ON i.Id=t.LinkId JOIN @known k ON k.Id=i.Id WHERE k.RequestId<>t.RequestId)
  THROW 51000,'Every controlled allocation, including reversals, must be supplied explicitly',1;
 -- Base adapter retains all money precision, conservation, date and capture locks.
 EXEC bms_execution343.AcceptCash @snapshotId=@snapshotId,@grossSha256=@grossSha256,@capturedAtUtc=@capturedAtUtc,
  @cutoffExclusiveLocal=@cutoffExclusiveLocal,@cells=@cells,@transfers=@transfers;
 -- This is current technical source proof, not a financial ledger rewrite.
 -- Post subsequently records the resulting paid deltas as append-only movements.
 UPDATE stored SET AmountKzt=t.AmountKzt,OriginalReservePaidKzt=t.OriginalReservePaidKzt,
  SnapshotId=t.SnapshotId,Evidence=t.Evidence
 FROM bms_execution343.CashTransfers stored JOIN @target t ON t.LinkId=stored.LinkId
  AND t.CostCenterId=stored.CostCenterId AND t.CashFlowItemId=stored.CashFlowItemId AND t.BudgetMonth=stored.BudgetMonth
 JOIN @ids controlled ON controlled.Id=t.LinkId;
END;
GO
-- No new principal or public grant. Retain the existing restricted service
-- execution boundary; deployment verifies permission using the service identity.
