-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    SQL041: budget cash-flow direction plus immutable approved BDDS snapshots.

    Existing budget lines are payments. Imported source amounts keep their
    cent precision and provenance in SQL Server; no application-server data
    files participate in the report.
*/
IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS BDDS migration refused: run SQL041 outside an ambient transaction.', 16, 1);
    RETURN;
END;

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

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 64121, N'BMS BDDS migration refused: current database must be the explicitly owned BMS database.', 1;
IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 64122, N'BMS BDDS migration refused: current database is not READ_WRITE.', 1;
IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.BudgetInstances', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.CostCenters', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.CashFlowItems', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.Employees', N'U') IS NULL
    THROW 64123, N'BMS BDDS migration refused: required managed objects are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK) WHERE version_no > 41)
        THROW 64124, N'BMS BDDS migration refused: a future schema version is registered.', 1;
    IF (SELECT COUNT(*) FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no BETWEEN 1 AND 40) <> 40
        THROW 64125, N'BMS BDDS migration refused: SQL001-SQL040 predecessors are incomplete.', 1;

    IF COL_LENGTH(N'bms_web.BudgetInstanceLines', N'FlowDirection') IS NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'bms_web.BudgetInstanceLines')
              AND name = N'UX_bms_web_BudgetInstanceLines_Key')
        BEGIN
            IF EXISTS (SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'bms_web.BudgetInstanceLines')
                  AND name = N'UX_bms_web_BudgetInstanceLines_Key'
                  AND is_unique_constraint = 1)
                ALTER TABLE bms_web.BudgetInstanceLines
                    DROP CONSTRAINT UX_bms_web_BudgetInstanceLines_Key;
            ELSE
                DROP INDEX UX_bms_web_BudgetInstanceLines_Key ON bms_web.BudgetInstanceLines;
        END;

        ALTER TABLE bms_web.BudgetInstanceLines
            ADD FlowDirection nvarchar(10) COLLATE Latin1_General_100_BIN2 NOT NULL
                CONSTRAINT DF_bms_web_BudgetInstanceLines_FlowDirection DEFAULT N'Payment' WITH VALUES;
    END;

    EXEC sys.sp_executesql N'
        IF EXISTS (SELECT 1 FROM bms_web.BudgetInstanceLines
            WHERE FlowDirection NOT IN (N''Payment'', N''Receipt''))
            THROW 64126, N''BMS BDDS migration refused: an invalid flow direction exists.'', 1;';

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.BudgetInstanceLines')
          AND name = N'CK_bms_web_BudgetInstanceLines_direction')
        EXEC(N'ALTER TABLE bms_web.BudgetInstanceLines WITH CHECK
            ADD CONSTRAINT CK_bms_web_BudgetInstanceLines_direction
                CHECK (FlowDirection = N''Payment'' OR FlowDirection = N''Receipt'');');

    IF EXISTS (SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U')
          AND name = N'UX_bms_web_BudgetInstanceLines_Key')
       AND NOT
       (
           EXISTS (SELECT 1 FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U')
              AND name = N'UX_bms_web_BudgetInstanceLines_Key'
              AND is_unique = 1
              AND is_disabled = 0)
           AND
           (SELECT COUNT(*) FROM sys.index_columns
            WHERE object_id = OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U')
              AND index_id = INDEXPROPERTY(
                  OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U'),
                  N'UX_bms_web_BudgetInstanceLines_Key', N'IndexId')
              AND key_ordinal > 0) = 4
           AND EXISTS (SELECT 1 FROM sys.index_columns WHERE object_id = OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U') AND index_id = INDEXPROPERTY(OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U'), N'UX_bms_web_BudgetInstanceLines_Key', N'IndexId') AND key_ordinal = 1 AND column_id = COLUMNPROPERTY(OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U'), N'BudgetInstanceId', N'ColumnId'))
           AND EXISTS (SELECT 1 FROM sys.index_columns WHERE object_id = OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U') AND index_id = INDEXPROPERTY(OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U'), N'UX_bms_web_BudgetInstanceLines_Key', N'IndexId') AND key_ordinal = 2 AND column_id = COLUMNPROPERTY(OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U'), N'CashFlowItemId', N'ColumnId'))
           AND EXISTS (SELECT 1 FROM sys.index_columns WHERE object_id = OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U') AND index_id = INDEXPROPERTY(OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U'), N'UX_bms_web_BudgetInstanceLines_Key', N'IndexId') AND key_ordinal = 3 AND column_id = COLUMNPROPERTY(OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U'), N'BudgetMonth', N'ColumnId'))
           AND EXISTS (SELECT 1 FROM sys.index_columns WHERE object_id = OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U') AND index_id = INDEXPROPERTY(OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U'), N'UX_bms_web_BudgetInstanceLines_Key', N'IndexId') AND key_ordinal = 4 AND column_id = COLUMNPROPERTY(OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U'), N'FlowDirection', N'ColumnId'))
       )
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U')
              AND name = N'UX_bms_web_BudgetInstanceLines_Key'
              AND is_unique_constraint = 1)
            ALTER TABLE bms_web.BudgetInstanceLines
                DROP CONSTRAINT UX_bms_web_BudgetInstanceLines_Key;
        ELSE
            DROP INDEX UX_bms_web_BudgetInstanceLines_Key ON bms_web.BudgetInstanceLines;
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U')
          AND name = N'UX_bms_web_BudgetInstanceLines_Key')
        EXEC(N'CREATE UNIQUE INDEX UX_bms_web_BudgetInstanceLines_Key
            ON bms_web.BudgetInstanceLines
                (BudgetInstanceId, CashFlowItemId, BudgetMonth, FlowDirection);');

    IF OBJECT_ID(N'bms_web.BddsSnapshots', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.BddsSnapshots
        (
            Id uniqueidentifier NOT NULL,
            SourceVersionDate date NOT NULL,
            Scenario nvarchar(100) NOT NULL,
            BudgetYear int NOT NULL,
            SourceSystem nvarchar(100) NOT NULL,
            SourceTable nvarchar(128) NOT NULL,
            SourceHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
            SourceRowCount int NOT NULL,
            CostCenterCount int NOT NULL,
            ArticleCount int NOT NULL,
            NonZeroCellCount int NOT NULL,
            ReceiptsAmountKzt decimal(19, 2) NOT NULL,
            PaymentsAmountKzt decimal(19, 2) NOT NULL,
            NetCashFlowAmountKzt decimal(19, 2) NOT NULL,
            CreatedAtUtc datetimeoffset(3) NOT NULL,
            ApprovedAtUtc datetimeoffset(3) NOT NULL,
            ApprovedByEmployeeId uniqueidentifier NOT NULL,
            ApprovedByExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
            IsSealed bit NOT NULL
                CONSTRAINT DF_bms_web_BddsSnapshots_IsSealed DEFAULT (0),
            CONSTRAINT PK_bms_web_BddsSnapshots PRIMARY KEY (Id),
            CONSTRAINT FK_bms_web_BddsSnapshots_ApprovedByEmployee FOREIGN KEY (ApprovedByEmployeeId)
                REFERENCES bms_web.Employees(Id),
            CONSTRAINT CK_bms_web_BddsSnapshots_counts CHECK
                (BudgetYear BETWEEN 2000 AND 2100 AND SourceRowCount > 0
                 AND CostCenterCount > 0 AND ArticleCount > 0 AND NonZeroCellCount > 0),
            CONSTRAINT CK_bms_web_BddsSnapshots_hash CHECK
                (SourceHash NOT LIKE '%[^0-9A-F]%' AND LEN(SourceHash) = 64),
            CONSTRAINT CK_bms_web_BddsSnapshots_totals CHECK
                (ReceiptsAmountKzt >= 0 AND PaymentsAmountKzt >= 0
                 AND NetCashFlowAmountKzt = ReceiptsAmountKzt - PaymentsAmountKzt),
            CONSTRAINT CK_bms_web_BddsSnapshots_utc CHECK
                (DATEPART(TZOFFSET, CreatedAtUtc) = 0 AND DATEPART(TZOFFSET, ApprovedAtUtc) = 0)
        );
        CREATE UNIQUE INDEX UX_bms_web_BddsSnapshots_SourceHash
            ON bms_web.BddsSnapshots(SourceHash);
        CREATE INDEX IX_bms_web_BddsSnapshots_Year_ApprovedAtUtc
            ON bms_web.BddsSnapshots(BudgetYear, ApprovedAtUtc DESC);
    END;

    IF OBJECT_ID(N'bms_web.BddsSnapshotRows', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.BddsSnapshotRows
        (
            Id bigint IDENTITY(1, 1) NOT NULL,
            SnapshotId uniqueidentifier NOT NULL,
            SourceOrdinal int NOT NULL,
            SourceCostCenterName nvarchar(500) NOT NULL,
            MappedCostCenterId uniqueidentifier NOT NULL,
            ActivityName nvarchar(500) NOT NULL,
            SectionName nvarchar(500) NOT NULL,
            GroupName nvarchar(500) NOT NULL,
            ArticleBusinessCode nvarchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
            ArticleName nvarchar(500) NOT NULL,
            CashFlowItemId uniqueidentifier NOT NULL,
            BudgetMonth date NOT NULL,
            SignedAmountKzt decimal(19, 2) NOT NULL,
            SourceFile nvarchar(1000) NOT NULL,
            SourceSheet nvarchar(200) NOT NULL,
            SourceRowNumber int NOT NULL,
            SourceLoadedAtUtc datetimeoffset(3) NOT NULL,
            RowHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
            CONSTRAINT PK_bms_web_BddsSnapshotRows PRIMARY KEY (Id),
            CONSTRAINT FK_bms_web_BddsSnapshotRows_Snapshot FOREIGN KEY (SnapshotId)
                REFERENCES bms_web.BddsSnapshots(Id),
            CONSTRAINT FK_bms_web_BddsSnapshotRows_CostCenter FOREIGN KEY (MappedCostCenterId)
                REFERENCES bms_web.CostCenters(Id),
            CONSTRAINT FK_bms_web_BddsSnapshotRows_CashFlowItem FOREIGN KEY (CashFlowItemId)
                REFERENCES bms_web.CashFlowItems(Id),
            CONSTRAINT CK_bms_web_BddsSnapshotRows_values CHECK
                (SourceOrdinal > 0 AND SourceRowNumber > 0 AND DAY(BudgetMonth) = 1
                 AND DATEPART(TZOFFSET, SourceLoadedAtUtc) = 0),
            CONSTRAINT CK_bms_web_BddsSnapshotRows_hash CHECK
                (RowHash NOT LIKE '%[^0-9A-F]%' AND LEN(RowHash) = 64)
        );
        CREATE UNIQUE INDEX UX_bms_web_BddsSnapshotRows_Snapshot_Ordinal
            ON bms_web.BddsSnapshotRows(SnapshotId, SourceOrdinal);
        CREATE INDEX IX_bms_web_BddsSnapshotRows_Report
            ON bms_web.BddsSnapshotRows(SnapshotId, MappedCostCenterId, BudgetMonth)
            INCLUDE (ActivityName, SectionName, GroupName, ArticleBusinessCode,
                     ArticleName, SignedAmountKzt, SourceOrdinal);
    END;

    IF OBJECT_ID(N'bms_web.BddsSnapshotBudgetInstances', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.BddsSnapshotBudgetInstances
        (
            SnapshotId uniqueidentifier NOT NULL,
            BudgetInstanceId uniqueidentifier NOT NULL,
            CostCenterId uniqueidentifier NOT NULL,
            SourceCostCenterName nvarchar(500) NOT NULL,
            CONSTRAINT PK_bms_web_BddsSnapshotBudgetInstances
                PRIMARY KEY (SnapshotId, BudgetInstanceId),
            CONSTRAINT FK_bms_web_BddsSnapshotBudgetInstances_Snapshot FOREIGN KEY (SnapshotId)
                REFERENCES bms_web.BddsSnapshots(Id),
            CONSTRAINT FK_bms_web_BddsSnapshotBudgetInstances_Instance FOREIGN KEY (BudgetInstanceId)
                REFERENCES bms_web.BudgetInstances(Id),
            CONSTRAINT FK_bms_web_BddsSnapshotBudgetInstances_CostCenter FOREIGN KEY (CostCenterId)
                REFERENCES bms_web.CostCenters(Id)
        );
        CREATE UNIQUE INDEX UX_bms_web_BddsSnapshotBudgetInstances_Snapshot_CostCenter
            ON bms_web.BddsSnapshotBudgetInstances(SnapshotId, CostCenterId);
    END;

    IF OBJECT_ID(N'bms_web.TR_BddsSnapshots_Immutable', N'TR') IS NULL
        EXEC(N'CREATE TRIGGER bms_web.TR_BddsSnapshots_Immutable
            ON bms_web.BddsSnapshots AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id = d.Id WHERE i.Id IS NULL)
                    THROW 64131, N''BDDS snapshots cannot be deleted.'', 1;
                IF UPDATE(Id) OR UPDATE(SourceVersionDate) OR UPDATE(Scenario) OR UPDATE(BudgetYear)
                   OR UPDATE(SourceSystem) OR UPDATE(SourceTable) OR UPDATE(SourceHash)
                   OR UPDATE(SourceRowCount) OR UPDATE(CostCenterCount) OR UPDATE(ArticleCount)
                   OR UPDATE(NonZeroCellCount) OR UPDATE(ReceiptsAmountKzt)
                   OR UPDATE(PaymentsAmountKzt) OR UPDATE(NetCashFlowAmountKzt)
                   OR UPDATE(CreatedAtUtc) OR UPDATE(ApprovedAtUtc)
                   OR UPDATE(ApprovedByEmployeeId) OR UPDATE(ApprovedByExternalId)
                    THROW 64131, N''BDDS snapshot contents are immutable.'', 1;
                IF NOT UPDATE(IsSealed)
                   OR EXISTS
                   (
                       SELECT 1
                       FROM inserted i
                       INNER JOIN deleted d ON d.Id = i.Id
                       WHERE d.IsSealed <> 0 OR i.IsSealed <> 1
                   )
                    THROW 64131, N''Only the initial BDDS snapshot seal is allowed.'', 1;
                IF EXISTS
                (
                    SELECT 1
                    FROM inserted i
                    WHERE (SELECT COUNT(*) FROM bms_web.BddsSnapshotRows r WHERE r.SnapshotId = i.Id) <> i.SourceRowCount
                       OR (SELECT COUNT(DISTINCT r.MappedCostCenterId) FROM bms_web.BddsSnapshotRows r WHERE r.SnapshotId = i.Id) <> i.CostCenterCount
                       OR (SELECT COUNT(*) FROM bms_web.BddsSnapshotBudgetInstances l WHERE l.SnapshotId = i.Id) <> i.CostCenterCount
                       OR (SELECT COALESCE(SUM(CASE WHEN r.SignedAmountKzt > 0 THEN r.SignedAmountKzt ELSE 0 END), 0) FROM bms_web.BddsSnapshotRows r WHERE r.SnapshotId = i.Id) <> i.ReceiptsAmountKzt
                       OR (SELECT COALESCE(-SUM(CASE WHEN r.SignedAmountKzt < 0 THEN r.SignedAmountKzt ELSE 0 END), 0) FROM bms_web.BddsSnapshotRows r WHERE r.SnapshotId = i.Id) <> i.PaymentsAmountKzt
                       OR EXISTS (SELECT 1 FROM bms_web.BddsSnapshotRows r WHERE r.SnapshotId = i.Id AND YEAR(r.BudgetMonth) <> i.BudgetYear)
                       OR EXISTS
                       (
                           SELECT 1
                           FROM bms_web.BddsSnapshotBudgetInstances l
                           LEFT JOIN bms_web.BudgetInstances b ON b.Id = l.BudgetInstanceId
                           WHERE l.SnapshotId = i.Id
                             AND (b.Id IS NULL OR b.CostCenterId <> l.CostCenterId OR b.Status <> N''Approved'')
                       )
                )
                    THROW 64134, N''BDDS snapshot cannot be sealed because its persisted contents are incomplete.'', 1;
            END;');
    IF OBJECT_ID(N'bms_web.TR_BddsSnapshotRows_Immutable', N'TR') IS NULL
        EXEC(N'CREATE TRIGGER bms_web.TR_BddsSnapshotRows_Immutable
            ON bms_web.BddsSnapshotRows AFTER INSERT, UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted)
                    THROW 64132, N''BDDS snapshot rows are immutable.'', 1;
                IF EXISTS
                (
                    SELECT 1
                    FROM inserted i
                    INNER JOIN bms_web.BddsSnapshots s ON s.Id = i.SnapshotId
                    WHERE s.IsSealed = 1
                )
                    THROW 64132, N''A sealed BDDS snapshot cannot receive rows.'', 1;
            END;');
    IF OBJECT_ID(N'bms_web.TR_BddsSnapshotBudgetInstances_Immutable', N'TR') IS NULL
        EXEC(N'CREATE TRIGGER bms_web.TR_BddsSnapshotBudgetInstances_Immutable
            ON bms_web.BddsSnapshotBudgetInstances AFTER INSERT, UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted)
                    THROW 64133, N''BDDS snapshot links are immutable.'', 1;
                IF EXISTS
                (
                    SELECT 1
                    FROM inserted i
                    INNER JOIN bms_web.BddsSnapshots s ON s.Id = i.SnapshotId
                    WHERE s.IsSealed = 1
                )
                    THROW 64133, N''A sealed BDDS snapshot cannot receive budget links.'', 1;
            END;');

    IF NOT EXISTS (SELECT 1 FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK) WHERE version_no = 41)
        INSERT INTO bms_web.schema_version (version_no, script_name)
        VALUES (41, N'041_bdds_snapshot_and_flow_direction.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
