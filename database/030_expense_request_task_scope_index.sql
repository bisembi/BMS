-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    W629: stable organization root for the "My tasks" expense-request read.

    Workflow commands update Status, UpdatedAtUtc, ConcurrencyStamp and the
    active route coordinates. None of those columns belongs to this index, so
    a command does not need the index lock held by a Serializable task reader.
    OrganizationId narrows the common filtered inbox before the task predicate;
    Id gives a deterministic stable key and is the clustered lookup target.
*/
IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS W629 refused: run SQL030 outside an ambient transaction.', 16, 1);
    RETURN;
END;
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 62601, N'BMS W629 refused: current database must be the explicitly owned BMS database.', 1;
IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 62602, N'BMS W629 refused: current database is not READ_WRITE.', 1;
IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.ExpenseRequests', N'U') IS NULL
    THROW 62603, N'BMS W629 refused: required managed tables are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no > 30
    )
        THROW 62604, N'BMS W629 refused: a future schema version is registered.', 1;

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
                (27, N'027_accrual_red_zone_notifications.sql'),
                (28, N'028_cash_flow_accrual_settings_version.sql'),
                (29, N'029_expense_request_substitutions.sql')
        ) AS expected(version_no, script_name)
        INNER JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK)
          ON actual.version_no = expected.version_no
         AND actual.script_name COLLATE Latin1_General_100_BIN2 =
             expected.script_name COLLATE Latin1_General_100_BIN2
         AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
    ) <> 29
        THROW 62605, N'BMS W629 refused: exact SQL001-SQL029 predecessors are required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 30
          AND
          (
              script_name COLLATE Latin1_General_100_BIN2 <>
                  N'030_expense_request_task_scope_index.sql'
              OR DATALENGTH(script_name) <>
                  DATALENGTH(N'030_expense_request_task_scope_index.sql')
          )
    )
        THROW 62606, N'BMS W629 refused: SQL030 belongs to another script.', 1;

    DECLARE @VersionRegistered bit = CASE WHEN EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 30
    ) THEN 1 ELSE 0 END;
    DECLARE @TableId int = OBJECT_ID(N'bms_web.ExpenseRequests');
    DECLARE @IndexName sysname =
        N'IX_bms_web_ExpenseRequests_OrganizationId_Id';
    DECLARE @IndexId int =
        INDEXPROPERTY(@TableId, @IndexName, 'IndexId');

    IF (@VersionRegistered = 0 AND @IndexId IS NOT NULL)
       OR (@VersionRegistered = 1 AND @IndexId IS NULL)
        THROW 62607, N'BMS W629 refused: version registration and index ownership disagree.', 1;

    DECLARE @RequestLock uniqueidentifier;
    SELECT TOP (1) @RequestLock = Id
    FROM bms_web.ExpenseRequests WITH (TABLOCKX, HOLDLOCK)
    ORDER BY Id;

    IF @VersionRegistered = 0
    BEGIN
        CREATE NONCLUSTERED INDEX
            IX_bms_web_ExpenseRequests_OrganizationId_Id
            ON bms_web.ExpenseRequests (OrganizationId ASC, Id ASC);
        SET @IndexId = INDEXPROPERTY(@TableId, @IndexName, 'IndexId');
    END;

    IF @IndexId IS NULL
        THROW 62608, N'BMS W629 refused: task-scope index was not created.', 1;
    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = @TableId
          AND index_id = @IndexId
          AND name = @IndexName
          AND type = 2
          AND is_primary_key = 0
          AND is_unique = 0
          AND is_unique_constraint = 0
          AND is_disabled = 0
          AND is_hypothetical = 0
          AND has_filter = 0
          AND filter_definition IS NULL
          AND ignore_dup_key = 0
          AND is_padded = 0
          AND allow_row_locks = 1
          AND allow_page_locks = 1
    )
        THROW 62609, N'BMS W629 refused: task-scope index metadata differs.', 1;
    IF
    (
        SELECT STRING_AGG(
            CONCAT(column_metadata.name, N':', index_metadata.is_descending_key),
            N',') WITHIN GROUP (ORDER BY index_metadata.key_ordinal)
        FROM sys.index_columns AS index_metadata
        INNER JOIN sys.columns AS column_metadata
          ON column_metadata.object_id = index_metadata.object_id
         AND column_metadata.column_id = index_metadata.column_id
        WHERE index_metadata.object_id = @TableId
          AND index_metadata.index_id = @IndexId
          AND index_metadata.key_ordinal > 0
    ) COLLATE Latin1_General_100_BIN2 <> N'OrganizationId:0,Id:0'
       OR EXISTS
       (
           SELECT 1
           FROM sys.index_columns
           WHERE object_id = @TableId
             AND index_id = @IndexId
             AND is_included_column = 1
       )
        THROW 62610, N'BMS W629 refused: task-scope index key shape differs.', 1;

    IF @VersionRegistered = 0
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (30, N'030_expense_request_task_scope_index.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
