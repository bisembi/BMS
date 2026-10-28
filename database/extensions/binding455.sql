SET XACT_ABORT ON;
IF CONVERT(nvarchar(128),SERVERPROPERTY('MachineName'))<>N'BMS' OR DB_NAME() NOT IN(N'BmsDdsReplica2026',N'BmsPayroll455_Test') THROW 65503,'EXACT_BINDING455_DATABASE',1;
IF NOT EXISTS(SELECT 1 FROM dbo.ReplicaMeta WHERE OneId=1 AND OwnerMarker='6FC44B33-6166-4D35-85F1-622190601EB6' AND BindingSHA256='0375B39C632DF0246C86BABFEFBE10E10B0228FB5831899BA2C5CEF9F9BF3A9E') THROW 65503,'BINDING455_OWNER',1;
IF OBJECT_ID(N'dbo.SourceBindingSupplement455') IS NOT NULL THROW 65503,'NO_BINDING455_OVERWRITE',1;
IF EXISTS(SELECT 1 FROM(SELECT DatabaseName,ArticleRef FROM dbo.SourceBindings UNION ALL SELECT DatabaseName,ArticleRef FROM dbo.SourceBindingExtensions UNION ALL SELECT DatabaseName,ArticleRef FROM dbo.SourceBindingSupplement415)b WHERE b.DatabaseName=N'ExampleAccountingA' AND b.ArticleRef='91E8BE3AF2B6059F11F1C228D44B526A') THROW 65503,'NO_BINDING455_OVERLAP',1;
CREATE TABLE dbo.SourceBindingSupplement455(
 DatabaseName nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 ArticleRef char(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
 ArticleCode nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 SourceName nvarchar(500) COLLATE Latin1_General_100_BIN2 NOT NULL,
 CanonicalName nvarchar(500) COLLATE Latin1_General_100_BIN2 NOT NULL,
 IsExcluded bit NOT NULL,BmsItemId uniqueidentifier NOT NULL,
 PolicySHA256 char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 PRIMARY KEY(DatabaseName,ArticleRef),
 CHECK(DatabaseName=N'ExampleAccountingA' AND ArticleRef='91E8BE3AF2B6059F11F1C228D44B526A' AND ArticleCode=N'000000357'
  AND SourceName=N'857002 Архивирование и утилизация документов' AND CanonicalName=SourceName AND IsExcluded=0
  AND BmsItemId='a9df5fd2-7a30-416c-9500-b2ab100ea6a0'
  AND PolicySHA256='6BCFBF6E6E31A76EC5ECC890AD7F9D60CFE5C83245D9A98BC4EFBD9CF298888F'));
INSERT dbo.SourceBindingSupplement455 VALUES(N'ExampleAccountingA','91E8BE3AF2B6059F11F1C228D44B526A',N'000000357',N'857002 Архивирование и утилизация документов',N'857002 Архивирование и утилизация документов',0,'a9df5fd2-7a30-416c-9500-b2ab100ea6a0','6BCFBF6E6E31A76EC5ECC890AD7F9D60CFE5C83245D9A98BC4EFBD9CF298888F');
CREATE TABLE dbo.SnapshotBindingProof455(SnapshotId uniqueidentifier PRIMARY KEY REFERENCES dbo.Snapshots(SnapshotId),
 SupplementSHA256 char(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(SupplementSHA256='6BCFBF6E6E31A76EC5ECC890AD7F9D60CFE5C83245D9A98BC4EFBD9CF298888F'));
GO
CREATE TRIGGER dbo.SourceBindingSupplement455Frozen ON dbo.SourceBindingSupplement455 AFTER INSERT,UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;THROW 65503,'BINDING455_SEALED',1;END;
GO
CREATE TRIGGER dbo.SnapshotBindingProof455Immutable ON dbo.SnapshotBindingProof455 AFTER INSERT,UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted) OR EXISTS(SELECT 1 FROM inserted i JOIN dbo.Snapshots s ON s.SnapshotId=i.SnapshotId WHERE s.IsSealed<>0) THROW 65503,'SNAPSHOT455_PROOF_IMMUTABLE',1;
END;
GO
CREATE PROCEDURE dbo.ReadExchangeGeneration455 WITH EXECUTE AS OWNER AS
BEGIN
 SET NOCOUNT ON;
 IF NOT EXISTS(SELECT 1 FROM dbo.ReplicaMeta WHERE OneId=1 AND OwnerMarker='6FC44B33-6166-4D35-85F1-622190601EB6' AND ReaderVersion=1) THROW 65503,'GENERATION455_OWNER',1;
 SELECT s.SnapshotId,s.SourceGeneration,a.VerifiedSourceCapturedAtUtc,s.CutoffExclusiveLocal,p.Id AS PaymentId,p.CapturedAtUtc,p.CutoffExclusiveLocal AS PaymentCutoff,p.DocumentCount,p.LegCount,proof.SupplementSHA256
 FROM dbo.ActiveSnapshot a JOIN dbo.Snapshots s ON s.SnapshotId=a.SnapshotId AND s.IsSealed=1
 CROSS JOIN dbo.ActivePaymentCapture ap JOIN dbo.PaymentCaptureBatches p ON p.Id=ap.BatchId AND p.IsSealed=1
 LEFT JOIN dbo.SnapshotBindingProof455 proof ON proof.SnapshotId=s.SnapshotId WHERE a.OneId=1;
END;
