-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    SQL040: classify each cash-flow item by business activity.

    The stored values are stable English identifiers; the web client renders
    the approved Russian labels. Existing rows are classified as Operating so
    the additive migration never leaves a required value absent.
*/
IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS cash-flow activity type refused: run SQL040 outside an ambient transaction.', 16, 1);
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
    THROW 64021, N'BMS cash-flow activity type refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 64022, N'BMS cash-flow activity type refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.CashFlowItems', N'U') IS NULL
    THROW 64023, N'BMS cash-flow activity type refused: required managed objects are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no > 40
    )
        THROW 64024, N'BMS cash-flow activity type refused: a future schema version is registered.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no BETWEEN 1 AND 39
    ) <> 39
        THROW 64025, N'BMS cash-flow activity type refused: SQL001-SQL039 predecessors are incomplete.', 1;

    IF COL_LENGTH(N'bms_web.CashFlowItems', N'ActivityType') IS NULL
        EXEC(N'ALTER TABLE bms_web.CashFlowItems
            ADD ActivityType nvarchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL
                CONSTRAINT DF_bms_web_CashFlowItems_ActivityType DEFAULT N''Operating'' WITH VALUES;');

    DECLARE @invalidActivityType bit = 0;
    EXEC sys.sp_executesql
        N'SELECT @invalidActivityType = CONVERT(bit, CASE WHEN EXISTS
            (SELECT 1 FROM bms_web.CashFlowItems
             WHERE ActivityType NOT IN (N''Operating'', N''Investing'', N''Financing''))
            THEN 1 ELSE 0 END);',
        N'@invalidActivityType bit OUTPUT',
        @invalidActivityType OUTPUT;
    IF @invalidActivityType = 1
        THROW 64026, N'BMS cash-flow activity type refused: an existing value is outside the approved set.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.CashFlowItems')
          AND name = N'CK_bms_web_CashFlowItems_ActivityType'
    )
        EXEC(N'ALTER TABLE bms_web.CashFlowItems
            ADD CONSTRAINT CK_bms_web_CashFlowItems_ActivityType
            CHECK (ActivityType = N''Operating''
                OR ActivityType = N''Investing''
                OR ActivityType = N''Financing'');');

    IF (SELECT COUNT(*) FROM sys.columns
        WHERE object_id = OBJECT_ID(N'bms_web.CashFlowItems')) <> 10
        THROW 64027, N'BMS cash-flow activity type refused: CashFlowItems column count is drifted.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.columns AS c
        WHERE c.object_id = OBJECT_ID(N'bms_web.CashFlowItems')
          AND c.name = N'ActivityType'
          AND TYPE_NAME(c.user_type_id) = N'nvarchar'
          AND c.max_length = 40
          AND c.is_nullable = 0
          AND c.collation_name COLLATE Latin1_General_100_BIN2 =
              N'Latin1_General_100_BIN2' COLLATE Latin1_General_100_BIN2
          AND c.is_computed = 0
          AND c.is_identity = 0
          AND c.default_object_id <> 0
          AND c.rule_object_id = 0
    )
        THROW 64028, N'BMS cash-flow activity type refused: ActivityType column contract is drifted.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.default_constraints AS dc
        WHERE dc.parent_object_id = OBJECT_ID(N'bms_web.CashFlowItems')
          AND dc.parent_column_id = COLUMNPROPERTY(
              OBJECT_ID(N'bms_web.CashFlowItems'), N'ActivityType', N'ColumnId')
          AND dc.name = N'DF_bms_web_CashFlowItems_ActivityType'
          AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(dc.definition,
              N'(', N''), N')', N''), N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N'')
              COLLATE Latin1_General_100_BIN2 = N'N''Operating''' COLLATE Latin1_General_100_BIN2
    )
        THROW 64029, N'BMS cash-flow activity type refused: ActivityType default is drifted.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints AS cc
        WHERE cc.parent_object_id = OBJECT_ID(N'bms_web.CashFlowItems')
          AND cc.name = N'CK_bms_web_CashFlowItems_ActivityType'
          AND cc.is_disabled = 0
          AND cc.is_not_trusted = 0
          AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(cc.definition,
              N'[ActivityType]', N'ActivityType'), N'(', N''), N')', N''), N' ', N''),
              CHAR(9), N''), CHAR(10), N'')
              COLLATE Latin1_General_100_BIN2 =
              N'ActivityType=N''Operating''ORActivityType=N''Investing''ORActivityType=N''Financing''' COLLATE Latin1_General_100_BIN2
    )
        THROW 64030, N'BMS cash-flow activity type refused: ActivityType CHECK is drifted.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 40
    )
        INSERT INTO bms_web.schema_version (version_no, script_name)
        VALUES (40, N'040_cash_flow_activity_type.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
