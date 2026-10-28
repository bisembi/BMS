-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    W611: version of the two BMS-owned accrual settings on a cash-flow article.
    Standalone after SQL001-SQL027. Existing articles receive a new technical
    version; no business settings, request snapshots, money or roles are changed.
    A pre-existing unregistered column or drifted registered shape is refused.
*/
IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS W611 refused: run SQL028 outside an ambient transaction.', 16, 1);
    RETURN;
END;
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 62401, N'BMS W611 refused: current database must be the explicitly owned BMS database.', 1;
IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 62402, N'BMS W611 refused: current database is not READ_WRITE.', 1;
IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.CashFlowItems', N'U') IS NULL
    THROW 62403, N'BMS W611 refused: required managed tables are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK) WHERE version_no > 28)
        THROW 62404, N'BMS W611 refused: a future schema version is registered.', 1;
    IF
    (
        SELECT COUNT(*)
        FROM
        (
            VALUES
                (1, N'001_bootstrap_bms_web.sql'),
                (2, N'002_budget_projection.sql'),
                (3, N'003_access_scope.sql'),
                (4, N'004_workflow_matrix.sql'),
                (5, N'005_expense_request_evidence.sql'),
                (6, N'006_expense_request_copy_source.sql'),
                (7, N'007_expense_request_line_order.sql'),
                (8, N'008_actor_idempotency_collation.sql'),
                (9, N'009_cash_flow_items_accrual_source.sql'),
                (10, N'010_expense_items_accrual_mapping.sql'),
                (11, N'011_expense_request_line_accrual_snapshot.sql'),
                (12, N'012_money_decimal18_scale2.sql'),
                (13, N'013_counterparties_reference.sql'),
                (14, N'014_contracts_reference.sql'),
                (15, N'015_employee_functional_roles.sql'),
                (16, N'016_currencies_and_daily_rates.sql'),
                (17, N'017_expense_request_currency_rate_snapshot.sql'),
                (18, N'018_expense_request_line_currency_conversion.sql'),
                (19, N'019_overpayment_deficit_storage.sql'),
                (20, N'020_treasury_payment_overpayment_storage.sql'),
                (21, N'021_budget_owner_cost_center_assignments.sql'),
                (22, N'022_expense_request_approval_route_snapshots.sql'),
                (23, N'023_employee_actor_mappings.sql'),
                (24, N'024_expense_request_workflow_activation.sql'),
                (25, N'025_expense_request_accrual_closing_cycles.sql'),
                (26, N'026_expense_request_closing_activation.sql'),
                (27, N'027_accrual_red_zone_notifications.sql')
        ) AS expected(version_no, script_name)
        INNER JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK)
          ON actual.version_no = expected.version_no
         AND actual.script_name COLLATE Latin1_General_100_BIN2 =
             expected.script_name COLLATE Latin1_General_100_BIN2
         AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
    ) <> 27
        THROW 62405, N'BMS W611 refused: exact SQL001-SQL027 predecessors are required.', 1;
    IF EXISTS
    (
        SELECT 1 FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 28 AND
        (
            script_name COLLATE Latin1_General_100_BIN2 <> N'028_cash_flow_accrual_settings_version.sql'
            OR DATALENGTH(script_name) <> DATALENGTH(N'028_cash_flow_accrual_settings_version.sql')
        )
    )
        THROW 62406, N'BMS W611 refused: SQL028 belongs to another script.', 1;

    DECLARE @TableId int = OBJECT_ID(N'bms_web.CashFlowItems');
    DECLARE @VersionRegistered bit = CASE WHEN EXISTS
        (SELECT 1 FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK) WHERE version_no = 28)
        THEN 1 ELSE 0 END;
    DECLARE @ColumnId int = COLUMNPROPERTY(@TableId, N'AccrualSettingsVersion', 'ColumnId');
    IF (@VersionRegistered = 0 AND @ColumnId IS NOT NULL)
       OR (@VersionRegistered = 1 AND @ColumnId IS NULL)
        THROW 62407, N'BMS W611 refused: version registration and column ownership disagree.', 1;
    IF EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = @TableId AND is_ms_shipped = 0)
        THROW 62408, N'BMS W611 refused: CashFlowItems has an unmanaged DML trigger.', 1;

    DECLARE @ArticleLock uniqueidentifier;
    SELECT TOP (1) @ArticleLock = Id FROM bms_web.CashFlowItems WITH (TABLOCKX, HOLDLOCK);
    IF @VersionRegistered = 0
    BEGIN
        IF OBJECT_ID(N'bms_web.DF_CashFlowItems_AccrualSettingsVersion') IS NOT NULL
            THROW 62409, N'BMS W611 refused: the default name is already owned.', 1;
        EXEC sys.sp_executesql N'
            ALTER TABLE bms_web.CashFlowItems
            ADD AccrualSettingsVersion uniqueidentifier NOT NULL
                CONSTRAINT DF_CashFlowItems_AccrualSettingsVersion DEFAULT (NEWID()) WITH VALUES;';
    END;

    SET @ColumnId = COLUMNPROPERTY(@TableId, N'AccrualSettingsVersion', 'ColumnId');
    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = @TableId) <> 9
        THROW 62410, N'BMS W611 refused: CashFlowItems has an unexpected column set.', 1;
    IF EXISTS
    (
        SELECT 1 FROM
        (VALUES
            (N'Id', N'uniqueidentifier', 16, 0),
            (N'Code', N'nvarchar', 40, 0),
            (N'Name', N'nvarchar', 300, 0),
            (N'ParentId', N'uniqueidentifier', 16, 1),
            (N'IsActive', N'bit', 1, 0),
            (N'IsGroup', N'bit', 1, 0),
            (N'FormsManagerialAccrual', N'bit', 1, 0),
            (N'AccrualExpenseItemId', N'uniqueidentifier', 16, 1),
            (N'AccrualSettingsVersion', N'uniqueidentifier', 16, 0)
        ) AS expected(Name, TypeName, MaxLength, IsNullable)
        LEFT JOIN sys.columns AS c ON c.object_id = @TableId AND c.name = expected.Name
        WHERE c.column_id IS NULL OR TYPE_NAME(c.user_type_id) <> expected.TypeName
           OR c.max_length <> expected.MaxLength OR c.is_nullable <> expected.IsNullable
           OR c.is_computed <> 0 OR c.rule_object_id <> 0 OR c.generated_always_type <> 0
           OR c.is_sparse <> 0 OR c.is_column_set <> 0 OR c.is_hidden <> 0
           OR c.encryption_type IS NOT NULL OR c.is_masked <> 0
           OR (expected.Name <> N'AccrualSettingsVersion' AND c.default_object_id <> 0)
    )
        THROW 62411, N'BMS W611 refused: CashFlowItems column metadata differs.', 1;
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.default_constraints AS dc
        WHERE dc.parent_object_id = @TableId AND dc.parent_column_id = @ColumnId
          AND dc.name COLLATE Latin1_General_100_BIN2 = N'DF_CashFlowItems_AccrualSettingsVersion'
          AND dc.is_system_named = 0
          AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(dc.definition,
              N'(', N''), N')', N''), N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N''))
              COLLATE Latin1_General_100_BIN2 = N'NEWID'
    )
        THROW 62412, N'BMS W611 refused: the version default is not exactly NEWID().', 1;
    IF EXISTS
    (
        SELECT 1 FROM sys.index_columns WHERE object_id = @TableId AND column_id = @ColumnId
    ) OR EXISTS
    (
        SELECT 1 FROM sys.foreign_key_columns
        WHERE (parent_object_id = @TableId AND parent_column_id = @ColumnId)
           OR (referenced_object_id = @TableId AND referenced_column_id = @ColumnId)
    )
        THROW 62413, N'BMS W611 refused: version has unexpected index or foreign-key semantics.', 1;
    EXEC sys.sp_executesql N'
        IF EXISTS (SELECT 1 FROM bms_web.CashFlowItems
                   WHERE AccrualSettingsVersion = CONVERT(uniqueidentifier, ''00000000-0000-0000-0000-000000000000''))
            THROW 62414, N''BMS W611 refused: an article has an empty settings version.'', 1;';

    IF @VersionRegistered = 0
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (28, N'028_cash_flow_accrual_settings_version.sql');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
