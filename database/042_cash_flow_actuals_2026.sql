-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/* SQL042: manually refreshed SQL-only, non-VGO, group ODDS actuals for 2026. */
IF @@TRANCOUNT <> 0 THROW 64200, N'Run SQL042 outside an ambient transaction.', 1;
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase')) THROW 64201, N'SQL042 requires the explicitly owned BMS database.', 1;
IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE' THROW 64202, N'the explicitly owned BMS database must be writable.', 1;
IF OBJECT_ID(N'bms_web.schema_version',N'U') IS NULL
    OR OBJECT_ID(N'bms_web.CashFlowItems',N'U') IS NULL
    OR OBJECT_ID(N'bms_web.Employees',N'U') IS NULL THROW 64203, N'Required managed objects are absent.', 1;
BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM bms_web.schema_version WITH (UPDLOCK,HOLDLOCK) WHERE version_no>42)
        THROW 64204, N'Future schema version present.', 1;
    IF (SELECT COUNT(*) FROM bms_web.schema_version WITH (UPDLOCK,HOLDLOCK) WHERE version_no BETWEEN 1 AND 41)<>41
        THROW 64205, N'SQL001-SQL041 predecessors are incomplete.', 1;

    IF OBJECT_ID(N'bms_web.CashFlowActualsSnapshots',N'U') IS NULL
    CREATE TABLE bms_web.CashFlowActualsSnapshots
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_bms_web_CashFlowActualsSnapshots PRIMARY KEY,
        SourceYear int NOT NULL,
        SourceHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SourceGeneration char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CapturedAtUtc datetimeoffset(3) NOT NULL,
        ImportedAtUtc datetimeoffset(3) NOT NULL,
        ImportedByEmployeeId uniqueidentifier NOT NULL,
        ImportedByExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [RowCount] int NOT NULL,
        ArticleCount int NOT NULL,
        ReceiptsAmountKzt decimal(19,2) NOT NULL,
        PaymentsAmountKzt decimal(19,2) NOT NULL,
        NetCashFlowAmountKzt decimal(19,2) NOT NULL,
        IsSealed bit NOT NULL,
        CONSTRAINT FK_bms_web_CashFlowActualsSnapshots_Employee FOREIGN KEY(ImportedByEmployeeId) REFERENCES bms_web.Employees(Id),
        CONSTRAINT CK_bms_web_CashFlowActualsSnapshots_Year CHECK(SourceYear=2026),
        CONSTRAINT CK_bms_web_CashFlowActualsSnapshots_Hashes CHECK(LEN(SourceHash)=64 AND LEN(SourceGeneration)=64 AND SourceHash NOT LIKE '%[^0-9A-F]%' AND SourceGeneration NOT LIKE '%[^0-9A-F]%'),
        CONSTRAINT CK_bms_web_CashFlowActualsSnapshots_Counts CHECK([RowCount] BETWEEN 1 AND 24000 AND ArticleCount BETWEEN 1 AND 2000),
        CONSTRAINT CK_bms_web_CashFlowActualsSnapshots_Totals CHECK(ReceiptsAmountKzt>=0 AND PaymentsAmountKzt>=0 AND NetCashFlowAmountKzt=ReceiptsAmountKzt-PaymentsAmountKzt),
        CONSTRAINT CK_bms_web_CashFlowActualsSnapshots_Actor CHECK(DATALENGTH(ImportedByExternalId)>0 AND ImportedByExternalId=LTRIM(RTRIM(ImportedByExternalId))),
        CONSTRAINT CK_bms_web_CashFlowActualsSnapshots_Utc CHECK(DATEPART(TZOFFSET,CapturedAtUtc)=0 AND DATEPART(TZOFFSET,ImportedAtUtc)=0)
    );
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'bms_web.CashFlowActualsSnapshots') AND name=N'UX_bms_web_CashFlowActualsSnapshots_Hash')
        CREATE UNIQUE INDEX UX_bms_web_CashFlowActualsSnapshots_Hash ON bms_web.CashFlowActualsSnapshots(SourceHash);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'bms_web.CashFlowActualsSnapshots') AND name=N'IX_bms_web_CashFlowActualsSnapshots_Latest')
        CREATE INDEX IX_bms_web_CashFlowActualsSnapshots_Latest ON bms_web.CashFlowActualsSnapshots(SourceYear,IsSealed,ImportedAtUtc);

    IF OBJECT_ID(N'bms_web.CashFlowActualsMonths',N'U') IS NULL
    CREATE TABLE bms_web.CashFlowActualsMonths
    (
        SnapshotId uniqueidentifier NOT NULL,
        BudgetMonth date NOT NULL,
        CONSTRAINT PK_bms_web_CashFlowActualsMonths PRIMARY KEY(SnapshotId,BudgetMonth),
        CONSTRAINT FK_bms_web_CashFlowActualsMonths_Snapshot FOREIGN KEY(SnapshotId) REFERENCES bms_web.CashFlowActualsSnapshots(Id),
        CONSTRAINT CK_bms_web_CashFlowActualsMonths_Year CHECK(YEAR(BudgetMonth)=2026 AND DAY(BudgetMonth)=1)
    );
    IF OBJECT_ID(N'bms_web.CashFlowActualsArticles',N'U') IS NULL
    CREATE TABLE bms_web.CashFlowActualsArticles
    (
        SnapshotId uniqueidentifier NOT NULL,
        ArticleOrdinal int NOT NULL,
        ArticleName nvarchar(500) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CashFlowItemId uniqueidentifier NULL,
        CONSTRAINT PK_bms_web_CashFlowActualsArticles PRIMARY KEY(SnapshotId,ArticleOrdinal),
        CONSTRAINT FK_bms_web_CashFlowActualsArticles_Snapshot FOREIGN KEY(SnapshotId) REFERENCES bms_web.CashFlowActualsSnapshots(Id),
        CONSTRAINT FK_bms_web_CashFlowActualsArticles_Item FOREIGN KEY(CashFlowItemId) REFERENCES bms_web.CashFlowItems(Id),
        CONSTRAINT CK_bms_web_CashFlowActualsArticles_Name CHECK(DATALENGTH(ArticleName)>0 AND ArticleName=LTRIM(RTRIM(ArticleName)) AND ArticleOrdinal>0),
        CONSTRAINT CK_bms_web_CashFlowActualsArticles_NoVgo CHECK
        (
            ArticleName COLLATE Latin1_General_100_CI_AI NOT LIKE N'%ВГО%'
            AND ArticleName COLLATE Latin1_General_100_CI_AI NOT LIKE N'%Внутригрупп%'
            AND LEFT(ArticleName,6) NOT IN(N'111116',N'710006',N'111121',N'111122',N'859701',N'859702')
        )
    );
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'bms_web.CashFlowActualsArticles') AND name=N'UX_bms_web_CashFlowActualsArticles_Name')
        CREATE UNIQUE INDEX UX_bms_web_CashFlowActualsArticles_Name ON bms_web.CashFlowActualsArticles(SnapshotId,ArticleName);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'bms_web.CashFlowActualsArticles') AND name=N'IX_bms_web_CashFlowActualsArticles_Mapping')
        CREATE INDEX IX_bms_web_CashFlowActualsArticles_Mapping ON bms_web.CashFlowActualsArticles(SnapshotId,CashFlowItemId);

    IF OBJECT_ID(N'bms_web.CashFlowActualsRows',N'U') IS NULL
    CREATE TABLE bms_web.CashFlowActualsRows
    (
        SnapshotId uniqueidentifier NOT NULL,
        SourceOrdinal int NOT NULL,
        ArticleOrdinal int NOT NULL,
        BudgetMonth date NOT NULL,
        SignedAmountKzt decimal(18,2) NOT NULL,
        CONSTRAINT PK_bms_web_CashFlowActualsRows PRIMARY KEY(SnapshotId,SourceOrdinal),
        CONSTRAINT FK_bms_web_CashFlowActualsRows_Article FOREIGN KEY(SnapshotId,ArticleOrdinal) REFERENCES bms_web.CashFlowActualsArticles(SnapshotId,ArticleOrdinal),
        CONSTRAINT FK_bms_web_CashFlowActualsRows_Month FOREIGN KEY(SnapshotId,BudgetMonth) REFERENCES bms_web.CashFlowActualsMonths(SnapshotId,BudgetMonth),
        CONSTRAINT CK_bms_web_CashFlowActualsRows_Year CHECK(YEAR(BudgetMonth)=2026 AND DAY(BudgetMonth)=1 AND SourceOrdinal>0)
    );
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'bms_web.CashFlowActualsRows') AND name=N'UX_bms_web_CashFlowActualsRows_Cell')
        CREATE UNIQUE INDEX UX_bms_web_CashFlowActualsRows_Cell ON bms_web.CashFlowActualsRows(SnapshotId,ArticleOrdinal,BudgetMonth);

    EXEC(N'CREATE OR ALTER TRIGGER bms_web.TR_CashFlowActualsSnapshots_Immutable ON bms_web.CashFlowActualsSnapshots AFTER INSERT,UPDATE,DELETE AS
    BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM deleted WHERE IsSealed=1) THROW 64210, N''Sealed actuals are immutable.'',1;
        IF EXISTS
        (
            SELECT 1 FROM inserted AS i WHERE i.IsSealed=1 AND
            (
                i.[RowCount]<>(SELECT COUNT(*) FROM bms_web.CashFlowActualsRows r WHERE r.SnapshotId=i.Id)
                OR i.ArticleCount<>(SELECT COUNT(*) FROM bms_web.CashFlowActualsArticles a WHERE a.SnapshotId=i.Id)
                OR NOT EXISTS(SELECT 1 FROM bms_web.CashFlowActualsMonths m WHERE m.SnapshotId=i.Id)
                OR i.[RowCount]<>i.ArticleCount*(SELECT COUNT(*) FROM bms_web.CashFlowActualsMonths m WHERE m.SnapshotId=i.Id)
                OR i.ReceiptsAmountKzt<>COALESCE((SELECT SUM(CASE WHEN r.SignedAmountKzt>0 THEN r.SignedAmountKzt ELSE 0 END) FROM bms_web.CashFlowActualsRows r WHERE r.SnapshotId=i.Id),0)
                OR i.PaymentsAmountKzt<>COALESCE((SELECT SUM(CASE WHEN r.SignedAmountKzt<0 THEN -r.SignedAmountKzt ELSE 0 END) FROM bms_web.CashFlowActualsRows r WHERE r.SnapshotId=i.Id),0)
            )
        ) THROW 64211,N''Actuals controls do not reconcile.'',1;
    END;');
    EXEC(N'CREATE OR ALTER TRIGGER bms_web.TR_CashFlowActualsMonths_Immutable ON bms_web.CashFlowActualsMonths AFTER INSERT,UPDATE,DELETE AS
    BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM bms_web.CashFlowActualsSnapshots s WHERE s.IsSealed=1 AND s.Id IN(SELECT SnapshotId FROM inserted UNION SELECT SnapshotId FROM deleted))
            THROW 64212,N''Sealed actuals months are immutable.'',1;
    END;');
    EXEC(N'CREATE OR ALTER TRIGGER bms_web.TR_CashFlowActualsArticles_Immutable ON bms_web.CashFlowActualsArticles AFTER INSERT,UPDATE,DELETE AS
    BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM bms_web.CashFlowActualsSnapshots s WHERE s.IsSealed=1 AND s.Id IN(SELECT SnapshotId FROM inserted UNION SELECT SnapshotId FROM deleted))
            THROW 64213,N''Sealed actuals articles are immutable.'',1;
    END;');
    EXEC(N'CREATE OR ALTER TRIGGER bms_web.TR_CashFlowActualsRows_Immutable ON bms_web.CashFlowActualsRows AFTER INSERT,UPDATE,DELETE AS
    BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM bms_web.CashFlowActualsSnapshots s WHERE s.IsSealed=1 AND s.Id IN(SELECT SnapshotId FROM inserted UNION SELECT SnapshotId FROM deleted))
            THROW 64214,N''Sealed actuals rows are immutable.'',1;
    END;');

    IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=42)
        INSERT bms_web.schema_version(version_no,script_name) VALUES(42,N'042_cash_flow_actuals_2026.sql');
    IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=42 AND script_name COLLATE Latin1_General_100_BIN2=N'042_cash_flow_actuals_2026.sql')
        THROW 64215,N'SQL042 registry identity mismatch.',1;
    COMMIT;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK;
    THROW;
END CATCH;
