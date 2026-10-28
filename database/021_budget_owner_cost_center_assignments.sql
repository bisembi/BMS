-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web W404 — read-only source projection of budget-owner assignments.

    IMPORTANT
    - Apply SQL001 through SQL020 first and run this script standalone.
    - The table is intentionally empty: this migration neither imports nor
      invents employees, functional roles, cost centres, access grants or
      business assignments.
    - A row is a source-projected relation “employee — budget-owner — cost
      centre”. It is not ActorCostCenterAccess and conveys no direct read
      scope, authentication claim, workflow permission or write authority.
    - The future reader must additionally require active Employee,
      FunctionalRole BUDGET_OWNER and EmployeeFunctionalRole assignment;
      a SQL CHECK constraint cannot enforce those cross-table conditions.
    - It has not been executed against an accounting server.
*/

IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS W404 refused: run 021_budget_owner_cost_center_assignments.sql outside an ambient transaction.', 16, 1);
    RETURN;
END;

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 60401, N'BMS W404 refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 60402, N'BMS W404 refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.Employees', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.FunctionalRoles', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.EmployeeFunctionalRoles', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.CostCenters', N'U') IS NULL
    THROW 60403, N'BMS W404 refused: required managed source tables are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no > 21
    )
        THROW 60404, N'BMS W404 refused: a future schema version is already registered.', 1;

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
                (20, N'020_treasury_payment_overpayment_storage.sql')
        ) AS expected(version_no, script_name)
        INNER JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK)
          ON actual.version_no = expected.version_no
         AND actual.script_name COLLATE Latin1_General_100_BIN2 =
             expected.script_name COLLATE Latin1_General_100_BIN2
         AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
    ) <> 20
        THROW 60405, N'BMS W404 refused: exact schema versions 1 through 20 are required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 21
          AND
          (
              script_name COLLATE Latin1_General_100_BIN2 <>
                  N'021_budget_owner_cost_center_assignments.sql'
              OR DATALENGTH(script_name) <>
                  DATALENGTH(N'021_budget_owner_cost_center_assignments.sql')
          )
    )
        THROW 60406, N'BMS W404 refused: schema version 21 belongs to another script.', 1;

    IF OBJECT_ID(N'bms_web.BudgetOwnerCostCenterAssignments', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.BudgetOwnerCostCenterAssignments
        (
            EmployeeId    uniqueidentifier NOT NULL,
            CostCenterId  uniqueidentifier NOT NULL,
            IsActive      bit NOT NULL,
            CONSTRAINT PK_bms_web_BudgetOwnerCostCenterAssignments
                PRIMARY KEY CLUSTERED (EmployeeId ASC, CostCenterId ASC),
            CONSTRAINT FK_bms_web_BudgetOwnerCostCenterAssignments_Employee
                FOREIGN KEY (EmployeeId) REFERENCES bms_web.Employees (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_BudgetOwnerCostCenterAssignments_CostCenter
                FOREIGN KEY (CostCenterId) REFERENCES bms_web.CostCenters (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION
        );

        CREATE NONCLUSTERED INDEX
            IX_bms_web_BudgetOwnerCostCenterAssignments_CostCenter_Active_Employee
            ON bms_web.BudgetOwnerCostCenterAssignments
                (CostCenterId ASC, IsActive ASC, EmployeeId ASC);
    END;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'EmployeeId', N'uniqueidentifier', 16, 0),
                (N'CostCenterId', N'uniqueidentifier', 16, 0),
                (N'IsActive', N'bit', 1, 0)
        ) AS expected(ColumnName, TypeName, MaxLength, IsNullable)
        LEFT JOIN sys.columns AS actual
          ON actual.object_id = OBJECT_ID(N'bms_web.BudgetOwnerCostCenterAssignments')
         AND actual.name = expected.ColumnName
        WHERE actual.object_id IS NULL
           OR TYPE_NAME(actual.user_type_id) <> expected.TypeName
           OR actual.max_length <> expected.MaxLength
           OR actual.is_nullable <> expected.IsNullable
           OR actual.is_computed <> 0 OR actual.is_identity <> 0
           OR actual.is_rowguidcol <> 0 OR actual.is_filestream <> 0
           OR actual.default_object_id <> 0 OR actual.rule_object_id <> 0
           OR actual.generated_always_type <> 0
           OR actual.is_sparse <> 0 OR actual.is_column_set <> 0
           OR actual.encryption_type IS NOT NULL OR actual.is_hidden <> 0
           OR actual.is_masked <> 0 OR actual.collation_name IS NOT NULL
    )
       OR (SELECT COUNT(*)
           FROM sys.columns
           WHERE object_id = OBJECT_ID(N'bms_web.BudgetOwnerCostCenterAssignments')) <> 3
        THROW 60407, N'BMS W404 refused: assignment column contract is drifted.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'bms_web.BudgetOwnerCostCenterAssignments')
          AND index_id > 0
    ) <> 2
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (N'PK_bms_web_BudgetOwnerCostCenterAssignments', 1, 1, 1, 0, 2),
                   (N'IX_bms_web_BudgetOwnerCostCenterAssignments_CostCenter_Active_Employee', 2, 0, 0, 0, 3)
           ) AS expected(IndexName, IndexType, IsPrimaryKey, IsUnique, IsUniqueConstraint, KeyCount)
           LEFT JOIN sys.indexes AS actual
             ON actual.object_id = OBJECT_ID(N'bms_web.BudgetOwnerCostCenterAssignments')
            AND actual.name = expected.IndexName
           WHERE actual.index_id IS NULL OR actual.type <> expected.IndexType
              OR actual.is_primary_key <> expected.IsPrimaryKey
              OR actual.is_unique <> expected.IsUnique
              OR actual.is_unique_constraint <> expected.IsUniqueConstraint
              OR actual.is_disabled <> 0 OR actual.is_hypothetical <> 0
              OR actual.has_filter <> 0 OR actual.filter_definition IS NOT NULL
              OR actual.ignore_dup_key <> 0
              OR
              (
                  SELECT COUNT(*)
                  FROM sys.index_columns AS key_column
                  WHERE key_column.object_id = actual.object_id
                    AND key_column.index_id = actual.index_id
                    AND key_column.key_ordinal > 0
              ) <> expected.KeyCount
       )
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (N'PK_bms_web_BudgetOwnerCostCenterAssignments', 1, N'EmployeeId'),
                   (N'PK_bms_web_BudgetOwnerCostCenterAssignments', 2, N'CostCenterId'),
                   (N'IX_bms_web_BudgetOwnerCostCenterAssignments_CostCenter_Active_Employee', 1, N'CostCenterId'),
                   (N'IX_bms_web_BudgetOwnerCostCenterAssignments_CostCenter_Active_Employee', 2, N'IsActive'),
                   (N'IX_bms_web_BudgetOwnerCostCenterAssignments_CostCenter_Active_Employee', 3, N'EmployeeId')
           ) AS expected(IndexName, KeyOrdinal, ColumnName)
           LEFT JOIN sys.indexes AS actual_index
             ON actual_index.object_id = OBJECT_ID(N'bms_web.BudgetOwnerCostCenterAssignments')
            AND actual_index.name = expected.IndexName
           LEFT JOIN sys.index_columns AS actual_key
             ON actual_key.object_id = actual_index.object_id
            AND actual_key.index_id = actual_index.index_id
            AND actual_key.key_ordinal = expected.KeyOrdinal
           LEFT JOIN sys.columns AS actual_column
             ON actual_column.object_id = actual_key.object_id
            AND actual_column.column_id = actual_key.column_id
           WHERE actual_column.name IS NULL OR actual_column.name <> expected.ColumnName
              OR actual_key.is_descending_key <> 0 OR actual_key.is_included_column <> 0
       )
       OR EXISTS
       (
           SELECT 1
           FROM sys.index_columns
           WHERE object_id = OBJECT_ID(N'bms_web.BudgetOwnerCostCenterAssignments')
             AND is_included_column <> 0
       )
        THROW 60408, N'BMS W404 refused: assignment index contract is drifted.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'bms_web.BudgetOwnerCostCenterAssignments')
    ) <> 2
       OR
       (
        SELECT COUNT(*)
        FROM sys.foreign_key_columns
        WHERE parent_object_id = OBJECT_ID(N'bms_web.BudgetOwnerCostCenterAssignments')
       ) <> 2
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (N'FK_bms_web_BudgetOwnerCostCenterAssignments_Employee', N'EmployeeId', N'Employees', N'Id'),
                   (N'FK_bms_web_BudgetOwnerCostCenterAssignments_CostCenter', N'CostCenterId', N'CostCenters', N'Id')
           ) AS expected(ConstraintName, ParentColumnName, ReferencedTableName, ReferencedColumnName)
           LEFT JOIN sys.foreign_keys AS actual_fk
             ON actual_fk.parent_object_id =
                    OBJECT_ID(N'bms_web.BudgetOwnerCostCenterAssignments')
            AND actual_fk.name = expected.ConstraintName
           LEFT JOIN sys.foreign_key_columns AS actual_fkc
             ON actual_fkc.constraint_object_id = actual_fk.object_id
            AND actual_fkc.constraint_column_id = 1
           LEFT JOIN sys.columns AS actual_parent_column
             ON actual_parent_column.object_id = actual_fkc.parent_object_id
            AND actual_parent_column.column_id = actual_fkc.parent_column_id
           LEFT JOIN sys.columns AS actual_referenced_column
             ON actual_referenced_column.object_id = actual_fkc.referenced_object_id
            AND actual_referenced_column.column_id = actual_fkc.referenced_column_id
           WHERE actual_fk.object_id IS NULL
              OR actual_fk.is_disabled <> 0 OR actual_fk.is_not_trusted <> 0
              OR actual_fk.is_not_for_replication <> 0 OR actual_fk.is_system_named <> 0
              OR actual_fk.delete_referential_action <> 0
              OR actual_fk.update_referential_action <> 0
              OR actual_parent_column.name IS NULL
              OR actual_parent_column.name <> expected.ParentColumnName
              OR actual_fkc.referenced_object_id <>
                    OBJECT_ID(N'bms_web.' + expected.ReferencedTableName)
              OR actual_referenced_column.name IS NULL
              OR actual_referenced_column.name <> expected.ReferencedColumnName
       )
        THROW 60409, N'BMS W404 refused: assignment foreign-key contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.BudgetOwnerCostCenterAssignments')
    )
       OR EXISTS
       (
           SELECT 1
           FROM sys.triggers
           WHERE parent_id = OBJECT_ID(N'bms_web.BudgetOwnerCostCenterAssignments')
             AND is_ms_shipped = 0
       )
        THROW 60410, N'BMS W404 refused: assignment table has unexpected CHECK or trigger.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 21
    )
       AND
       (
           SELECT COUNT(*)
           FROM sys.tables
           WHERE object_id = OBJECT_ID(N'bms_web.BudgetOwnerCostCenterAssignments')
       ) <> 1
        THROW 60411, N'BMS W404 refused: version 21 is registered without its table.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 21
    )
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (21, N'021_budget_owner_cost_center_assignments.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
