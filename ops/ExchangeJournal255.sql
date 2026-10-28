-- Additive read-only procedures. No source data, bindings or existing procedures are changed.
-- Execute in the owner-marked DDS replica; grant only EXECUTE to the existing BMS service user.
SET NOCOUNT ON;
IF DB_NAME() NOT IN(N'BmsDdsReplica2026',N'BmsGoLive255_Replica')
    THROW 51000, 'Wrong DDS journal database.', 1;
IF NOT EXISTS(SELECT 1 FROM dbo.ReplicaMeta WHERE OneId=1 AND OwnerMarker='6FC44B33-6166-4D35-85F1-622190601EB6' AND ReaderVersion=1)
    THROW 51000, 'DDS owner marker required.', 1;
GO
CREATE OR ALTER PROCEDURE dbo.ReadExchangeRuns255
    @FromUtc datetimeoffset(7)=NULL,@ToUtc datetimeoffset(7)=NULL,@Status varchar(10)='all',
    @Skip int=0,@Take int=50
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF @Skip<0 OR @Skip>10000 OR @Take<1 OR @Take>10100 OR @Status NOT IN('all','success','failed','running')
        OR (@FromUtc IS NOT NULL AND @ToUtc IS NOT NULL AND @FromUtc>=@ToUtc)
        THROW 51000, 'Invalid journal filter.', 1;
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRANSACTION;
    SELECT COUNT(*) AS Total FROM dbo.CollectionRuns r
    WHERE (@FromUtc IS NULL OR r.StartedAtUtc>=@FromUtc) AND (@ToUtc IS NULL OR r.StartedAtUtc<@ToUtc)
      AND (@Status='all' OR @Status='success' AND r.Status='PASS' OR @Status='failed' AND r.Status='FAIL'
           OR @Status='running' AND r.Status NOT IN('PASS','FAIL'));
    SELECT r.RunId,r.StartedAtUtc,r.FinishedAtUtc,r.Status,r.SnapshotId,r.ErrorCode,s.SourceLegs,
        s.PublishedAtUtc,s.CapturedAtUtc
    FROM dbo.CollectionRuns r LEFT JOIN dbo.Snapshots s ON s.SnapshotId=r.SnapshotId
    WHERE (@FromUtc IS NULL OR r.StartedAtUtc>=@FromUtc) AND (@ToUtc IS NULL OR r.StartedAtUtc<@ToUtc)
      AND (@Status='all' OR @Status='success' AND r.Status='PASS' OR @Status='failed' AND r.Status='FAIL'
           OR @Status='running' AND r.Status NOT IN('PASS','FAIL'))
    ORDER BY r.StartedAtUtc DESC,CONVERT(char(36),r.RunId) COLLATE Latin1_General_100_BIN2 DESC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
    SELECT a.VerifiedSourceCapturedAtUtc,a.LastCheckedAtUtc,s.CutoffExclusiveLocal,
        (SELECT MAX(TRY_CONVERT(date,l.EntryDateLocalText)) FROM dbo.RawCashLegs l WHERE l.SnapshotId=a.SnapshotId) AS LatestBusinessDate,
        s.SourceLegs,s.ReceiptsAmountKzt,s.PaymentsAmountKzt,s.NetCashFlowAmountKzt
    FROM dbo.ActiveSnapshot a JOIN dbo.Snapshots s ON s.SnapshotId=a.SnapshotId AND s.IsSealed=1;
    COMMIT TRANSACTION;
END;
GO
CREATE OR ALTER PROCEDURE dbo.ReadExchangeRegistry255
    @RunId uniqueidentifier,@IncludeExcluded bit=0,@Skip int=0,@Take int=50
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF @RunId IS NULL OR @Skip<0 OR @Skip>1000000 OR @Take NOT IN(50,100)
        THROW 51000, 'Invalid journal registry page.', 1;
    -- Published snapshots and their raw/evidence rows are immutable. Capture one run/snapshot identity.
    DECLARE @SnapshotId uniqueidentifier,@Status varchar(16),@Found bit=0;
    SELECT @Found=1,@SnapshotId=SnapshotId,@Status=Status FROM dbo.CollectionRuns WHERE RunId=@RunId;
    SELECT @Found AS RunFound,@SnapshotId AS SnapshotId,@Status AS Status;
    SELECT COUNT(*) AS Total,
        COALESCE(SUM(CASE WHEN p.ExclusionCode='NONE' AND l.Direction='Dt' THEN l.SourceAmountKzt ELSE 0 END),0) AS ReceiptsAmountKzt,
        COALESCE(SUM(CASE WHEN p.ExclusionCode='NONE' AND l.Direction='Ct' THEN l.SourceAmountKzt ELSE 0 END),0) AS PaymentsAmountKzt
    FROM dbo.RawCashLegs l
    LEFT JOIN dbo.SnapshotLegEvidence e ON e.SnapshotId=l.SnapshotId AND e.DatabaseName=l.DatabaseName
        AND e.RecorderType=l.RecorderType AND e.RecorderRef=l.RecorderRef AND e.[LineNo]=l.[LineNo] AND e.Direction=l.Direction
    -- Immutable-name policy: CashFlowActualsSource.IsExcludedArticle.
    -- Never join current bindings to historical rows.
    CROSS APPLY(VALUES(CASE WHEN l.ArticleName LIKE N'%ВГО%' OR l.ArticleName LIKE N'%Внутригрупп%'
        OR LEFT(l.ArticleName,6) IN(N'111116',N'710006',N'111121',N'111122',N'859701',N'859702')
        THEN 'INTERCOMPANY_V1' ELSE COALESCE(e.ExclusionRule,'MISSING') END)) p(ExclusionCode)
    WHERE l.SnapshotId=@SnapshotId AND (@IncludeExcluded=1 OR p.ExclusionCode='NONE');
    SELECT l.EntryDateLocalText,l.DatabaseName,l.ArticleName,e.CounterpartyName,l.Direction,l.SourceAmountKzt,p.ExclusionCode AS ExclusionRule
    FROM dbo.RawCashLegs l
    LEFT JOIN dbo.SnapshotLegEvidence e ON e.SnapshotId=l.SnapshotId AND e.DatabaseName=l.DatabaseName
        AND e.RecorderType=l.RecorderType AND e.RecorderRef=l.RecorderRef AND e.[LineNo]=l.[LineNo] AND e.Direction=l.Direction
    CROSS APPLY(VALUES(CASE WHEN l.ArticleName LIKE N'%ВГО%' OR l.ArticleName LIKE N'%Внутригрупп%'
        OR LEFT(l.ArticleName,6) IN(N'111116',N'710006',N'111121',N'111122',N'859701',N'859702')
        THEN 'INTERCOMPANY_V1' ELSE COALESCE(e.ExclusionRule,'MISSING') END)) p(ExclusionCode)
    WHERE l.SnapshotId=@SnapshotId AND (@IncludeExcluded=1 OR p.ExclusionCode='NONE')
    ORDER BY l.EntryDateLocalText DESC,l.DatabaseName,l.RegisterName,l.RecorderType,l.RecorderRef,l.[LineNo],l.Direction
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END;
GO
IF DATABASE_PRINCIPAL_ID(N'NT SERVICE\BmsWebCurrent') IS NULL
    THROW 51000, 'Existing BMS reader principal required.', 1;
GRANT EXECUTE ON dbo.ReadExchangeRuns255 TO [NT SERVICE\BmsWebCurrent];
GRANT EXECUTE ON dbo.ReadExchangeRegistry255 TO [NT SERVICE\BmsWebCurrent];
