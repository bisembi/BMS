CREATE PROCEDURE dbo.ReadDdsDrill313 @SnapshotId uniqueidentifier,@ArticleIds nvarchar(max),
 @FromMonth date,@ToMonth date,@Skip int=0,@Take int=50,
 @SortBy nvarchar(128)=NULL,@SortDirection nvarchar(16)=NULL
WITH EXECUTE AS OWNER
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;
 IF ISJSON(@ArticleIds)<>1 OR DATALENGTH(@ArticleIds)>160000 OR @Skip NOT BETWEEN 0 AND 1000000 OR @Take NOT IN(50,100)
  OR YEAR(@FromMonth)<>2026 OR YEAR(@ToMonth)<>2026 OR DAY(@FromMonth)<>1 OR DAY(@ToMonth)<>1 OR @FromMonth>@ToMonth
  THROW 52780,'Invalid detail query.',1;
 IF (@SortBy IS NULL AND @SortDirection IS NOT NULL)
  OR (@SortBy IS NOT NULL AND (@SortDirection IS NULL
   OR @SortDirection COLLATE Latin1_General_100_BIN2 NOT IN(N'asc',N'desc')
   OR DATALENGTH(@SortDirection)<>DATALENGTH(RTRIM(@SortDirection))
   OR @SortBy COLLATE Latin1_General_100_BIN2 NOT IN
    (N'entryDateLocal',N'company',N'documentType',N'document',N'counterparty',N'notes',N'comment',N'amountKzt',N'lineNo')
   OR DATALENGTH(@SortBy)<>DATALENGTH(RTRIM(@SortBy))))
  THROW 53130,'Invalid detail sort.',1;
 SELECT TRY_CONVERT(uniqueidentifier,value) Id INTO #ids FROM OPENJSON(@ArticleIds);
 IF (SELECT COUNT(*) FROM #ids) NOT BETWEEN 1 AND 2000 OR EXISTS(SELECT 1 FROM #ids WHERE Id IS NULL)
  OR EXISTS(SELECT 1 FROM #ids GROUP BY Id HAVING COUNT(*)<>1) THROW 52780,'Invalid article scope.',1;
 DECLARE @BatchId uniqueidentifier;
 SELECT @BatchId=b.Id FROM dbo.DdsDrillActive277 a JOIN dbo.DdsDrillBatches277 b ON b.Id=a.BatchId
  WHERE a.SnapshotId=@SnapshotId AND b.IsSealed=1;
 SELECT CONVERT(bit,CASE WHEN @BatchId IS NOT NULL THEN 1 ELSE 0 END) Available,
  (SELECT CapturedAtUtc FROM dbo.DdsDrillBatches277 WHERE Id=@BatchId) CapturedAtUtc;
 SELECT COUNT(*) Total,COALESCE(SUM(r.AmountKzt),0) TotalKzt FROM dbo.DdsDrillRows277 r JOIN #ids i ON i.Id=r.CashFlowItemId
  WHERE r.BatchId=@BatchId AND r.EntryDateLocal>=@FromMonth AND r.EntryDateLocal<DATEADD(month,1,@ToMonth);
 -- Append the source posting line so the original eight columns remain compatible.
 SELECT EntryDateLocal,Company,DocumentType,DocumentNumber,Counterparty,Notes,Comment,AmountKzt,r.[LineNo]
 FROM dbo.DdsDrillRows277 r JOIN #ids i ON i.Id=r.CashFlowItemId
 WHERE r.BatchId=@BatchId AND r.EntryDateLocal>=@FromMonth AND r.EntryDateLocal<DATEADD(month,1,@ToMonth)
 ORDER BY
  CASE WHEN @SortBy=N'entryDateLocal' AND @SortDirection=N'asc' THEN EntryDateLocal END ASC,
  CASE WHEN @SortBy=N'entryDateLocal' AND @SortDirection=N'desc' THEN EntryDateLocal END DESC,
  CASE WHEN @SortBy=N'company' AND @SortDirection=N'asc' THEN Company END COLLATE Cyrillic_General_100_CI_AS ASC,
  CASE WHEN @SortBy=N'company' AND @SortDirection=N'desc' THEN Company END COLLATE Cyrillic_General_100_CI_AS DESC,
  CASE WHEN @SortBy=N'documentType' AND @SortDirection=N'asc' THEN DocumentType END COLLATE Cyrillic_General_100_CI_AS ASC,
  CASE WHEN @SortBy=N'documentType' AND @SortDirection=N'desc' THEN DocumentType END COLLATE Cyrillic_General_100_CI_AS DESC,
  CASE WHEN @SortBy=N'document' AND @SortDirection=N'asc' THEN DocumentNumber END COLLATE Cyrillic_General_100_CI_AS ASC,
  CASE WHEN @SortBy=N'document' AND @SortDirection=N'desc' THEN DocumentNumber END COLLATE Cyrillic_General_100_CI_AS DESC,
  CASE WHEN @SortBy=N'counterparty' AND @SortDirection=N'asc' THEN Counterparty END COLLATE Cyrillic_General_100_CI_AS ASC,
  CASE WHEN @SortBy=N'counterparty' AND @SortDirection=N'desc' THEN Counterparty END COLLATE Cyrillic_General_100_CI_AS DESC,
  CASE WHEN @SortBy=N'notes' AND @SortDirection=N'asc' THEN Notes END COLLATE Cyrillic_General_100_CI_AS ASC,
  CASE WHEN @SortBy=N'notes' AND @SortDirection=N'desc' THEN Notes END COLLATE Cyrillic_General_100_CI_AS DESC,
  CASE WHEN @SortBy=N'comment' AND @SortDirection=N'asc' THEN Comment END COLLATE Cyrillic_General_100_CI_AS ASC,
  CASE WHEN @SortBy=N'comment' AND @SortDirection=N'desc' THEN Comment END COLLATE Cyrillic_General_100_CI_AS DESC,
  CASE WHEN @SortBy=N'amountKzt' AND @SortDirection=N'asc' THEN AmountKzt END ASC,
  CASE WHEN @SortBy=N'amountKzt' AND @SortDirection=N'desc' THEN AmountKzt END DESC,
  CASE WHEN @SortBy=N'lineNo' AND @SortDirection=N'asc' THEN r.[LineNo] END ASC,
  CASE WHEN @SortBy=N'lineNo' AND @SortDirection=N'desc' THEN r.[LineNo] END DESC,
  -- No chosen sort preserves the exact old order. Stable source identities
  -- break every equal-key tie before OFFSET, so pages cannot skip/repeat rows.
  CASE WHEN @SortBy IS NULL THEN ABS(AmountKzt) END DESC,
  EntryDateLocal DESC,DatabaseName,RecorderType,RecorderRef,[LineNo],Direction
 OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END;
GO
GRANT EXECUTE ON dbo.ReadDdsDrill313 TO [NT SERVICE\BmsWebCurrent];
