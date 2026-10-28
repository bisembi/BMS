-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    SQL039: publish the counterparty BIN used by the BMS directory.

    The business code remains an internal CP-###### sequence.  BIN is the
    user-facing identifier and is nullable because foreign counterparties and
    historical source rows can legitimately have no Kazakhstan BIN/IIN.
*/
IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS counterparty BIN refused: run SQL039 outside an ambient transaction.', 16, 1);
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
    THROW 63921, N'BMS counterparty BIN refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 63922, N'BMS counterparty BIN refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.Counterparties', N'U') IS NULL
    THROW 63923, N'BMS counterparty BIN refused: required managed objects are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no > 39
    )
        THROW 63924, N'BMS counterparty BIN refused: a future schema version is registered.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no BETWEEN 1 AND 38
    ) <> 38
        THROW 63925, N'BMS counterparty BIN refused: SQL001-SQL038 predecessors are incomplete.', 1;

    IF COL_LENGTH(N'bms_web.Counterparties', N'Bin') IS NULL
        EXEC(N'ALTER TABLE bms_web.Counterparties
              ADD Bin nvarchar(12) COLLATE Latin1_General_100_BIN2 NULL;');

    DECLARE @duplicateBin bit = 0;
    EXEC sys.sp_executesql
        N'SELECT @duplicateBin = CONVERT(bit, CASE WHEN EXISTS
            (SELECT 1 FROM bms_web.Counterparties
             WHERE Bin IS NOT NULL
             GROUP BY Bin
             HAVING COUNT(*) > 1) THEN 1 ELSE 0 END);',
        N'@duplicateBin bit OUTPUT',
        @duplicateBin OUTPUT;
    IF @duplicateBin = 1
        THROW 63926, N'BMS counterparty BIN refused: duplicate BIN values already exist.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'bms_web.Counterparties')
          AND name = N'IX_bms_web_Counterparties_Bin'
    )
        EXEC(N'CREATE UNIQUE NONCLUSTERED INDEX IX_bms_web_Counterparties_Bin
              ON bms_web.Counterparties (Bin ASC)
              WHERE Bin IS NOT NULL;');

    IF (SELECT COUNT(*) FROM sys.columns
        WHERE object_id = OBJECT_ID(N'bms_web.Counterparties')) <> 5
        THROW 63927, N'BMS counterparty BIN refused: Counterparties column count is drifted.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'bms_web.Counterparties')
          AND name = N'Bin'
          AND TYPE_NAME(user_type_id) = N'nvarchar'
          AND max_length = 24
          AND is_nullable = 1
          AND collation_name COLLATE Latin1_General_100_BIN2 =
              N'Latin1_General_100_BIN2' COLLATE Latin1_General_100_BIN2
          AND is_computed = 0
          AND is_identity = 0
          AND default_object_id = 0
          AND rule_object_id = 0
    )
        THROW 63928, N'BMS counterparty BIN refused: BIN column contract is drifted.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes AS i
        WHERE i.object_id = OBJECT_ID(N'bms_web.Counterparties')
          AND i.name = N'IX_bms_web_Counterparties_Bin'
          AND i.type = 2
          AND i.is_unique = 1
          AND i.has_filter = 1
          AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(i.filter_definition, N'[', N''), N']', N''), N' ', N''), N'(', N''), N')', N'')
                COLLATE Latin1_General_100_BIN2 = N'BinISNOTNULL' COLLATE Latin1_General_100_BIN2
          AND (SELECT COUNT(*) FROM sys.index_columns AS ic
               WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                 AND ic.key_ordinal = 1 AND ic.is_included_column = 0
                 AND ic.is_descending_key = 0
                 AND EXISTS
                 (
                     SELECT 1 FROM sys.columns AS c
                     WHERE c.object_id = ic.object_id AND c.column_id = ic.column_id
                       AND c.name = N'Bin'
                 )) = 1
    )
        THROW 63929, N'BMS counterparty BIN refused: BIN unique index is drifted.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 39
    )
        INSERT INTO bms_web.schema_version (version_no, script_name)
        VALUES (39, N'039_counterparty_bin.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
