-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    SQL033: exact trusted SQL032 predecessor -> inclusive monthly draft period.
    SQL032 remains byte-identical. Structural guards below are retained from
    SQL032, choosing the exact annual predecessor or exact monthly repeat CHECK.
    Only the named CHECK and version registration change. No document/data,
    numbering, membership, approval or Limit/Reserve/Paid writes.
*/
IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS budget-instance draft refused: run SQL033 outside an ambient transaction.', 16, 1);
    RETURN;
END;
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 63201, N'BMS budget-instance draft refused: current database must be the explicitly owned BMS database.', 1;
IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 63202, N'BMS budget-instance draft refused: current database is not READ_WRITE.', 1;
IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.CostCenters', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.CashFlowItems', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.Employees', N'U') IS NULL
    THROW 63203, N'BMS budget-instance draft refused: required managed tables are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Refuse pre-existing local names; this invocation owns only tables it creates.
    IF OBJECT_ID(N'tempdb..#BI033HeaderYear') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BI033HeaderMonth') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BI033HeaderStatus') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BI033LineMonth') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BI033LineLimit') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BI033LineSort') IS NOT NULL
        THROW 63309, N'BMS monthly period refused: canonical local temp name is already in use.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no > 33
    )
        THROW 63204, N'BMS budget-instance draft refused: a future schema version is registered.', 1;

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
                (29, N'029_expense_request_substitutions.sql'),
                (30, N'030_expense_request_task_scope_index.sql'),
                (31, N'031_expense_request_attachments.sql'),
                (32, N'032_budget_instance_draft.sql')
        ) AS expected(version_no, script_name)
        INNER JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK)
          ON actual.version_no = expected.version_no
         AND actual.script_name COLLATE Latin1_General_100_BIN2 =
             expected.script_name COLLATE Latin1_General_100_BIN2
         AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
    ) <> 32
        THROW 63205, N'BMS budget-instance draft refused: exact SQL001-SQL032 predecessors are required.', 1;

    IF EXISTS
    (
        SELECT 1 FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 33 AND
        (
            script_name COLLATE Latin1_General_100_BIN2 <> N'033_budget_instance_monthly_period.sql'
            OR DATALENGTH(script_name) <> DATALENGTH(N'033_budget_instance_monthly_period.sql')
        )
    )
        THROW 63206, N'BMS budget-instance draft refused: SQL033 belongs to another script.', 1;

    DECLARE @VersionRegistered bit = CASE WHEN EXISTS
        (SELECT 1 FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK) WHERE version_no = 33)
        THEN 1 ELSE 0 END;
    DECLARE @HeaderId int = OBJECT_ID(N'bms_web.BudgetInstances');
    DECLARE @LineId int = OBJECT_ID(N'bms_web.BudgetInstanceLines');
    IF @HeaderId IS NULL OR @LineId IS NULL
        THROW 63307, N'BMS monthly period refused: predecessor draft tables are absent.', 1;

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

    CREATE TABLE #BI033HeaderYear
    (
        ValidFrom date, ValidTo date,
        CHECK (
            DAY(ValidFrom) = 1 AND MONTH(ValidFrom) = 1
            AND DAY(ValidTo) = 31 AND MONTH(ValidTo) = 12
            AND YEAR(ValidFrom) = YEAR(ValidTo))
    );
    CREATE TABLE #BI033HeaderMonth
    (
        ValidFrom date, ValidTo date,
        CHECK (DAY(ValidFrom) = 1 AND ValidTo = EOMONTH(ValidTo) AND ValidFrom <= ValidTo)
    );
    CREATE TABLE #BI033HeaderStatus
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
        (CASE WHEN @VersionRegistered = 0 THEN N'CK_bms_web_BudgetInstances_year_bounds'
              ELSE N'CK_bms_web_BudgetInstances_month_bounds' END,
         CAST(NULL AS sysname),
         CASE WHEN @VersionRegistered = 0 THEN N'tempdb..#BI033HeaderYear'
              ELSE N'tempdb..#BI033HeaderMonth' END),
        (N'CK_bms_web_BudgetInstances_status_draft_slice',
         N'Status', N'tempdb..#BI033HeaderStatus')
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

    CREATE TABLE #BI033LineMonth (BudgetMonth date, CHECK (DAY(BudgetMonth) = 1));
    CREATE TABLE #BI033LineLimit (LimitAmountKzt decimal(18, 2), CHECK (LimitAmountKzt >= 0));
    CREATE TABLE #BI033LineSort (SortOrder int, CHECK (SortOrder >= 0));
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
         N'BudgetMonth', N'tempdb..#BI033LineMonth'),
        (N'CK_bms_web_BudgetInstanceLines_limit',
         N'LimitAmountKzt', N'tempdb..#BI033LineLimit'),
        (N'CK_bms_web_BudgetInstanceLines_sort',
         N'SortOrder', N'tempdb..#BI033LineSort')
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

    IF @VersionRegistered = 0
    BEGIN
        ALTER TABLE bms_web.BudgetInstances DROP CONSTRAINT CK_bms_web_BudgetInstances_year_bounds;
        ALTER TABLE bms_web.BudgetInstances WITH CHECK
            ADD CONSTRAINT CK_bms_web_BudgetInstances_month_bounds
            CHECK (DAY(ValidFrom) = 1 AND ValidTo = EOMONTH(ValidTo) AND ValidFrom <= ValidTo);
    END;

    -- Verify the trusted post-shape before registration, including the first upgrade.
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints AS actual
        INNER JOIN tempdb.sys.check_constraints AS canonical
          ON canonical.parent_object_id = OBJECT_ID(N'tempdb..#BI033HeaderMonth')
        WHERE actual.parent_object_id = @HeaderId
          AND actual.name = N'CK_bms_web_BudgetInstances_month_bounds'
          AND actual.is_disabled = 0 AND actual.is_not_trusted = 0
          AND actual.is_not_for_replication = 0 AND actual.is_system_named = 0
          AND actual.parent_column_id = 0
          AND REPLACE(REPLACE(REPLACE(REPLACE(actual.definition,
              N' ', N''), NCHAR(13), N''), NCHAR(10), N''), NCHAR(9), N'') COLLATE Latin1_General_100_BIN2
            = REPLACE(REPLACE(REPLACE(REPLACE(canonical.definition,
              N' ', N''), NCHAR(13), N''), NCHAR(10), N''), NCHAR(9), N'') COLLATE Latin1_General_100_BIN2
    )
        THROW 63308, N'BMS monthly period refused: post-upgrade CHECK differs.', 1;
    IF @VersionRegistered = 0
        INSERT INTO bms_web.schema_version (version_no, script_name)
        VALUES (33, N'033_budget_instance_monthly_period.sql');
    -- All canonical comparisons are complete. SQL032 session objects stay untouched.
    DROP TABLE #BI033HeaderYear;
    DROP TABLE #BI033HeaderMonth;
    DROP TABLE #BI033HeaderStatus;
    DROP TABLE #BI033LineMonth;
    DROP TABLE #BI033LineLimit;
    DROP TABLE #BI033LineSort;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
