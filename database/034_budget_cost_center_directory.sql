-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    SQL034: budget-only CFO settings, explicit membership, monotonic-counter storage,
    and permanent prefix ownership. No real people, abbreviations or ordinals are seeded.
    Missing counter is not zero. Legacy documents and expense-request authority stay intact.
    Full Unicode/NFC/case validation and transactional allocation belong to the writer.
*/
IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS budget directory refused: run SQL034 outside an ambient transaction.', 16, 1);
    RETURN;
END;
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 63401, N'BMS budget directory refused: current database must be the explicitly owned BMS database.', 1;
IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 63402, N'BMS budget directory refused: current database is not READ_WRITE.', 1;
IF SCHEMA_ID(N'bms_web') IS NULL OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.CostCenters', N'U') IS NULL OR OBJECT_ID(N'bms_web.Employees', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.BudgetInstances', N'U') IS NULL OR OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U') IS NULL
    THROW 63403, N'BMS budget directory refused: required managed tables are absent.', 1;
BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK) WHERE version_no > 34)
        THROW 63404, N'BMS budget directory refused: a future schema version is registered.', 1;
    DECLARE @Predecessors TABLE (version_no int NOT NULL PRIMARY KEY, script_name nvarchar(200) NOT NULL);
    INSERT INTO @Predecessors VALUES
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
        (33, N'033_budget_instance_monthly_period.sql');
    IF (SELECT COUNT(*) FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK) WHERE version_no <> 34) <> 33
       OR EXISTS
       (
           SELECT 1 FROM @Predecessors AS expected
           LEFT JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK) ON actual.version_no = expected.version_no
           WHERE actual.version_no IS NULL
              OR actual.script_name COLLATE Latin1_General_100_BIN2 <> expected.script_name COLLATE Latin1_General_100_BIN2
              OR DATALENGTH(actual.script_name) <> DATALENGTH(expected.script_name)
       )
        THROW 63405, N'BMS budget directory refused: exact SQL001-SQL033 predecessors are required.', 1;
    IF EXISTS
    (
        SELECT 1 FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK) WHERE version_no = 34 AND
        (script_name COLLATE Latin1_General_100_BIN2 <> N'034_budget_cost_center_directory.sql'
         OR DATALENGTH(script_name) <> DATALENGTH(N'034_budget_cost_center_directory.sql'))
    )
        THROW 63406, N'BMS budget directory refused: SQL034 belongs to another script.', 1;
    DECLARE @VersionRegistered bit = CASE WHEN EXISTS
        (SELECT 1 FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK) WHERE version_no = 34) THEN 1 ELSE 0 END;
    /* SQL034 PREDECESSOR SHAPE BEGIN */
    -- Retain SQL033's complete read-only monthly repeat checks, not its DDL or registration.
    -- The immutable predecessor file stays unchanged; this invocation owns different temp names.
    IF OBJECT_ID(N'tempdb..#BD034PreHeaderYear') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BD034PreHeaderMonth') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BD034PreHeaderStatus') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BD034PreLineMonth') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BD034PreLineLimit') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BD034PreLineSort') IS NOT NULL
        THROW 63411, N'BMS budget directory refused: predecessor canonical temp name is already in use.', 1;
    DECLARE @BD034PredecessorVersionRegistered bit = 1;
    DECLARE @HeaderId int = OBJECT_ID(N'bms_web.BudgetInstances');
    DECLARE @LineId int = OBJECT_ID(N'bms_web.BudgetInstanceLines');
    /* ---- BudgetInstances structural drift (create path and repeat apply) ---- */
    IF EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = @HeaderId AND is_ms_shipped = 0)
        THROW 63210, N'BMS budget-instance draft refused: BudgetInstances has an unmanaged trigger.', 1;
    IF EXISTS
    (
        SELECT 1 FROM sys.tables
        WHERE object_id = @HeaderId AND (temporal_type <> 0 OR is_memory_optimized <> 0)
    )
        THROW 63210, N'BMS budget-instance draft refused: BudgetInstances has unsupported behavior.', 1;
    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = @HeaderId) <> 9
        THROW 63211, N'BMS budget-instance draft refused: BudgetInstances column count differs.', 1;
    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'Id', N'uniqueidentifier', 16, 0, 0, 0, NULL),
                (N'Number', N'nvarchar', 80, 0, 0, 0, N'Latin1_General_100_BIN2'),
                (N'CostCenterId', N'uniqueidentifier', 16, 0, 0, 0, NULL),
                (N'ValidFrom', N'date', 3, 0, 10, 0, NULL),
                (N'ValidTo', N'date', 3, 0, 10, 0, NULL),
                (N'Status', N'nvarchar', 80, 0, 0, 0, N'Latin1_General_100_BIN2'),
                (N'OwnerEmployeeId', N'uniqueidentifier', 16, 0, 0, 0, NULL),
                (N'OwnerExternalId', N'nvarchar', 512, 0, 0, 0, N'Latin1_General_100_BIN2'),
                (N'ConcurrencyStamp', N'uniqueidentifier', 16, 0, 0, 0, NULL)
        ) AS expected(Name, TypeName, MaxLength, IsNullable, [Precision], Scale, CollationName)
        LEFT JOIN sys.columns AS actual
          ON actual.object_id = @HeaderId AND actual.name = expected.Name
        WHERE actual.column_id IS NULL
           OR TYPE_NAME(actual.user_type_id) <> expected.TypeName
           OR actual.max_length <> expected.MaxLength
           OR actual.is_nullable <> expected.IsNullable
           OR (expected.[Precision] <> 0 AND actual.[precision] <> expected.[Precision])
           OR (expected.Scale <> 0 AND actual.scale <> expected.Scale)
           OR ISNULL(actual.collation_name, N'') <> ISNULL(expected.CollationName, N'')
           OR actual.is_identity <> 0 OR actual.is_computed <> 0
           OR actual.default_object_id <> 0 OR actual.rule_object_id <> 0
           OR actual.generated_always_type <> 0 OR actual.is_rowguidcol <> 0
           OR actual.is_filestream <> 0 OR actual.is_sparse <> 0
           OR actual.is_column_set <> 0 OR actual.encryption_type IS NOT NULL
           OR actual.is_hidden <> 0 OR actual.is_masked <> 0
    )
        THROW 63212, N'BMS budget-instance draft refused: BudgetInstances column metadata differs.', 1;

    IF (SELECT COUNT(*) FROM sys.indexes WHERE object_id = @HeaderId AND index_id > 0) <> 4
        THROW 63213, N'BMS budget-instance draft refused: BudgetInstances index count differs.', 1;
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = @HeaderId
          AND name = N'PK_bms_web_BudgetInstances'
          AND type = 1 AND is_primary_key = 1 AND is_unique = 1
          AND is_unique_constraint = 0 AND is_disabled = 0 AND is_hypothetical = 0
          AND has_filter = 0 AND filter_definition IS NULL AND ignore_dup_key = 0
    )
        THROW 63214, N'BMS budget-instance draft refused: BudgetInstances primary key differs.', 1;
    IF
    (
        SELECT STRING_AGG(c.name, N',') WITHIN GROUP (ORDER BY ic.key_ordinal)
        FROM sys.indexes AS i
        INNER JOIN sys.index_columns AS ic
          ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        INNER JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = @HeaderId
          AND i.name = N'PK_bms_web_BudgetInstances'
          AND ic.key_ordinal > 0
    ) COLLATE Latin1_General_100_BIN2 <> N'Id'
        THROW 63214, N'BMS budget-instance draft refused: BudgetInstances primary key shape differs.', 1;
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = @HeaderId
          AND name = N'UX_bms_web_BudgetInstances_Number'
          AND type = 2 AND is_primary_key = 0 AND is_unique = 1
          AND is_unique_constraint = 1 AND is_disabled = 0 AND is_hypothetical = 0
          AND has_filter = 0 AND filter_definition IS NULL AND ignore_dup_key = 0
    )
        THROW 63215, N'BMS budget-instance draft refused: BudgetInstances number uniqueness differs.', 1;
    IF
    (
        SELECT STRING_AGG(c.name, N',') WITHIN GROUP (ORDER BY ic.key_ordinal)
        FROM sys.indexes AS i
        INNER JOIN sys.index_columns AS ic
          ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        INNER JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = @HeaderId
          AND i.name = N'UX_bms_web_BudgetInstances_Number'
          AND ic.key_ordinal > 0
    ) COLLATE Latin1_General_100_BIN2 <> N'Number'
        THROW 63215, N'BMS budget-instance draft refused: BudgetInstances number uniqueness shape differs.', 1;
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = @HeaderId
          AND name = N'IX_bms_web_BudgetInstances_Owner_Status_Year'
          AND type = 2 AND is_primary_key = 0 AND is_unique = 0
          AND is_unique_constraint = 0 AND is_disabled = 0 AND is_hypothetical = 0
          AND has_filter = 0 AND filter_definition IS NULL AND ignore_dup_key = 0
    )
       OR
       (
           SELECT STRING_AGG(CONCAT(c.name, N':', ic.is_descending_key), N',')
               WITHIN GROUP (ORDER BY ic.key_ordinal)
           FROM sys.indexes AS i
           INNER JOIN sys.index_columns AS ic
             ON ic.object_id = i.object_id AND ic.index_id = i.index_id
           INNER JOIN sys.columns AS c
             ON c.object_id = ic.object_id AND c.column_id = ic.column_id
           WHERE i.object_id = @HeaderId
             AND i.name = N'IX_bms_web_BudgetInstances_Owner_Status_Year'
             AND ic.key_ordinal > 0
       ) COLLATE Latin1_General_100_BIN2 <> N'OwnerEmployeeId:0,Status:0,ValidFrom:0'
        THROW 63216, N'BMS budget-instance draft refused: BudgetInstances owner index differs.', 1;
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = @HeaderId
          AND name = N'IX_bms_web_BudgetInstances_CostCenter_Year_Status'
          AND type = 2 AND is_primary_key = 0 AND is_unique = 0
          AND is_unique_constraint = 0 AND is_disabled = 0 AND is_hypothetical = 0
          AND has_filter = 0 AND filter_definition IS NULL AND ignore_dup_key = 0
    )
       OR
       (
           SELECT STRING_AGG(CONCAT(c.name, N':', ic.is_descending_key), N',')
               WITHIN GROUP (ORDER BY ic.key_ordinal)
           FROM sys.indexes AS i
           INNER JOIN sys.index_columns AS ic
             ON ic.object_id = i.object_id AND ic.index_id = i.index_id
           INNER JOIN sys.columns AS c
             ON c.object_id = ic.object_id AND c.column_id = ic.column_id
           WHERE i.object_id = @HeaderId
             AND i.name = N'IX_bms_web_BudgetInstances_CostCenter_Year_Status'
             AND ic.key_ordinal > 0
       ) COLLATE Latin1_General_100_BIN2 <> N'CostCenterId:0,ValidFrom:0,Status:0'
        THROW 63217, N'BMS budget-instance draft refused: BudgetInstances cost-center index differs.', 1;
    IF EXISTS
    (
        SELECT 1
        FROM sys.indexes AS i
        INNER JOIN sys.index_columns AS ic
          ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        WHERE i.object_id = @HeaderId
          AND i.name IN
          (
              N'PK_bms_web_BudgetInstances',
              N'UX_bms_web_BudgetInstances_Number',
              N'IX_bms_web_BudgetInstances_Owner_Status_Year',
              N'IX_bms_web_BudgetInstances_CostCenter_Year_Status'
          )
          AND (ic.is_included_column <> 0 OR ic.is_descending_key <> 0)
    )
        THROW 63217, N'BMS budget-instance draft refused: BudgetInstances index columns differ.', 1;

    IF (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = @HeaderId) <> 2
        THROW 63218, N'BMS budget-instance draft refused: BudgetInstances check count differs.', 1;

    CREATE TABLE #BD034PreHeaderYear
    (
        ValidFrom date, ValidTo date,
        CHECK (
            DAY(ValidFrom) = 1 AND MONTH(ValidFrom) = 1
            AND DAY(ValidTo) = 31 AND MONTH(ValidTo) = 12
            AND YEAR(ValidFrom) = YEAR(ValidTo))
    );
    CREATE TABLE #BD034PreHeaderMonth
    (
        ValidFrom date, ValidTo date,
        CHECK (DAY(ValidFrom) = 1 AND ValidTo = EOMONTH(ValidTo) AND ValidFrom <= ValidTo)
    );
    CREATE TABLE #BD034PreHeaderStatus
    (
        Status nvarchar(40),
        CHECK (Status = N'Draft')
    );
    DECLARE @ExpectedHeaderChecks TABLE
        (ConstraintName sysname NOT NULL, ParentColumnId int NOT NULL,
         CanonicalDefinition nvarchar(max) NULL);
    INSERT INTO @ExpectedHeaderChecks
    SELECT expected.ConstraintName,
           CASE WHEN expected.ParentColumnName IS NULL THEN 0
                ELSE ISNULL(COLUMNPROPERTY(
                    @HeaderId, expected.ParentColumnName, N'ColumnId'), -1) END,
           actual.definition
    FROM (VALUES
        (CASE WHEN @BD034PredecessorVersionRegistered = 0 THEN N'CK_bms_web_BudgetInstances_year_bounds'
              ELSE N'CK_bms_web_BudgetInstances_month_bounds' END,
         CAST(NULL AS sysname),
         CASE WHEN @BD034PredecessorVersionRegistered = 0 THEN N'tempdb..#BD034PreHeaderYear'
              ELSE N'tempdb..#BD034PreHeaderMonth' END),
        (N'CK_bms_web_BudgetInstances_status_draft_slice',
         N'Status', N'tempdb..#BD034PreHeaderStatus')
    ) AS expected(ConstraintName, ParentColumnName, TemporaryTable)
    LEFT JOIN tempdb.sys.check_constraints AS actual
      ON actual.parent_object_id = OBJECT_ID(expected.TemporaryTable);
    IF EXISTS
    (
        SELECT 1 FROM @ExpectedHeaderChecks AS expected
        LEFT JOIN sys.check_constraints AS actual
          ON actual.parent_object_id = @HeaderId
         AND actual.name = expected.ConstraintName
        WHERE actual.object_id IS NULL OR actual.definition IS NULL
           OR expected.CanonicalDefinition IS NULL
           OR actual.is_disabled <> 0 OR actual.is_not_trusted <> 0
           OR actual.is_not_for_replication <> 0 OR actual.is_system_named <> 0
           OR actual.parent_column_id <> expected.ParentColumnId
           OR
           (SELECT COUNT(DISTINCT normalized_definition COLLATE Latin1_General_100_BIN2)
            FROM (VALUES
              (REPLACE(REPLACE(REPLACE(REPLACE(actual.definition,
                N' ', N''), NCHAR(13), N''), NCHAR(10), N''), NCHAR(9), N'')),
              (REPLACE(REPLACE(REPLACE(REPLACE(expected.CanonicalDefinition,
                N' ', N''), NCHAR(13), N''), NCHAR(10), N''), NCHAR(9), N''))
            ) AS definitions(normalized_definition)) <> 1
    )
        THROW 63218, N'BMS budget-instance draft refused: BudgetInstances check shape differs.', 1;

    IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = @HeaderId) <> 2
        THROW 63219, N'BMS budget-instance draft refused: BudgetInstances foreign key count differs.', 1;
    IF EXISTS
    (
        SELECT 1
        FROM (VALUES
            (N'FK_bms_web_BudgetInstances_CostCenter', N'CostCenterId', N'Id',
             OBJECT_ID(N'bms_web.CostCenters'), 0),
            (N'FK_bms_web_BudgetInstances_OwnerEmployee', N'OwnerEmployeeId', N'Id',
             OBJECT_ID(N'bms_web.Employees'), 0)
        ) AS expected(ConstraintName, ParentColumnName, ReferencedColumnName,
                      ReferencedObjectId, DeleteAction)
        LEFT JOIN sys.foreign_keys AS actual_fk
          ON actual_fk.parent_object_id = @HeaderId
         AND actual_fk.name = expected.ConstraintName
        LEFT JOIN sys.foreign_key_columns AS actual_fkc
          ON actual_fkc.constraint_object_id = actual_fk.object_id
         AND actual_fkc.constraint_column_id = 1
        LEFT JOIN sys.columns AS parent_column
          ON parent_column.object_id = actual_fkc.parent_object_id
         AND parent_column.column_id = actual_fkc.parent_column_id
        LEFT JOIN sys.columns AS referenced_column
          ON referenced_column.object_id = actual_fkc.referenced_object_id
         AND referenced_column.column_id = actual_fkc.referenced_column_id
        WHERE actual_fk.object_id IS NULL
           OR actual_fk.referenced_object_id <> expected.ReferencedObjectId
           OR actual_fk.is_disabled <> 0 OR actual_fk.is_not_trusted <> 0
           OR actual_fk.is_not_for_replication <> 0 OR actual_fk.is_system_named <> 0
           OR actual_fk.delete_referential_action <> expected.DeleteAction
           OR actual_fk.update_referential_action <> 0
           OR parent_column.name IS NULL
           OR parent_column.name <> expected.ParentColumnName
           OR referenced_column.name IS NULL
           OR referenced_column.name <> expected.ReferencedColumnName
           OR (SELECT COUNT(*) FROM sys.foreign_key_columns
               WHERE constraint_object_id = actual_fk.object_id) <> 1
    )
        THROW 63219, N'BMS budget-instance draft refused: BudgetInstances foreign keys differ.', 1;

    /* ---- BudgetInstanceLines structural drift ---- */
    IF EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = @LineId AND is_ms_shipped = 0)
        THROW 63220, N'BMS budget-instance draft refused: BudgetInstanceLines has an unmanaged trigger.', 1;
    IF EXISTS
    (
        SELECT 1 FROM sys.tables
        WHERE object_id = @LineId AND (temporal_type <> 0 OR is_memory_optimized <> 0)
    )
        THROW 63220, N'BMS budget-instance draft refused: BudgetInstanceLines has unsupported behavior.', 1;
    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = @LineId) <> 6
        THROW 63221, N'BMS budget-instance draft refused: BudgetInstanceLines column count differs.', 1;
    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'Id', N'uniqueidentifier', 16, 0, 0, 0, NULL),
                (N'BudgetInstanceId', N'uniqueidentifier', 16, 0, 0, 0, NULL),
                (N'CashFlowItemId', N'uniqueidentifier', 16, 0, 0, 0, NULL),
                (N'BudgetMonth', N'date', 3, 0, 10, 0, NULL),
                (N'LimitAmountKzt', N'decimal', 9, 0, 18, 2, NULL),
                (N'SortOrder', N'int', 4, 0, 10, 0, NULL)
        ) AS expected(Name, TypeName, MaxLength, IsNullable, [Precision], Scale, CollationName)
        LEFT JOIN sys.columns AS actual
          ON actual.object_id = @LineId AND actual.name = expected.Name
        WHERE actual.column_id IS NULL
           OR TYPE_NAME(actual.user_type_id) <> expected.TypeName
           OR actual.max_length <> expected.MaxLength
           OR actual.is_nullable <> expected.IsNullable
           OR (expected.[Precision] <> 0 AND actual.[precision] <> expected.[Precision])
           OR (expected.Scale <> 0 AND actual.scale <> expected.Scale)
           OR ISNULL(actual.collation_name, N'') <> ISNULL(expected.CollationName, N'')
           OR actual.is_identity <> 0 OR actual.is_computed <> 0
           OR actual.default_object_id <> 0 OR actual.rule_object_id <> 0
           OR actual.generated_always_type <> 0 OR actual.is_rowguidcol <> 0
           OR actual.is_filestream <> 0 OR actual.is_sparse <> 0
           OR actual.is_column_set <> 0 OR actual.encryption_type IS NOT NULL
           OR actual.is_hidden <> 0 OR actual.is_masked <> 0
    )
        THROW 63222, N'BMS budget-instance draft refused: BudgetInstanceLines column metadata differs.', 1;

    IF (SELECT COUNT(*) FROM sys.indexes WHERE object_id = @LineId AND index_id > 0) <> 2
        THROW 63223, N'BMS budget-instance draft refused: BudgetInstanceLines index count differs.', 1;
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = @LineId
          AND name = N'PK_bms_web_BudgetInstanceLines'
          AND type = 1 AND is_primary_key = 1 AND is_unique = 1
          AND is_unique_constraint = 0 AND is_disabled = 0 AND is_hypothetical = 0
          AND has_filter = 0 AND filter_definition IS NULL AND ignore_dup_key = 0
    )
        THROW 63224, N'BMS budget-instance draft refused: BudgetInstanceLines primary key differs.', 1;
    IF
    (
        SELECT STRING_AGG(c.name, N',') WITHIN GROUP (ORDER BY ic.key_ordinal)
        FROM sys.indexes AS i
        INNER JOIN sys.index_columns AS ic
          ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        INNER JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = @LineId
          AND i.name = N'PK_bms_web_BudgetInstanceLines'
          AND ic.key_ordinal > 0
    ) COLLATE Latin1_General_100_BIN2 <> N'Id'
        THROW 63224, N'BMS budget-instance draft refused: BudgetInstanceLines primary key shape differs.', 1;
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = @LineId
          AND name = N'UX_bms_web_BudgetInstanceLines_Key'
          AND type = 2 AND is_primary_key = 0 AND is_unique = 1
          AND is_unique_constraint = 1 AND is_disabled = 0 AND is_hypothetical = 0
          AND has_filter = 0 AND filter_definition IS NULL AND ignore_dup_key = 0
    )
       OR
       (
           SELECT STRING_AGG(CONCAT(c.name, N':', ic.is_descending_key), N',')
               WITHIN GROUP (ORDER BY ic.key_ordinal)
           FROM sys.indexes AS i
           INNER JOIN sys.index_columns AS ic
             ON ic.object_id = i.object_id AND ic.index_id = i.index_id
           INNER JOIN sys.columns AS c
             ON c.object_id = ic.object_id AND c.column_id = ic.column_id
           WHERE i.object_id = @LineId
             AND i.name = N'UX_bms_web_BudgetInstanceLines_Key'
             AND ic.key_ordinal > 0
       ) COLLATE Latin1_General_100_BIN2 <> N'BudgetInstanceId:0,CashFlowItemId:0,BudgetMonth:0'
        THROW 63225, N'BMS budget-instance draft refused: BudgetInstanceLines key uniqueness differs.', 1;
    IF EXISTS
    (
        SELECT 1
        FROM sys.indexes AS i
        INNER JOIN sys.index_columns AS ic
          ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        WHERE i.object_id = @LineId
          AND i.name IN
          (
              N'PK_bms_web_BudgetInstanceLines',
              N'UX_bms_web_BudgetInstanceLines_Key'
          )
          AND (ic.is_included_column <> 0 OR ic.is_descending_key <> 0)
    )
        THROW 63225, N'BMS budget-instance draft refused: BudgetInstanceLines index columns differ.', 1;

    IF (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = @LineId) <> 3
        THROW 63226, N'BMS budget-instance draft refused: BudgetInstanceLines check count differs.', 1;

    CREATE TABLE #BD034PreLineMonth (BudgetMonth date, CHECK (DAY(BudgetMonth) = 1));
    CREATE TABLE #BD034PreLineLimit (LimitAmountKzt decimal(18, 2), CHECK (LimitAmountKzt >= 0));
    CREATE TABLE #BD034PreLineSort (SortOrder int, CHECK (SortOrder >= 0));
    DECLARE @ExpectedLineChecks TABLE
        (ConstraintName sysname NOT NULL, ParentColumnId int NOT NULL,
         CanonicalDefinition nvarchar(max) NULL);
    INSERT INTO @ExpectedLineChecks
    SELECT expected.ConstraintName,
           ISNULL(COLUMNPROPERTY(
               @LineId, expected.ParentColumnName, N'ColumnId'), -1),
           actual.definition
    FROM (VALUES
        (N'CK_bms_web_BudgetInstanceLines_month_start',
         N'BudgetMonth', N'tempdb..#BD034PreLineMonth'),
        (N'CK_bms_web_BudgetInstanceLines_limit',
         N'LimitAmountKzt', N'tempdb..#BD034PreLineLimit'),
        (N'CK_bms_web_BudgetInstanceLines_sort',
         N'SortOrder', N'tempdb..#BD034PreLineSort')
    ) AS expected(ConstraintName, ParentColumnName, TemporaryTable)
    LEFT JOIN tempdb.sys.check_constraints AS actual
      ON actual.parent_object_id = OBJECT_ID(expected.TemporaryTable);
    IF EXISTS
    (
        SELECT 1 FROM @ExpectedLineChecks AS expected
        LEFT JOIN sys.check_constraints AS actual
          ON actual.parent_object_id = @LineId
         AND actual.name = expected.ConstraintName
        WHERE actual.object_id IS NULL OR actual.definition IS NULL
           OR expected.CanonicalDefinition IS NULL
           OR actual.is_disabled <> 0 OR actual.is_not_trusted <> 0
           OR actual.is_not_for_replication <> 0 OR actual.is_system_named <> 0
           OR actual.parent_column_id <> expected.ParentColumnId
           OR
           (SELECT COUNT(DISTINCT normalized_definition COLLATE Latin1_General_100_BIN2)
            FROM (VALUES
              (REPLACE(REPLACE(REPLACE(REPLACE(actual.definition,
                N' ', N''), NCHAR(13), N''), NCHAR(10), N''), NCHAR(9), N'')),
              (REPLACE(REPLACE(REPLACE(REPLACE(expected.CanonicalDefinition,
                N' ', N''), NCHAR(13), N''), NCHAR(10), N''), NCHAR(9), N''))
            ) AS definitions(normalized_definition)) <> 1
    )
        THROW 63226, N'BMS budget-instance draft refused: BudgetInstanceLines check shape differs.', 1;

    IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = @LineId) <> 2
        THROW 63227, N'BMS budget-instance draft refused: BudgetInstanceLines foreign key count differs.', 1;
    IF EXISTS
    (
        SELECT 1
        FROM (VALUES
            (N'FK_bms_web_BudgetInstanceLines_Instance', N'BudgetInstanceId', N'Id',
             OBJECT_ID(N'bms_web.BudgetInstances'), 1),
            (N'FK_bms_web_BudgetInstanceLines_CashFlowItem', N'CashFlowItemId', N'Id',
             OBJECT_ID(N'bms_web.CashFlowItems'), 0)
        ) AS expected(ConstraintName, ParentColumnName, ReferencedColumnName,
                      ReferencedObjectId, DeleteAction)
        LEFT JOIN sys.foreign_keys AS actual_fk
          ON actual_fk.parent_object_id = @LineId
         AND actual_fk.name = expected.ConstraintName
        LEFT JOIN sys.foreign_key_columns AS actual_fkc
          ON actual_fkc.constraint_object_id = actual_fk.object_id
         AND actual_fkc.constraint_column_id = 1
        LEFT JOIN sys.columns AS parent_column
          ON parent_column.object_id = actual_fkc.parent_object_id
         AND parent_column.column_id = actual_fkc.parent_column_id
        LEFT JOIN sys.columns AS referenced_column
          ON referenced_column.object_id = actual_fkc.referenced_object_id
         AND referenced_column.column_id = actual_fkc.referenced_column_id
        WHERE actual_fk.object_id IS NULL
           OR actual_fk.referenced_object_id <> expected.ReferencedObjectId
           OR actual_fk.is_disabled <> 0 OR actual_fk.is_not_trusted <> 0
           OR actual_fk.is_not_for_replication <> 0 OR actual_fk.is_system_named <> 0
           OR actual_fk.delete_referential_action <> expected.DeleteAction
           OR actual_fk.update_referential_action <> 0
           OR parent_column.name IS NULL
           OR parent_column.name <> expected.ParentColumnName
           OR referenced_column.name IS NULL
           OR referenced_column.name <> expected.ReferencedColumnName
           OR (SELECT COUNT(*) FROM sys.foreign_key_columns
               WHERE constraint_object_id = actual_fk.object_id) <> 1
    )
        THROW 63227, N'BMS budget-instance draft refused: BudgetInstanceLines foreign keys differ.', 1;

    IF EXISTS
    (
        SELECT 1 FROM bms_web.BudgetInstances WITH (HOLDLOCK)
        WHERE Status COLLATE Latin1_General_100_BIN2 <> N'Draft'
           OR DAY(ValidFrom) <> 1 OR ValidTo <> EOMONTH(ValidTo)
           OR ValidFrom > ValidTo
    )
        THROW 63228, N'BMS budget-instance draft refused: stored header rows violate the draft contract.', 1;
    IF EXISTS
    (
        SELECT 1
        FROM bms_web.BudgetInstanceLines AS line WITH (HOLDLOCK)
        INNER JOIN bms_web.BudgetInstances AS header WITH (HOLDLOCK)
          ON header.Id = line.BudgetInstanceId
        WHERE DAY(line.BudgetMonth) <> 1
           OR line.BudgetMonth < header.ValidFrom OR line.BudgetMonth > header.ValidTo
           OR line.LimitAmountKzt < 0
           OR line.SortOrder < 0
    )
        THROW 63228, N'BMS budget-instance draft refused: stored line rows violate the draft contract.', 1;
    DROP TABLE #BD034PreHeaderYear;
    DROP TABLE #BD034PreHeaderMonth;
    DROP TABLE #BD034PreHeaderStatus;
    DROP TABLE #BD034PreLineMonth;
    DROP TABLE #BD034PreLineLimit;
    DROP TABLE #BD034PreLineSort;
    /* SQL034 PREDECESSOR SHAPE END */

    IF @VersionRegistered = 0
    BEGIN
        -- Any named object, even a view or an empty partial table, is foreign until registered.
        IF OBJECT_ID(N'bms_web.BudgetPrefixOwners') IS NOT NULL
           OR OBJECT_ID(N'bms_web.BudgetCostCenterSettings') IS NOT NULL
           OR OBJECT_ID(N'bms_web.BudgetCostCenterCounters') IS NOT NULL
           OR OBJECT_ID(N'bms_web.BudgetCostCenterMemberships') IS NOT NULL
            THROW 63407, N'BMS budget directory refused: unregistered directory objects already exist.', 1;
        CREATE TABLE bms_web.BudgetPrefixOwners
        (
            CanonicalPrefix nvarchar(6) COLLATE Latin1_General_100_BIN2 NOT NULL,
            CostCenterId uniqueidentifier NOT NULL,
            CONSTRAINT PK_bms_web_BudgetPrefixOwners PRIMARY KEY CLUSTERED (CanonicalPrefix),
            CONSTRAINT UX_bms_web_BudgetPrefixOwners_Prefix_CostCenter UNIQUE NONCLUSTERED (CanonicalPrefix, CostCenterId),
            CONSTRAINT CK_bms_web_BudgetPrefixOwners_storage_capacity CHECK (DATALENGTH(CanonicalPrefix) BETWEEN 4 AND 12),
            CONSTRAINT FK_bms_web_BudgetPrefixOwners_CostCenter FOREIGN KEY (CostCenterId)
                REFERENCES bms_web.CostCenters (Id) ON DELETE NO ACTION ON UPDATE NO ACTION
        );
        CREATE TABLE bms_web.BudgetCostCenterSettings
        (
            CostCenterId uniqueidentifier NOT NULL,
            Abbreviation nvarchar(6) COLLATE Latin1_General_100_BIN2 NULL,
            ConcurrencyStamp uniqueidentifier NOT NULL,
            CONSTRAINT PK_bms_web_BudgetCostCenterSettings PRIMARY KEY CLUSTERED (CostCenterId),
            CONSTRAINT CK_bms_web_BudgetCostCenterSettings_stamp CHECK (ConcurrencyStamp <> CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000')),
            CONSTRAINT FK_bms_web_BudgetCostCenterSettings_CostCenter FOREIGN KEY (CostCenterId)
                REFERENCES bms_web.CostCenters (Id) ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_BudgetCostCenterSettings_PrefixOwner FOREIGN KEY (Abbreviation, CostCenterId)
                REFERENCES bms_web.BudgetPrefixOwners (CanonicalPrefix, CostCenterId) ON DELETE NO ACTION ON UPDATE NO ACTION
        );
        CREATE TABLE bms_web.BudgetCostCenterCounters
        (
            CostCenterId uniqueidentifier NOT NULL,
            LastIssuedOrdinal bigint NOT NULL,
            CONSTRAINT PK_bms_web_BudgetCostCenterCounters PRIMARY KEY CLUSTERED (CostCenterId),
            CONSTRAINT CK_bms_web_BudgetCostCenterCounters_ordinal CHECK (LastIssuedOrdinal >= 0),
            CONSTRAINT FK_bms_web_BudgetCostCenterCounters_Settings FOREIGN KEY (CostCenterId)
                REFERENCES bms_web.BudgetCostCenterSettings (CostCenterId) ON DELETE NO ACTION ON UPDATE NO ACTION
        );
        CREATE TABLE bms_web.BudgetCostCenterMemberships
        (
            EmployeeId uniqueidentifier NOT NULL,
            CostCenterId uniqueidentifier NOT NULL,
            MembershipKind nvarchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
            IsActive bit NOT NULL,
            CONSTRAINT PK_bms_web_BudgetCostCenterMemberships PRIMARY KEY CLUSTERED (EmployeeId, CostCenterId),
            CONSTRAINT CK_bms_web_BudgetCostCenterMemberships_kind CHECK ((MembershipKind = N'OWNER' AND DATALENGTH(MembershipKind) = 10) OR (MembershipKind = N'PARTICIPANT' AND DATALENGTH(MembershipKind) = 22)),
            CONSTRAINT FK_bms_web_BudgetCostCenterMemberships_Employee FOREIGN KEY (EmployeeId)
                REFERENCES bms_web.Employees (Id) ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_BudgetCostCenterMemberships_Settings FOREIGN KEY (CostCenterId)
                REFERENCES bms_web.BudgetCostCenterSettings (CostCenterId) ON DELETE NO ACTION ON UPDATE NO ACTION
        );
        CREATE NONCLUSTERED INDEX IX_bms_web_BudgetCostCenterMemberships_CostCenter_Active_Employee
            ON bms_web.BudgetCostCenterMemberships (CostCenterId, IsActive, EmployeeId);
        CREATE NONCLUSTERED INDEX IX_bms_web_BudgetPrefixOwners_CostCenter
            ON bms_web.BudgetPrefixOwners (CostCenterId);
        CREATE NONCLUSTERED INDEX IX_bms_web_BudgetCostCenterSettings_PrefixOwner
            ON bms_web.BudgetCostCenterSettings (Abbreviation, CostCenterId);
    END;
    ELSE IF OBJECT_ID(N'bms_web.BudgetPrefixOwners', N'U') IS NULL
         OR OBJECT_ID(N'bms_web.BudgetCostCenterSettings', N'U') IS NULL
         OR OBJECT_ID(N'bms_web.BudgetCostCenterCounters', N'U') IS NULL
         OR OBJECT_ID(N'bms_web.BudgetCostCenterMemberships', N'U') IS NULL
        THROW 63408, N'BMS budget directory refused: registered directory objects are absent.', 1;

/* SQL034 DIRECTORY SHAPE BEGIN */
-- Shared exact metadata predicate: migration repeat and runtime readiness.
-- Prefix storage holds up to three supplementary Unicode letters (six UTF-16 units).
-- The byte-capacity CHECK is NOT the NFC/uppercase/Unicode-letter business validator.
IF OBJECT_ID(N'tempdb..#BD034Prefix') IS NOT NULL
   OR OBJECT_ID(N'tempdb..#BD034Stamp') IS NOT NULL
   OR OBJECT_ID(N'tempdb..#BD034Counter') IS NOT NULL
   OR OBJECT_ID(N'tempdb..#BD034Membership') IS NOT NULL
    THROW 63409, N'BMS budget directory refused: canonical local temp name is already in use.', 1;
CREATE TABLE #BD034Prefix
(
    CanonicalPrefix nvarchar(6) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CHECK (DATALENGTH(CanonicalPrefix) BETWEEN 4 AND 12)
);
CREATE TABLE #BD034Stamp
(
    ConcurrencyStamp uniqueidentifier NOT NULL,
    CHECK (ConcurrencyStamp <> CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000'))
);
CREATE TABLE #BD034Counter
(
    LastIssuedOrdinal bigint NOT NULL,
    CHECK (LastIssuedOrdinal >= 0)
);
CREATE TABLE #BD034Membership
(
    MembershipKind nvarchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CHECK ((MembershipKind = N'OWNER' AND DATALENGTH(MembershipKind) = 10) OR (MembershipKind = N'PARTICIPANT' AND DATALENGTH(MembershipKind) = 22))
);
DECLARE @BD034Tables TABLE (TableName sysname NOT NULL PRIMARY KEY, ObjectId int NULL);
INSERT INTO @BD034Tables VALUES
    (N'BudgetPrefixOwners', OBJECT_ID(N'bms_web.BudgetPrefixOwners', N'U')),
    (N'BudgetCostCenterSettings', OBJECT_ID(N'bms_web.BudgetCostCenterSettings', N'U')),
    (N'BudgetCostCenterCounters', OBJECT_ID(N'bms_web.BudgetCostCenterCounters', N'U')),
    (N'BudgetCostCenterMemberships', OBJECT_ID(N'bms_web.BudgetCostCenterMemberships', N'U'));
DECLARE @BD034Columns TABLE
(
    TableName sysname NOT NULL, Ordinal int NOT NULL, ColumnName sysname NOT NULL,
    TypeName sysname NOT NULL, MaxLength int NOT NULL, [Precision] int NOT NULL,
    Scale int NOT NULL, IsNullable bit NOT NULL, CollationName sysname NULL
);
INSERT INTO @BD034Columns VALUES
    (N'BudgetPrefixOwners', 1, N'CanonicalPrefix', N'nvarchar', 12, 0, 0, 0, N'Latin1_General_100_BIN2'),
    (N'BudgetPrefixOwners', 2, N'CostCenterId', N'uniqueidentifier', 16, 0, 0, 0, NULL),
    (N'BudgetCostCenterSettings', 1, N'CostCenterId', N'uniqueidentifier', 16, 0, 0, 0, NULL),
    (N'BudgetCostCenterSettings', 2, N'Abbreviation', N'nvarchar', 12, 0, 0, 1, N'Latin1_General_100_BIN2'),
    (N'BudgetCostCenterSettings', 3, N'ConcurrencyStamp', N'uniqueidentifier', 16, 0, 0, 0, NULL),
    (N'BudgetCostCenterCounters', 1, N'CostCenterId', N'uniqueidentifier', 16, 0, 0, 0, NULL),
    (N'BudgetCostCenterCounters', 2, N'LastIssuedOrdinal', N'bigint', 8, 19, 0, 0, NULL),
    (N'BudgetCostCenterMemberships', 1, N'EmployeeId', N'uniqueidentifier', 16, 0, 0, 0, NULL),
    (N'BudgetCostCenterMemberships', 2, N'CostCenterId', N'uniqueidentifier', 16, 0, 0, 0, NULL),
    (N'BudgetCostCenterMemberships', 3, N'MembershipKind', N'nvarchar', 32, 0, 0, 0, N'Latin1_General_100_BIN2'),
    (N'BudgetCostCenterMemberships', 4, N'IsActive', N'bit', 1, 1, 0, 0, NULL);
DECLARE @BD034Indexes TABLE
(
    TableName sysname NOT NULL, IndexName sysname NOT NULL, IndexType int NOT NULL,
    IsPrimary bit NOT NULL, IsUnique bit NOT NULL, IsConstraint bit NOT NULL,
    KeyColumns nvarchar(200) NOT NULL
);
INSERT INTO @BD034Indexes VALUES
    (N'BudgetPrefixOwners', N'PK_bms_web_BudgetPrefixOwners', 1, 1, 1, 0, N'CanonicalPrefix'),
    (N'BudgetPrefixOwners', N'UX_bms_web_BudgetPrefixOwners_Prefix_CostCenter', 2, 0, 1, 1, N'CanonicalPrefix,CostCenterId'),
    (N'BudgetPrefixOwners', N'IX_bms_web_BudgetPrefixOwners_CostCenter', 2, 0, 0, 0, N'CostCenterId'),
    (N'BudgetCostCenterSettings', N'PK_bms_web_BudgetCostCenterSettings', 1, 1, 1, 0, N'CostCenterId'),
    (N'BudgetCostCenterSettings', N'IX_bms_web_BudgetCostCenterSettings_PrefixOwner', 2, 0, 0, 0, N'Abbreviation,CostCenterId'),
    (N'BudgetCostCenterCounters', N'PK_bms_web_BudgetCostCenterCounters', 1, 1, 1, 0, N'CostCenterId'),
    (N'BudgetCostCenterMemberships', N'PK_bms_web_BudgetCostCenterMemberships', 1, 1, 1, 0, N'EmployeeId,CostCenterId'),
    (N'BudgetCostCenterMemberships', N'IX_bms_web_BudgetCostCenterMemberships_CostCenter_Active_Employee', 2, 0, 0, 0, N'CostCenterId,IsActive,EmployeeId');
DECLARE @BD034ForeignKeys TABLE
(
    TableName sysname NOT NULL, ConstraintName sysname NOT NULL, Ordinal int NOT NULL,
    ColumnName sysname NOT NULL, ReferencedTable sysname NOT NULL, ReferencedColumn sysname NOT NULL
);
INSERT INTO @BD034ForeignKeys VALUES
    (N'BudgetPrefixOwners', N'FK_bms_web_BudgetPrefixOwners_CostCenter', 1, N'CostCenterId', N'CostCenters', N'Id'),
    (N'BudgetCostCenterSettings', N'FK_bms_web_BudgetCostCenterSettings_CostCenter', 1, N'CostCenterId', N'CostCenters', N'Id'),
    (N'BudgetCostCenterSettings', N'FK_bms_web_BudgetCostCenterSettings_PrefixOwner', 1, N'Abbreviation', N'BudgetPrefixOwners', N'CanonicalPrefix'),
    (N'BudgetCostCenterSettings', N'FK_bms_web_BudgetCostCenterSettings_PrefixOwner', 2, N'CostCenterId', N'BudgetPrefixOwners', N'CostCenterId'),
    (N'BudgetCostCenterCounters', N'FK_bms_web_BudgetCostCenterCounters_Settings', 1, N'CostCenterId', N'BudgetCostCenterSettings', N'CostCenterId'),
    (N'BudgetCostCenterMemberships', N'FK_bms_web_BudgetCostCenterMemberships_Employee', 1, N'EmployeeId', N'Employees', N'Id'),
    (N'BudgetCostCenterMemberships', N'FK_bms_web_BudgetCostCenterMemberships_Settings', 1, N'CostCenterId', N'BudgetCostCenterSettings', N'CostCenterId');
DECLARE @BD034Checks TABLE
(
    TableName sysname NOT NULL, ConstraintName sysname NOT NULL,
    ParentColumnId int NOT NULL, CanonicalDefinition nvarchar(max) NULL
);
INSERT INTO @BD034Checks
SELECT expected.TableName, expected.ConstraintName,
       ISNULL(COLUMNPROPERTY(own_table.ObjectId, expected.ParentColumnName, N'ColumnId'), -1),
       canonical.definition
FROM (VALUES
    (N'BudgetPrefixOwners', N'CK_bms_web_BudgetPrefixOwners_storage_capacity', N'CanonicalPrefix', N'tempdb..#BD034Prefix'),
    (N'BudgetCostCenterSettings', N'CK_bms_web_BudgetCostCenterSettings_stamp', N'ConcurrencyStamp', N'tempdb..#BD034Stamp'),
    (N'BudgetCostCenterCounters', N'CK_bms_web_BudgetCostCenterCounters_ordinal', N'LastIssuedOrdinal', N'tempdb..#BD034Counter'),
    (N'BudgetCostCenterMemberships', N'CK_bms_web_BudgetCostCenterMemberships_kind', N'MembershipKind', N'tempdb..#BD034Membership')
) AS expected(TableName, ConstraintName, ParentColumnName, TemporaryTable)
INNER JOIN @BD034Tables AS own_table ON own_table.TableName = expected.TableName
LEFT JOIN tempdb.sys.check_constraints AS canonical ON canonical.parent_object_id = OBJECT_ID(expected.TemporaryTable);

DECLARE @BudgetDirectoryShapeReady bit = 1;
IF EXISTS
(
    SELECT 1 FROM @BD034Tables AS expected
    LEFT JOIN sys.tables AS actual ON actual.object_id = expected.ObjectId
    WHERE actual.object_id IS NULL OR actual.name COLLATE Latin1_General_100_BIN2 <> expected.TableName
       OR actual.temporal_type <> 0 OR actual.is_memory_optimized <> 0 OR actual.is_filetable <> 0
       OR EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = expected.ObjectId)
       OR (SELECT COUNT(*) FROM sys.columns WHERE object_id = expected.ObjectId)
          <> (SELECT COUNT(*) FROM @BD034Columns WHERE TableName = expected.TableName)
       OR (SELECT COUNT(*) FROM sys.indexes WHERE object_id = expected.ObjectId AND index_id > 0)
          <> (SELECT COUNT(*) FROM @BD034Indexes WHERE TableName = expected.TableName)
       OR (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = expected.ObjectId)
          <> (SELECT COUNT(DISTINCT ConstraintName) FROM @BD034ForeignKeys WHERE TableName = expected.TableName)
       OR (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = expected.ObjectId)
          <> (SELECT COUNT(*) FROM @BD034Checks WHERE TableName = expected.TableName)
) SET @BudgetDirectoryShapeReady = 0;
IF EXISTS
(
    SELECT 1 FROM @BD034Columns AS expected
    INNER JOIN @BD034Tables AS own_table ON own_table.TableName = expected.TableName
    LEFT JOIN sys.columns AS actual ON actual.object_id = own_table.ObjectId AND actual.column_id = expected.Ordinal
    WHERE actual.column_id IS NULL OR actual.name COLLATE Latin1_General_100_BIN2 <> expected.ColumnName
       OR TYPE_NAME(actual.user_type_id) COLLATE Latin1_General_100_BIN2 <> expected.TypeName
       OR actual.max_length <> expected.MaxLength OR actual.[precision] <> expected.[Precision]
       OR actual.scale <> expected.Scale OR actual.is_nullable <> expected.IsNullable
       OR ISNULL(actual.collation_name, N'') COLLATE Latin1_General_100_BIN2 <> ISNULL(expected.CollationName, N'')
       OR actual.is_identity <> 0 OR actual.is_computed <> 0 OR actual.default_object_id <> 0
       OR actual.rule_object_id <> 0 OR actual.generated_always_type <> 0 OR actual.is_rowguidcol <> 0
       OR actual.is_filestream <> 0 OR actual.is_sparse <> 0 OR actual.is_column_set <> 0
       OR actual.encryption_type IS NOT NULL OR actual.is_hidden <> 0 OR actual.is_masked <> 0
) SET @BudgetDirectoryShapeReady = 0;
IF EXISTS
(
    SELECT 1 FROM @BD034Indexes AS expected
    INNER JOIN @BD034Tables AS own_table ON own_table.TableName = expected.TableName
    LEFT JOIN sys.indexes AS actual ON actual.object_id = own_table.ObjectId
        AND actual.name COLLATE Latin1_General_100_BIN2 = expected.IndexName
    WHERE actual.index_id IS NULL OR actual.type <> expected.IndexType
       OR actual.is_primary_key <> expected.IsPrimary OR actual.is_unique <> expected.IsUnique
       OR actual.is_unique_constraint <> expected.IsConstraint OR actual.is_disabled <> 0
       OR actual.is_hypothetical <> 0 OR actual.has_filter <> 0 OR actual.filter_definition IS NOT NULL
       OR actual.ignore_dup_key <> 0
       OR ISNULL((
            SELECT STRING_AGG(c.name, N',') WITHIN GROUP (ORDER BY ic.key_ordinal)
            FROM sys.index_columns AS ic
            INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE ic.object_id = actual.object_id AND ic.index_id = actual.index_id AND ic.key_ordinal > 0
          ), N'') COLLATE Latin1_General_100_BIN2 <> expected.KeyColumns
) SET @BudgetDirectoryShapeReady = 0;
IF EXISTS
(
    SELECT 1 FROM sys.index_columns AS actual
    INNER JOIN @BD034Tables AS own_table ON own_table.ObjectId = actual.object_id
    WHERE actual.is_included_column <> 0 OR actual.is_descending_key <> 0
) SET @BudgetDirectoryShapeReady = 0;
IF EXISTS
(
    SELECT 1 FROM @BD034ForeignKeys AS expected
    INNER JOIN @BD034Tables AS own_table ON own_table.TableName = expected.TableName
    LEFT JOIN sys.foreign_keys AS actual ON actual.parent_object_id = own_table.ObjectId
        AND actual.name COLLATE Latin1_General_100_BIN2 = expected.ConstraintName
    LEFT JOIN sys.foreign_key_columns AS fk_column ON fk_column.constraint_object_id = actual.object_id
        AND fk_column.constraint_column_id = expected.Ordinal
    LEFT JOIN sys.columns AS parent_column ON parent_column.object_id = fk_column.parent_object_id
        AND parent_column.column_id = fk_column.parent_column_id
    LEFT JOIN sys.columns AS referenced_column ON referenced_column.object_id = fk_column.referenced_object_id
        AND referenced_column.column_id = fk_column.referenced_column_id
    WHERE actual.object_id IS NULL OR OBJECT_ID(N'bms_web.' + expected.ReferencedTable, N'U') IS NULL
       OR actual.referenced_object_id <> OBJECT_ID(N'bms_web.' + expected.ReferencedTable, N'U')
       OR actual.is_disabled <> 0 OR actual.is_not_trusted <> 0 OR actual.is_not_for_replication <> 0
       OR actual.is_system_named <> 0 OR actual.delete_referential_action <> 0 OR actual.update_referential_action <> 0
       OR parent_column.name IS NULL OR parent_column.name COLLATE Latin1_General_100_BIN2 <> expected.ColumnName
       OR referenced_column.name IS NULL OR referenced_column.name COLLATE Latin1_General_100_BIN2 <> expected.ReferencedColumn
       OR (SELECT COUNT(*) FROM sys.foreign_key_columns WHERE constraint_object_id = actual.object_id)
          <> (SELECT COUNT(*) FROM @BD034ForeignKeys WHERE ConstraintName = expected.ConstraintName)
) SET @BudgetDirectoryShapeReady = 0;
IF EXISTS
(
    SELECT 1 FROM @BD034Checks AS expected
    INNER JOIN @BD034Tables AS own_table ON own_table.TableName = expected.TableName
    LEFT JOIN sys.check_constraints AS actual ON actual.parent_object_id = own_table.ObjectId
        AND actual.name COLLATE Latin1_General_100_BIN2 = expected.ConstraintName
    WHERE actual.object_id IS NULL OR actual.definition IS NULL OR expected.CanonicalDefinition IS NULL
       OR actual.is_disabled <> 0 OR actual.is_not_trusted <> 0 OR actual.is_not_for_replication <> 0
       OR actual.is_system_named <> 0 OR expected.ParentColumnId <= 0
       OR actual.parent_column_id <> expected.ParentColumnId
       OR actual.definition COLLATE Latin1_General_100_BIN2 <> expected.CanonicalDefinition COLLATE Latin1_General_100_BIN2
       OR DATALENGTH(actual.definition) <> DATALENGTH(expected.CanonicalDefinition)
) SET @BudgetDirectoryShapeReady = 0;
DROP TABLE #BD034Prefix;
DROP TABLE #BD034Stamp;
DROP TABLE #BD034Counter;
DROP TABLE #BD034Membership;
/* SQL034 DIRECTORY SHAPE END */

    IF @BudgetDirectoryShapeReady = 0
    BEGIN
        -- Failure-only schema metadata; never business rows. Preserve the original
        -- refusal and rollback, but identify the measured mismatch in the local
        -- migration transcript instead of guessing which predicate failed.
        -- PRINT chunks avoid sqlcmd's 256-character limit for nvarchar(max) result columns.
        DECLARE @BD034FailureMetadata nvarchar(max) =
            N'{"tables":' + (
        SELECT N'SQL034_TABLES' AS DiagnosticFacet, expected.TableName, expected.ObjectId,
            actual.name, actual.temporal_type, actual.is_memory_optimized, actual.is_filetable,
            (SELECT COUNT(*) FROM sys.triggers WHERE parent_id = expected.ObjectId) AS TriggerCount,
            (SELECT COUNT(*) FROM sys.columns WHERE object_id = expected.ObjectId) AS ColumnCount,
            (SELECT COUNT(*) FROM sys.indexes WHERE object_id = expected.ObjectId AND index_id > 0) AS IndexCount,
            (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = expected.ObjectId) AS ForeignKeyCount,
            (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = expected.ObjectId) AS CheckCount
        FROM @BD034Tables AS expected LEFT JOIN sys.tables AS actual ON actual.object_id = expected.ObjectId
        FOR JSON PATH, INCLUDE_NULL_VALUES
            ) +
            N',"columns":' + (
        SELECT N'SQL034_COLUMNS' AS DiagnosticFacet, own_table.TableName, actual.name,
            actual.column_id, TYPE_NAME(actual.user_type_id) AS TypeName, actual.max_length,
            actual.[precision], actual.scale, actual.is_nullable, actual.collation_name,
            actual.is_identity, actual.is_computed, actual.default_object_id, actual.rule_object_id,
            actual.generated_always_type, actual.is_rowguidcol, actual.is_filestream,
            actual.is_sparse, actual.is_column_set, actual.encryption_type, actual.is_hidden, actual.is_masked
        FROM @BD034Tables AS own_table INNER JOIN sys.columns AS actual ON actual.object_id = own_table.ObjectId
        FOR JSON PATH, INCLUDE_NULL_VALUES
            ) +
            N',"indexes":' + (
        SELECT N'SQL034_INDEXES' AS DiagnosticFacet, own_table.TableName, actual.name, actual.index_id,
            actual.type, actual.is_primary_key, actual.is_unique, actual.is_unique_constraint,
            actual.is_disabled, actual.is_hypothetical, actual.has_filter, actual.filter_definition, actual.ignore_dup_key,
            column_info.key_ordinal, column_info.is_included_column, column_info.is_descending_key, col.name AS ColumnName
        FROM @BD034Tables AS own_table INNER JOIN sys.indexes AS actual ON actual.object_id = own_table.ObjectId
        LEFT JOIN sys.index_columns AS column_info ON column_info.object_id = actual.object_id AND column_info.index_id = actual.index_id
        LEFT JOIN sys.columns AS col ON col.object_id = column_info.object_id AND col.column_id = column_info.column_id
        WHERE actual.index_id > 0
        FOR JSON PATH, INCLUDE_NULL_VALUES
            ) +
            N',"foreignKeys":' + (
        SELECT N'SQL034_FOREIGN_KEYS' AS DiagnosticFacet, own_table.TableName, actual.name,
            OBJECT_SCHEMA_NAME(actual.referenced_object_id) AS ReferencedSchema,
            OBJECT_NAME(actual.referenced_object_id) AS ReferencedTable,
            actual.is_disabled, actual.is_not_trusted, actual.is_not_for_replication, actual.is_system_named,
            actual.delete_referential_action, actual.update_referential_action, fk_column.constraint_column_id,
            parent_column.name AS ParentColumn, referenced_column.name AS ReferencedColumn
        FROM @BD034Tables AS own_table INNER JOIN sys.foreign_keys AS actual ON actual.parent_object_id = own_table.ObjectId
        LEFT JOIN sys.foreign_key_columns AS fk_column ON fk_column.constraint_object_id = actual.object_id
        LEFT JOIN sys.columns AS parent_column ON parent_column.object_id = fk_column.parent_object_id AND parent_column.column_id = fk_column.parent_column_id
        LEFT JOIN sys.columns AS referenced_column ON referenced_column.object_id = fk_column.referenced_object_id AND referenced_column.column_id = fk_column.referenced_column_id
        FOR JSON PATH, INCLUDE_NULL_VALUES
            ) +
            N',"checks":' + (
        SELECT N'SQL034_CHECKS' AS DiagnosticFacet, expected.TableName, expected.ConstraintName,
            expected.ParentColumnId AS ExpectedParentColumnId,
            expected.CanonicalDefinition, actual.definition AS ActualDefinition,
            actual.is_disabled, actual.is_not_trusted, actual.is_not_for_replication,
            actual.is_system_named, actual.parent_column_id
        FROM @BD034Checks AS expected INNER JOIN @BD034Tables AS own_table ON own_table.TableName = expected.TableName
        LEFT JOIN sys.check_constraints AS actual ON actual.parent_object_id = own_table.ObjectId
            AND actual.name COLLATE Latin1_General_100_BIN2 = expected.ConstraintName
        FOR JSON PATH, INCLUDE_NULL_VALUES
            ) +
            N'}';
        PRINT N'SQL034_METADATA_BEGIN';
        DECLARE @BD034FailureOffset int = 1;
        WHILE @BD034FailureOffset <= DATALENGTH(@BD034FailureMetadata) / 2
        BEGIN
            PRINT SUBSTRING(@BD034FailureMetadata, @BD034FailureOffset, 1000);
            SET @BD034FailureOffset += 1000;
        END;
        PRINT N'SQL034_METADATA_END';
        THROW 63410, N'BMS budget directory refused: exact trusted directory schema differs.', 1;
    END;
    IF @VersionRegistered = 0
        INSERT INTO bms_web.schema_version (version_no, script_name)
        VALUES (34, N'034_budget_cost_center_directory.sql');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
