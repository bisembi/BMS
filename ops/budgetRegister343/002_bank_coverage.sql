-- Additive, finite, read-only source evidence. Existing exchange readers stay unchanged.
SET NOCOUNT ON;
IF CONVERT(nvarchar(128),SERVERPROPERTY('MachineName'))<>N'BMS'
 OR CONVERT(nvarchar(40),CONNECTIONPROPERTY('net_transport'))<>N'Shared memory'
 OR NOT(DB_NAME()=N'BmsDdsReplica2026' OR (DB_NAME()=N'BmsBudgetExecution343_ReplicaTest'
  AND EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BmsBudgetExecution343Owner'
   AND CONVERT(nvarchar(128),value)=N'BMS_EXECUTION_343_REPLICA_ISOLATED')))
 OR NOT EXISTS(SELECT 1 FROM dbo.ReplicaMeta WHERE OneId=1 AND OwnerMarker='6FC44B33-6166-4D35-85F1-622190601EB6' AND ReaderVersion=1)
 THROW 51000,'Exact budget payment evidence replica required',1;
IF OBJECT_ID(N'dbo.ReadAccountingBudgetCoverage343') IS NOT NULL THROW 51000,'Budget reader already exists; inspect rather than overwrite',1;
GO
CREATE PROCEDURE dbo.ReadAccountingBudgetCoverage343 @SnapshotId uniqueidentifier,@DocumentKeysJson nvarchar(max)
WITH EXECUTE AS OWNER
AS
BEGIN
 SET NOCOUNT ON;
 IF ISJSON(@DocumentKeysJson)<>1 OR DATALENGTH(@DocumentKeysJson)>40000000
  OR NOT EXISTS(SELECT 1 FROM dbo.Snapshots WHERE SnapshotId=@SnapshotId AND IsSealed=1)
  THROW 51000,'Exact sealed DDS capture required',1;
 DECLARE @keys TABLE(DocumentKey nvarchar(150) COLLATE Latin1_General_100_BIN2 PRIMARY KEY);
 INSERT @keys SELECT value FROM OPENJSON(@DocumentKeysJson);
 IF (SELECT COUNT(*) FROM @keys)>100000 THROW 51000,'Budget coverage request too large',1;
 SELECT k.DocumentKey,r.[LineNo],r.EntryDateLocalText AS EntryDateLocal,r.SourceAmountKzt AS AmountKzt,
  COALESCE(x.BmsItemId,b.BmsItemId) AS CashFlowItemId
 FROM dbo.RawCashLegs r
 JOIN @keys k ON k.DocumentKey=r.DatabaseName+N'|'+r.RecorderType+N'|'+r.RecorderRef
 JOIN dbo.SnapshotLegEvidence e ON e.SnapshotId=r.SnapshotId AND e.DatabaseName=r.DatabaseName
  AND e.RecorderType=r.RecorderType AND e.RecorderRef=r.RecorderRef AND e.[LineNo]=r.[LineNo] AND e.Direction=r.Direction
 LEFT JOIN dbo.SourceBindings b ON b.DatabaseName=r.DatabaseName AND b.ArticleRef=r.ArticleRef
 LEFT JOIN dbo.SourceBindingExtensions x ON x.DatabaseName=r.DatabaseName AND x.ArticleRef=r.ArticleRef
 WHERE r.SnapshotId=@SnapshotId AND r.Direction='Ct' AND r.SourceAmountKzt>0
  AND e.ExclusionRule='NONE' AND COALESCE(x.IsExcluded,b.IsExcluded,1)=0
 ORDER BY k.DocumentKey,r.[LineNo];
END;
