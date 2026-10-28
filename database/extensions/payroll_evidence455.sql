-- Read-only evidence gateway. No credentials, source writes or payment creation.
SET XACT_ABORT ON;
IF CONVERT(nvarchar(128),SERVERPROPERTY('MachineName'))<>N'BMS'
 OR DB_NAME() NOT IN(N'BmsDdsReplica2026',N'BmsPayroll455_Test') THROW 65501,N'Exact payroll evidence database required.',1;
GO
CREATE OR ALTER PROCEDURE dbo.ReadPayrollCashEvidence455 @CaptureId uniqueidentifier
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @active uniqueidentifier=(SELECT BatchId FROM dbo.ActivePaymentCapture),
   @snapshot uniqueidentifier=(SELECT SnapshotId FROM dbo.ActiveSnapshot);
 IF @CaptureId IS NULL OR @active<>@CaptureId OR @active IS NULL OR @snapshot IS NULL
  OR NOT EXISTS(SELECT 1 FROM dbo.PaymentCaptureBatches WHERE Id=@active AND IsSealed=1)
  OR NOT EXISTS(SELECT 1 FROM dbo.Snapshots WHERE SnapshotId=@snapshot AND IsSealed=1)
  THROW 65502,N'Current sealed 1C captures required.',1;
 SELECT @active CaptureId,@snapshot DdsSnapshotId;
 -- Materialize at most one bounded scan of the accepted raw capture.
 -- Subsequent leg joins seek its primary key rather than concatenating
 -- every raw key for every accounting document.
 SELECT DISTINCT DatabaseName,RecorderType,RecorderRef
 INTO #PayrollKeys FROM dbo.RawCashLegs
 WHERE SnapshotId=@snapshot AND Direction='Ct' AND DtAccountCode='3350';
 CREATE UNIQUE CLUSTERED INDEX IX_PayrollKeys ON #PayrollKeys(DatabaseName,RecorderType,RecorderRef);
 -- Return every payment leg of a document that has any payroll debit.
 -- A mixed document must not become eligible by filtering its other legs out.
 SELECT TOP(200001) d.DocumentKey,d.FinancialHash,l.[LineNo],l.EntryDateLocal,l.AmountKzt,
   r.DtAccountCode,r.CtAccountCode,r.SourceAmountKzt,r.EntryDateLocalText
 FROM #PayrollKeys k
 JOIN dbo.PaymentCaptureDocuments d ON d.BatchId=@active
  AND d.DocumentKey=CONCAT(k.DatabaseName,N'|',k.RecorderType,N'|',k.RecorderRef) COLLATE Latin1_General_100_BIN2
 JOIN dbo.PaymentCaptureLegs l ON l.BatchId=d.BatchId AND l.DocumentKey=d.DocumentKey
 LEFT JOIN dbo.RawCashLegs r ON r.SnapshotId=@snapshot AND r.Direction='Ct'
  AND r.DatabaseName=k.DatabaseName AND r.RecorderType=k.RecorderType AND r.RecorderRef=k.RecorderRef AND r.[LineNo]=l.[LineNo]
 WHERE d.BatchId=@active AND d.Eligible=1 AND d.CurrencyNumericCode='398' AND d.Amount>0 AND d.Amount=d.AmountKzt
 ORDER BY d.DocumentKey,l.[LineNo];
END
GO
IF DB_NAME()=N'BmsDdsReplica2026'
BEGIN
 IF DATABASE_PRINCIPAL_ID(N'NT SERVICE\BmsWebCurrent') IS NULL THROW 65501,N'Existing runtime principal required.',1;
 GRANT EXECUTE ON OBJECT::dbo.ReadPayrollCashEvidence455 TO [NT SERVICE\BmsWebCurrent];
 GRANT VIEW DEFINITION ON OBJECT::dbo.ReadPayrollCashEvidence455 TO [NT SERVICE\BmsWebCurrent];
END;
