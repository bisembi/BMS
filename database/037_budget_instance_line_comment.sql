-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    SQL037: durable comment for budget-instance lines.
    Exact SQL001-SQL036 provenance is required. The nullable comment is
    intentionally part of the line row so edits survive reloads and exports.
*/
IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS budget-line comment refused: run SQL037 outside an ambient transaction.', 16, 1);
    RETURN;
END;

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 63721, N'BMS budget-line comment refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 63722, N'BMS budget-line comment refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U') IS NULL
    THROW 63723, N'BMS budget-line comment refused: required managed tables are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no > 37
    )
        THROW 63724, N'BMS budget-line comment refused: a future schema version is registered.', 1;

    DECLARE @Predecessors TABLE
    (
        version_no int NOT NULL PRIMARY KEY,
        script_name nvarchar(200) NOT NULL
    );

    INSERT INTO @Predecessors (version_no, script_name)
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
        (27, N'027_accrual_red_zone_notifications.sql'),
        (28, N'028_cash_flow_accrual_settings_version.sql'),
        (29, N'029_expense_request_substitutions.sql'),
        (30, N'030_expense_request_task_scope_index.sql'),
        (31, N'031_expense_request_attachments.sql'),
        (32, N'032_budget_instance_draft.sql'),
        (33, N'033_budget_instance_monthly_period.sql'),
        (34, N'034_budget_cost_center_directory.sql'),
        (35, N'035_budget_instance_approval.sql'),
        (36, N'036_identity_reference_import_watermark.sql');

    IF
    (
        SELECT COUNT(*)
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no <> 37
    ) <> 36
       OR EXISTS
       (
           SELECT 1
           FROM @Predecessors AS expected
           LEFT JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK)
             ON actual.version_no = expected.version_no
           WHERE actual.version_no IS NULL
              OR actual.script_name COLLATE Latin1_General_100_BIN2 <>
                 expected.script_name COLLATE Latin1_General_100_BIN2
              OR DATALENGTH(actual.script_name) <> DATALENGTH(expected.script_name)
       )
        THROW 63725, N'BMS budget-line comment refused: exact SQL001-SQL036 predecessors are required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 37
          AND
          (
              script_name COLLATE Latin1_General_100_BIN2 <>
                  N'037_budget_instance_line_comment.sql'
              OR DATALENGTH(script_name) <>
                  DATALENGTH(N'037_budget_instance_line_comment.sql')
          )
    )
        THROW 63726, N'BMS budget-line comment refused: SQL037 belongs to another script.', 1;

    DECLARE @VersionRegistered bit = CASE WHEN EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 37
    ) THEN 1 ELSE 0 END;

    DECLARE @LineId int = OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U');

    IF @VersionRegistered = 0
       AND EXISTS
       (
           SELECT 1
           FROM sys.columns
           WHERE object_id = @LineId AND name = N'Comment'
       )
        THROW 63727, N'BMS budget-line comment refused: an unmanaged Comment column already exists.', 1;

    IF @VersionRegistered = 0
    BEGIN
        ALTER TABLE bms_web.BudgetInstanceLines
            ADD Comment nvarchar(2000) COLLATE Latin1_General_100_BIN2 NULL;
    END;

    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = @LineId) <> 7
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (N'Id', N'uniqueidentifier', 16, 0, 0, 0, CAST(NULL AS nvarchar(128))),
                   (N'BudgetInstanceId', N'uniqueidentifier', 16, 0, 0, 0, CAST(NULL AS nvarchar(128))),
                   (N'CashFlowItemId', N'uniqueidentifier', 16, 0, 0, 0, CAST(NULL AS nvarchar(128))),
                   (N'BudgetMonth', N'date', 3, 0, 10, 0, CAST(NULL AS nvarchar(128))),
                   (N'LimitAmountKzt', N'decimal', 9, 0, 18, 2, CAST(NULL AS nvarchar(128))),
                   (N'SortOrder', N'int', 4, 0, 10, 0, CAST(NULL AS nvarchar(128))),
                   (N'Comment', N'nvarchar', 4000, 1, 0, 0, N'Latin1_General_100_BIN2')
           ) AS expected(ColumnName, TypeName, MaxLength, IsNullable, PrecisionValue, ScaleValue, CollationName)
           LEFT JOIN sys.columns AS actual
             ON actual.object_id = @LineId
            AND actual.name = expected.ColumnName
           WHERE actual.object_id IS NULL
              OR TYPE_NAME(actual.user_type_id) <> expected.TypeName
              OR actual.max_length <> expected.MaxLength
              OR actual.is_nullable <> expected.IsNullable
              OR (expected.PrecisionValue <> 0 AND actual.precision <> expected.PrecisionValue)
              OR (expected.ScaleValue <> 0 AND actual.scale <> expected.ScaleValue)
              OR ISNULL(actual.collation_name, N'') <> ISNULL(expected.CollationName, N'')
              OR actual.is_identity <> 0 OR actual.is_computed <> 0
              OR actual.default_object_id <> 0 OR actual.rule_object_id <> 0
              OR actual.generated_always_type <> 0 OR actual.is_rowguidcol <> 0
              OR actual.is_filestream <> 0 OR actual.is_sparse <> 0
              OR actual.is_column_set <> 0 OR actual.encryption_type IS NOT NULL
              OR actual.is_hidden <> 0 OR actual.is_masked <> 0
       )
        THROW 63728, N'BMS budget-line comment refused: line column contract is drifted.', 1;

    IF @VersionRegistered = 0
        INSERT INTO bms_web.schema_version (version_no, script_name)
        VALUES (37, N'037_budget_instance_line_comment.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
