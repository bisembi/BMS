-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web W409 — dormant storage for immutable expense-request approval routes.

    IMPORTANT
    - Apply SQL001 through SQL021 first and run this script standalone.
    - This migration creates empty physical storage only. It does not seed
      statuses, commands, permissions, roles, employees, route participants or
      budget-owner assignments.
    - It does not submit an expense request, change its status, expose a route,
      create tasks, or grant a business command. The future writer must create
      a whole snapshot atomically with an approved workflow transition.
    - A route can have more than one eligible employee at a stage; later role
      changes must not rewrite this captured relation.
    - It has not been executed against an accounting server.
*/

IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS W409 refused: run 022_expense_request_approval_route_snapshots.sql outside an ambient transaction.', 16, 1);
    RETURN;
END;

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 60901, N'BMS W409 refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 60902, N'BMS W409 refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.ExpenseRequests', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.Organizations', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.CostCenters', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.Employees', N'U') IS NULL
    THROW 60903, N'BMS W409 refused: required managed source tables are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no > 22
    )
        THROW 60904, N'BMS W409 refused: a future schema version is already registered.', 1;

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
                (21, N'021_budget_owner_cost_center_assignments.sql')
        ) AS expected(version_no, script_name)
        INNER JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK)
          ON actual.version_no = expected.version_no
         AND actual.script_name COLLATE Latin1_General_100_BIN2 =
             expected.script_name COLLATE Latin1_General_100_BIN2
         AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
    ) <> 21
        THROW 60905, N'BMS W409 refused: exact schema versions 1 through 21 are required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 22
          AND
          (
              script_name COLLATE Latin1_General_100_BIN2 <>
                  N'022_expense_request_approval_route_snapshots.sql'
              OR DATALENGTH(script_name) <>
                  DATALENGTH(N'022_expense_request_approval_route_snapshots.sql')
          )
    )
        THROW 60906, N'BMS W409 refused: schema version 22 belongs to another script.', 1;

    IF OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteSnapshots', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.ExpenseRequestApprovalRouteSnapshots
        (
            ExpenseRequestId          uniqueidentifier NOT NULL,
            SnapshotNumber            int NOT NULL,
            WorkflowDefinitionVersion int NOT NULL,
            OrganizationId            uniqueidentifier NOT NULL,
            CostCenterId              uniqueidentifier NOT NULL,
            PositiveDeficitKzt        decimal(18, 2) NOT NULL,
            CapturedAtUtc             datetimeoffset(3) NOT NULL,
            CONSTRAINT PK_bms_web_ExpenseRequestApprovalRouteSnapshots
                PRIMARY KEY CLUSTERED (ExpenseRequestId ASC, SnapshotNumber ASC),
            CONSTRAINT FK_bms_web_ExpenseRequestApprovalRouteSnapshots_ExpenseRequest
                FOREIGN KEY (ExpenseRequestId) REFERENCES bms_web.ExpenseRequests (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_ExpenseRequestApprovalRouteSnapshots_Organization
                FOREIGN KEY (OrganizationId) REFERENCES bms_web.Organizations (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_ExpenseRequestApprovalRouteSnapshots_CostCenter
                FOREIGN KEY (CostCenterId) REFERENCES bms_web.CostCenters (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION
        );
    END;

    IF OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteStages', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.ExpenseRequestApprovalRouteStages
        (
            ExpenseRequestId uniqueidentifier NOT NULL,
            SnapshotNumber   int NOT NULL,
            StageNumber      int NOT NULL,
            RoleCode         nvarchar(30) COLLATE Latin1_General_100_BIN2 NOT NULL,
            CONSTRAINT PK_bms_web_ExpenseRequestApprovalRouteStages
                PRIMARY KEY CLUSTERED (ExpenseRequestId ASC, SnapshotNumber ASC, StageNumber ASC),
            CONSTRAINT FK_bms_web_ExpenseRequestApprovalRouteStages_Snapshot
                FOREIGN KEY (ExpenseRequestId, SnapshotNumber)
                REFERENCES bms_web.ExpenseRequestApprovalRouteSnapshots
                    (ExpenseRequestId, SnapshotNumber)
                ON DELETE NO ACTION ON UPDATE NO ACTION
        );
    END;

    IF OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteStageParticipants', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.ExpenseRequestApprovalRouteStageParticipants
        (
            ExpenseRequestId uniqueidentifier NOT NULL,
            SnapshotNumber   int NOT NULL,
            StageNumber      int NOT NULL,
            EmployeeId       uniqueidentifier NOT NULL,
            CONSTRAINT PK_bms_web_ExpenseRequestApprovalRouteStageParticipants
                PRIMARY KEY CLUSTERED
                    (ExpenseRequestId ASC, SnapshotNumber ASC, StageNumber ASC, EmployeeId ASC),
            CONSTRAINT FK_bms_web_ExpenseRequestApprovalRouteStageParticipants_Stage
                FOREIGN KEY (ExpenseRequestId, SnapshotNumber, StageNumber)
                REFERENCES bms_web.ExpenseRequestApprovalRouteStages
                    (ExpenseRequestId, SnapshotNumber, StageNumber)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_ExpenseRequestApprovalRouteStageParticipants_Employee
                FOREIGN KEY (EmployeeId) REFERENCES bms_web.Employees (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION
        );

        CREATE NONCLUSTERED INDEX
            IX_bms_web_ExpenseRequestApprovalRouteStageParticipants_Employee_Snapshot_Stage
            ON bms_web.ExpenseRequestApprovalRouteStageParticipants
                (EmployeeId ASC, ExpenseRequestId ASC, SnapshotNumber ASC, StageNumber ASC);
    END;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'ExpenseRequestApprovalRouteSnapshots', N'ExpenseRequestId', N'uniqueidentifier', 16, 0, 0, 0, CAST(NULL AS nvarchar(128))),
                (N'ExpenseRequestApprovalRouteSnapshots', N'SnapshotNumber', N'int', 4, 0, 10, 0, CAST(NULL AS nvarchar(128))),
                (N'ExpenseRequestApprovalRouteSnapshots', N'WorkflowDefinitionVersion', N'int', 4, 0, 10, 0, CAST(NULL AS nvarchar(128))),
                (N'ExpenseRequestApprovalRouteSnapshots', N'OrganizationId', N'uniqueidentifier', 16, 0, 0, 0, CAST(NULL AS nvarchar(128))),
                (N'ExpenseRequestApprovalRouteSnapshots', N'CostCenterId', N'uniqueidentifier', 16, 0, 0, 0, CAST(NULL AS nvarchar(128))),
                (N'ExpenseRequestApprovalRouteSnapshots', N'PositiveDeficitKzt', N'decimal', 9, 0, 18, 2, CAST(NULL AS nvarchar(128))),
                (N'ExpenseRequestApprovalRouteSnapshots', N'CapturedAtUtc', N'datetimeoffset', 9, 0, 30, 3, CAST(NULL AS nvarchar(128))),
                (N'ExpenseRequestApprovalRouteStages', N'ExpenseRequestId', N'uniqueidentifier', 16, 0, 0, 0, CAST(NULL AS nvarchar(128))),
                (N'ExpenseRequestApprovalRouteStages', N'SnapshotNumber', N'int', 4, 0, 10, 0, CAST(NULL AS nvarchar(128))),
                (N'ExpenseRequestApprovalRouteStages', N'StageNumber', N'int', 4, 0, 10, 0, CAST(NULL AS nvarchar(128))),
                (N'ExpenseRequestApprovalRouteStages', N'RoleCode', N'nvarchar', 60, 0, 0, 0, N'Latin1_General_100_BIN2'),
                (N'ExpenseRequestApprovalRouteStageParticipants', N'ExpenseRequestId', N'uniqueidentifier', 16, 0, 0, 0, CAST(NULL AS nvarchar(128))),
                (N'ExpenseRequestApprovalRouteStageParticipants', N'SnapshotNumber', N'int', 4, 0, 10, 0, CAST(NULL AS nvarchar(128))),
                (N'ExpenseRequestApprovalRouteStageParticipants', N'StageNumber', N'int', 4, 0, 10, 0, CAST(NULL AS nvarchar(128))),
                (N'ExpenseRequestApprovalRouteStageParticipants', N'EmployeeId', N'uniqueidentifier', 16, 0, 0, 0, CAST(NULL AS nvarchar(128)))
        ) AS expected(TableName, ColumnName, TypeName, MaxLength, IsNullable, [Precision], Scale, CollationName)
        LEFT JOIN sys.columns AS actual
          ON actual.object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
         AND actual.name = expected.ColumnName
        WHERE actual.object_id IS NULL
           OR TYPE_NAME(actual.user_type_id) <> expected.TypeName
           OR actual.max_length <> expected.MaxLength
           OR actual.is_nullable <> expected.IsNullable
           OR actual.[precision] <> expected.[Precision]
           OR actual.scale <> expected.Scale
           OR actual.is_computed <> 0 OR actual.is_identity <> 0
           OR actual.is_rowguidcol <> 0 OR actual.is_filestream <> 0
           OR actual.default_object_id <> 0 OR actual.rule_object_id <> 0
           OR actual.generated_always_type <> 0
           OR actual.is_sparse <> 0 OR actual.is_column_set <> 0
           OR actual.encryption_type IS NOT NULL OR actual.is_hidden <> 0
           OR actual.is_masked <> 0
           OR
           (
               expected.CollationName IS NULL AND actual.collation_name IS NOT NULL
               OR expected.CollationName IS NOT NULL AND
                  actual.collation_name COLLATE Latin1_General_100_BIN2 <>
                  expected.CollationName COLLATE Latin1_General_100_BIN2
           )
    )
       OR (SELECT COUNT(*) FROM sys.columns
           WHERE object_id = OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteSnapshots')) <> 7
       OR (SELECT COUNT(*) FROM sys.columns
           WHERE object_id = OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteStages')) <> 4
       OR (SELECT COUNT(*) FROM sys.columns
           WHERE object_id = OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteStageParticipants')) <> 4
        THROW 60907, N'BMS W409 refused: approval-route snapshot column contract is drifted.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.indexes
        WHERE object_id IN
        (
            OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteSnapshots'),
            OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteStages'),
            OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteStageParticipants')
        )
          AND index_id > 0
    ) <> 4
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (N'ExpenseRequestApprovalRouteSnapshots', N'PK_bms_web_ExpenseRequestApprovalRouteSnapshots', 1, 1, 1, 0, 2),
                   (N'ExpenseRequestApprovalRouteStages', N'PK_bms_web_ExpenseRequestApprovalRouteStages', 1, 1, 1, 0, 3),
                   (N'ExpenseRequestApprovalRouteStageParticipants', N'PK_bms_web_ExpenseRequestApprovalRouteStageParticipants', 1, 1, 1, 0, 4),
                   (N'ExpenseRequestApprovalRouteStageParticipants', N'IX_bms_web_ExpenseRequestApprovalRouteStageParticipants_Employee_Snapshot_Stage', 2, 0, 0, 0, 4)
           ) AS expected(TableName, IndexName, IndexType, IsPrimaryKey, IsUnique, IsUniqueConstraint, KeyCount)
           LEFT JOIN sys.indexes AS actual
             ON actual.object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
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
                   (N'ExpenseRequestApprovalRouteSnapshots', N'PK_bms_web_ExpenseRequestApprovalRouteSnapshots', 1, N'ExpenseRequestId'),
                   (N'ExpenseRequestApprovalRouteSnapshots', N'PK_bms_web_ExpenseRequestApprovalRouteSnapshots', 2, N'SnapshotNumber'),
                   (N'ExpenseRequestApprovalRouteStages', N'PK_bms_web_ExpenseRequestApprovalRouteStages', 1, N'ExpenseRequestId'),
                   (N'ExpenseRequestApprovalRouteStages', N'PK_bms_web_ExpenseRequestApprovalRouteStages', 2, N'SnapshotNumber'),
                   (N'ExpenseRequestApprovalRouteStages', N'PK_bms_web_ExpenseRequestApprovalRouteStages', 3, N'StageNumber'),
                   (N'ExpenseRequestApprovalRouteStageParticipants', N'PK_bms_web_ExpenseRequestApprovalRouteStageParticipants', 1, N'ExpenseRequestId'),
                   (N'ExpenseRequestApprovalRouteStageParticipants', N'PK_bms_web_ExpenseRequestApprovalRouteStageParticipants', 2, N'SnapshotNumber'),
                   (N'ExpenseRequestApprovalRouteStageParticipants', N'PK_bms_web_ExpenseRequestApprovalRouteStageParticipants', 3, N'StageNumber'),
                   (N'ExpenseRequestApprovalRouteStageParticipants', N'PK_bms_web_ExpenseRequestApprovalRouteStageParticipants', 4, N'EmployeeId'),
                   (N'ExpenseRequestApprovalRouteStageParticipants', N'IX_bms_web_ExpenseRequestApprovalRouteStageParticipants_Employee_Snapshot_Stage', 1, N'EmployeeId'),
                   (N'ExpenseRequestApprovalRouteStageParticipants', N'IX_bms_web_ExpenseRequestApprovalRouteStageParticipants_Employee_Snapshot_Stage', 2, N'ExpenseRequestId'),
                   (N'ExpenseRequestApprovalRouteStageParticipants', N'IX_bms_web_ExpenseRequestApprovalRouteStageParticipants_Employee_Snapshot_Stage', 3, N'SnapshotNumber'),
                   (N'ExpenseRequestApprovalRouteStageParticipants', N'IX_bms_web_ExpenseRequestApprovalRouteStageParticipants_Employee_Snapshot_Stage', 4, N'StageNumber')
           ) AS expected(TableName, IndexName, KeyOrdinal, ColumnName)
           LEFT JOIN sys.indexes AS actual_index
             ON actual_index.object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
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
           WHERE object_id IN
           (
               OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteSnapshots'),
               OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteStages'),
               OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteStageParticipants')
           )
             AND is_included_column <> 0
       )
        THROW 60908, N'BMS W409 refused: approval-route snapshot index contract is drifted.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.foreign_keys
        WHERE parent_object_id IN
        (
            OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteSnapshots'),
            OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteStages'),
            OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteStageParticipants')
        )
    ) <> 6
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (N'ExpenseRequestApprovalRouteSnapshots', N'FK_bms_web_ExpenseRequestApprovalRouteSnapshots_ExpenseRequest', 1, N'ExpenseRequestId', N'ExpenseRequests', N'Id'),
                   (N'ExpenseRequestApprovalRouteSnapshots', N'FK_bms_web_ExpenseRequestApprovalRouteSnapshots_Organization', 1, N'OrganizationId', N'Organizations', N'Id'),
                   (N'ExpenseRequestApprovalRouteSnapshots', N'FK_bms_web_ExpenseRequestApprovalRouteSnapshots_CostCenter', 1, N'CostCenterId', N'CostCenters', N'Id'),
                   (N'ExpenseRequestApprovalRouteStages', N'FK_bms_web_ExpenseRequestApprovalRouteStages_Snapshot', 1, N'ExpenseRequestId', N'ExpenseRequestApprovalRouteSnapshots', N'ExpenseRequestId'),
                   (N'ExpenseRequestApprovalRouteStages', N'FK_bms_web_ExpenseRequestApprovalRouteStages_Snapshot', 2, N'SnapshotNumber', N'ExpenseRequestApprovalRouteSnapshots', N'SnapshotNumber'),
                   (N'ExpenseRequestApprovalRouteStageParticipants', N'FK_bms_web_ExpenseRequestApprovalRouteStageParticipants_Stage', 1, N'ExpenseRequestId', N'ExpenseRequestApprovalRouteStages', N'ExpenseRequestId'),
                   (N'ExpenseRequestApprovalRouteStageParticipants', N'FK_bms_web_ExpenseRequestApprovalRouteStageParticipants_Stage', 2, N'SnapshotNumber', N'ExpenseRequestApprovalRouteStages', N'SnapshotNumber'),
                   (N'ExpenseRequestApprovalRouteStageParticipants', N'FK_bms_web_ExpenseRequestApprovalRouteStageParticipants_Stage', 3, N'StageNumber', N'ExpenseRequestApprovalRouteStages', N'StageNumber'),
                   (N'ExpenseRequestApprovalRouteStageParticipants', N'FK_bms_web_ExpenseRequestApprovalRouteStageParticipants_Employee', 1, N'EmployeeId', N'Employees', N'Id')
           ) AS expected(TableName, ForeignKeyName, Ordinal, ParentColumnName, ReferencedTableName, ReferencedColumnName)
           LEFT JOIN sys.foreign_keys AS actual_fk
             ON actual_fk.parent_object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
            AND actual_fk.name = expected.ForeignKeyName
           LEFT JOIN sys.foreign_key_columns AS actual_fkc
             ON actual_fkc.constraint_object_id = actual_fk.object_id
            AND actual_fkc.constraint_column_id = expected.Ordinal
           LEFT JOIN sys.columns AS actual_parent_column
             ON actual_parent_column.object_id = actual_fkc.parent_object_id
            AND actual_parent_column.column_id = actual_fkc.parent_column_id
           LEFT JOIN sys.tables AS actual_referenced_table
             ON actual_referenced_table.object_id = actual_fkc.referenced_object_id
           LEFT JOIN sys.columns AS actual_referenced_column
             ON actual_referenced_column.object_id = actual_fkc.referenced_object_id
            AND actual_referenced_column.column_id = actual_fkc.referenced_column_id
           WHERE actual_fk.object_id IS NULL
              OR actual_fk.is_disabled <> 0 OR actual_fk.is_not_trusted <> 0
              OR actual_fk.is_not_for_replication <> 0 OR actual_fk.is_system_named <> 0
              OR actual_fk.delete_referential_action <> 0 OR actual_fk.update_referential_action <> 0
              OR actual_parent_column.name <> expected.ParentColumnName
              OR actual_referenced_table.name <> expected.ReferencedTableName
              OR actual_referenced_column.name <> expected.ReferencedColumnName
       )
       OR (SELECT COUNT(*)
           FROM sys.foreign_key_columns
           WHERE parent_object_id IN
           (
               OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteSnapshots'),
               OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteStages'),
               OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteStageParticipants')
           )) <> 9
        THROW 60909, N'BMS W409 refused: approval-route snapshot foreign-key contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id IN
        (
            OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteSnapshots'),
            OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteStages'),
            OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteStageParticipants')
        )
    )
       OR EXISTS
       (
           SELECT 1
           FROM sys.triggers
           WHERE parent_id IN
           (
               OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteSnapshots'),
               OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteStages'),
               OBJECT_ID(N'bms_web.ExpenseRequestApprovalRouteStageParticipants')
           )
       )
        THROW 60910, N'BMS W409 refused: approval-route snapshot storage must have no CHECK constraints or triggers.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 22
    )
        INSERT INTO bms_web.schema_version (version_no, script_name)
        VALUES (22, N'022_expense_request_approval_route_snapshots.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
