-- Data-only, reversible renumbering. The host must stop BmsWebCurrent first.
-- @Forward bit; @CostCenterId uniqueidentifier = NULL (only local test scoping).
-- No ordinal, financial state, timestamp, concurrency token or audit is changed.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @@TRANCOUNT <> 0 THROW 51242, 'Number migration requires its own transaction.', 1;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lock int;
 EXEC @lock=sys.sp_getapplock @Resource=N'BMS:ExpenseRequest:NumberReservations',
  @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
 IF @lock<0 THROW 51242, 'Number reservation lock unavailable.', 1;
 -- Lock documents and leases before deriving a bounded one-to-one mapping.
 SELECT r.Id,r.RequestNumber,c.Code,r.DefaultCostCenterId
 INTO #requests FROM bms_web.ExpenseRequests r WITH(TABLOCKX,HOLDLOCK)
 LEFT JOIN bms_web.CostCenters c ON c.Id=r.DefaultCostCenterId
 WHERE r.RequestNumber IS NOT NULL AND (@CostCenterId IS NULL OR r.DefaultCostCenterId=@CostCenterId);
 SELECT l.CostCenterId,l.Ordinal,l.Number,c.Code
 INTO #leases FROM bms_web.ExpenseRequestNumberLeases l WITH(TABLOCKX,HOLDLOCK)
 LEFT JOIN bms_web.CostCenters c ON c.Id=l.CostCenterId
 WHERE @CostCenterId IS NULL OR l.CostCenterId=@CostCenterId;
 IF EXISTS(SELECT 1 FROM bms_web.ExpenseRequestCostCenterCounters s WITH(UPDLOCK,HOLDLOCK)
  LEFT JOIN bms_web.CostCenters c ON c.Id=s.CostCenterId
  WHERE (@CostCenterId IS NULL OR s.CostCenterId=@CostCenterId)
   AND (c.Id IS NULL OR s.Prefix COLLATE Latin1_General_100_BIN2<>c.Code COLLATE Latin1_General_100_BIN2))
  THROW 51242, 'Counter prefix differs from CFO code.', 1;
 IF EXISTS(SELECT 1 FROM #requests WHERE Code IS NULL OR LEN(Code)=0)
  OR EXISTS(SELECT 1 FROM #leases WHERE Code IS NULL OR LEN(Code)=0)
  THROW 51242, 'Missing CFO for a numbered document.', 1;
 SELECT Id,RequestNumber AS OldNumber,
  CASE WHEN @Forward=1 THEN SUBSTRING(RequestNumber,4,160) ELSE N'РЗ-'+RequestNumber END AS NewNumber,
  CASE WHEN @Forward=1 THEN N'РЗ-'+Code+N'-' ELSE Code+N'-' END AS ExpectedPrefix,
  SUBSTRING(RequestNumber,LEN(Code)+CASE WHEN @Forward=1 THEN 5 ELSE 2 END,160) AS Suffix
 INTO #requestMap FROM #requests;
 IF EXISTS(SELECT 1 FROM #requestMap WHERE
  LEFT(OldNumber,LEN(ExpectedPrefix)) COLLATE Latin1_General_100_BIN2<>ExpectedPrefix COLLATE Latin1_General_100_BIN2
  OR LEN(Suffix)<5 OR Suffix COLLATE Latin1_General_100_BIN2 LIKE N'%[^0-9]%'
  OR TRY_CONVERT(bigint,Suffix) IS NULL OR TRY_CONVERT(bigint,Suffix)<=0
  OR Suffix<>FORMAT(TRY_CONVERT(bigint,Suffix),'D5','en-US'))
  THROW 51242, 'Unexpected request number; no partial migration permitted.', 1;
 SELECT CostCenterId,Ordinal,Number AS OldNumber,
  CASE WHEN @Forward=1 THEN SUBSTRING(Number,4,128) ELSE N'РЗ-'+Number END AS NewNumber,
  (CASE WHEN @Forward=1 THEN N'РЗ-' ELSE N'' END)+Code+N'-'+FORMAT(Ordinal,'D5','en-US') AS ExpectedNumber
 INTO #leaseMap FROM #leases;
 IF EXISTS(SELECT 1 FROM #leaseMap WHERE OldNumber COLLATE Latin1_General_100_BIN2<>ExpectedNumber COLLATE Latin1_General_100_BIN2)
  THROW 51242, 'Unexpected lease number; no partial migration permitted.', 1;
 IF EXISTS(SELECT NewNumber FROM #requestMap GROUP BY NewNumber HAVING COUNT(*)>1)
  OR EXISTS(SELECT NewNumber FROM #leaseMap GROUP BY NewNumber HAVING COUNT(*)>1)
  OR EXISTS(SELECT 1 FROM #requestMap m JOIN bms_web.ExpenseRequests r ON r.RequestNumber=m.NewNumber AND r.Id<>m.Id)
  OR EXISTS(SELECT 1 FROM #leaseMap m JOIN bms_web.ExpenseRequestNumberLeases l ON l.Number=m.NewNumber AND (l.CostCenterId<>m.CostCenterId OR l.Ordinal<>m.Ordinal))
  THROW 51242, 'Renumbering collision; no changes made.', 1;
 UPDATE r SET RequestNumber=m.NewNumber FROM bms_web.ExpenseRequests r JOIN #requestMap m ON m.Id=r.Id;
 UPDATE l SET Number=m.NewNumber FROM bms_web.ExpenseRequestNumberLeases l JOIN #leaseMap m ON m.CostCenterId=l.CostCenterId AND m.Ordinal=l.Ordinal;
 COMMIT TRANSACTION;
 SELECT Id,OldNumber,NewNumber FROM #requestMap ORDER BY OldNumber;
 SELECT CostCenterId,Ordinal,OldNumber,NewNumber FROM #leaseMap ORDER BY OldNumber;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
