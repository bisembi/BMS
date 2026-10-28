-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    SQL038: automatic business numbering for expense requests.
    The number is RZ-(CFO code)-00001, with an independent monotonic sequence
    for every CFO. Historical requests remain unnumbered; this migration never
    invents numbers from existing rows.
*/
IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS expense-request numbering refused: run SQL038 outside an ambient transaction.', 16, 1);
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
    THROW 63821, N'BMS expense-request numbering refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 63822, N'BMS expense-request numbering refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.ExpenseRequests', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.CostCenters', N'U') IS NULL
    THROW 63823, N'BMS expense-request numbering refused: required managed tables are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no > 38
    )
        THROW 63824, N'BMS expense-request numbering refused: a future schema version is registered.', 1;

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
        (36, N'036_identity_reference_import_watermark.sql'),
        (37, N'037_budget_instance_line_comment.sql');

    IF
    (
        SELECT COUNT(*)
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no <> 38
    ) <> 37
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
        THROW 63825, N'BMS expense-request numbering refused: exact SQL001-SQL037 predecessors are required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 38
          AND
          (
              script_name COLLATE Latin1_General_100_BIN2 <>
                  N'038_expense_request_numbering.sql'
              OR DATALENGTH(script_name) <>
                  DATALENGTH(N'038_expense_request_numbering.sql')
          )
    )
        THROW 63826, N'BMS expense-request numbering refused: SQL038 belongs to another script.', 1;

    DECLARE @VersionRegistered bit = CASE WHEN EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 38
    ) THEN 1 ELSE 0 END;

    DECLARE @ExpenseRequestsId int = OBJECT_ID(N'bms_web.ExpenseRequests', N'U');
    DECLARE @CostCentersId int = OBJECT_ID(N'bms_web.CostCenters', N'U');
    DECLARE @CountersId int = OBJECT_ID(N'bms_web.ExpenseRequestCostCenterCounters', N'U');

    IF @VersionRegistered = 0
       AND EXISTS
       (
           SELECT 1
           FROM sys.columns
           WHERE object_id = @ExpenseRequestsId AND name = N'RequestNumber'
       )
        THROW 63827, N'BMS expense-request numbering refused: an unmanaged RequestNumber column already exists.', 1;

    IF @VersionRegistered = 0
       AND @CountersId IS NOT NULL
        THROW 63828, N'BMS expense-request numbering refused: an unmanaged counter table already exists.', 1;

    IF @VersionRegistered = 0
    BEGIN
        ALTER TABLE bms_web.ExpenseRequests
            ADD RequestNumber nvarchar(160) COLLATE Latin1_General_100_BIN2 NULL;

        -- SQL Server compiles a static batch before the preceding ALTER TABLE
        -- adds RequestNumber. Compile the filtered index in a new batch so the
        -- migration works on a database that is exactly at version 37.
        EXEC(N'CREATE UNIQUE INDEX UX_bms_web_ExpenseRequests_RequestNumber
            ON bms_web.ExpenseRequests (RequestNumber)
            WHERE RequestNumber IS NOT NULL;');

        CREATE TABLE bms_web.ExpenseRequestCostCenterCounters
        (
            CostCenterId uniqueidentifier NOT NULL,
            Prefix nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL,
            LastIssuedOrdinal bigint NOT NULL,
            CONSTRAINT PK_bms_web_ExpenseRequestCostCenterCounters
                PRIMARY KEY CLUSTERED (CostCenterId),
            CONSTRAINT CK_bms_web_ExpenseRequestCostCenterCounters_ordinal
                CHECK (LastIssuedOrdinal >= 0),
            CONSTRAINT FK_bms_web_ExpenseRequestCostCenterCounters_CostCenter
                FOREIGN KEY (CostCenterId)
                REFERENCES bms_web.CostCenters (Id)
                ON DELETE NO ACTION
                ON UPDATE NO ACTION
        );
    END;

    SET @CountersId = OBJECT_ID(N'bms_web.ExpenseRequestCostCenterCounters', N'U');

    IF @ExpenseRequestsId IS NULL OR @CostCentersId IS NULL OR @CountersId IS NULL
       OR EXISTS
       (
           SELECT 1
           FROM sys.tables
           WHERE object_id IN (@ExpenseRequestsId, @CountersId)
             AND (temporal_type <> 0 OR is_memory_optimized <> 0 OR is_filetable <> 0)
       )
       OR EXISTS
       (
           SELECT 1
           FROM sys.triggers
           WHERE parent_id IN (@ExpenseRequestsId, @CountersId)
             AND is_ms_shipped = 0
       )
        THROW 63829, N'BMS expense-request numbering refused: table shape is not owned and plain.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.columns AS column_definition
        WHERE column_definition.object_id = @ExpenseRequestsId
          AND column_definition.name = N'RequestNumber'
          AND
          (
              TYPE_NAME(column_definition.user_type_id) <> N'nvarchar'
              OR column_definition.max_length <> 320
              OR column_definition.is_nullable <> 1
              OR column_definition.collation_name <> N'Latin1_General_100_BIN2'
              OR column_definition.is_identity <> 0
              OR column_definition.is_computed <> 0
              OR column_definition.default_object_id <> 0
              OR column_definition.rule_object_id <> 0
              OR column_definition.generated_always_type <> 0
              OR column_definition.encryption_type IS NOT NULL
              OR column_definition.is_hidden <> 0
              OR column_definition.is_masked <> 0
          )
    )
       OR NOT EXISTS
       (
           SELECT 1
           FROM sys.columns
           WHERE object_id = @ExpenseRequestsId AND name = N'RequestNumber'
       )
        THROW 63830, N'BMS expense-request numbering refused: RequestNumber column contract is drifted.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.indexes
        WHERE object_id = @ExpenseRequestsId
          AND name = N'UX_bms_web_ExpenseRequests_RequestNumber'
    ) <> 1
       OR EXISTS
       (
           SELECT 1
           FROM sys.indexes AS index_definition
           WHERE index_definition.object_id = @ExpenseRequestsId
             AND index_definition.name = N'UX_bms_web_ExpenseRequests_RequestNumber'
             AND
             (
                 index_definition.type <> 2
                 OR index_definition.is_unique <> 1
                 OR index_definition.is_unique_constraint <> 0
                 OR index_definition.is_primary_key <> 0
                 OR index_definition.is_disabled <> 0
                 OR index_definition.is_hypothetical <> 0
                 OR index_definition.has_filter <> 1
                 OR LOWER(REPLACE(REPLACE(REPLACE(ISNULL(index_definition.filter_definition, N''), N' ', N''), N'(', N''), N')', N''))
                    COLLATE Latin1_General_100_BIN2 <> N'[requestnumber]isnotnull'
                 OR (SELECT COUNT(*) FROM sys.index_columns AS key_column
                     WHERE key_column.object_id = index_definition.object_id
                       AND key_column.index_id = index_definition.index_id
                       AND key_column.key_ordinal > 0) <> 1
                 OR EXISTS
                    (
                        SELECT 1
                        FROM sys.index_columns AS key_column
                        INNER JOIN sys.columns AS column_definition
                          ON column_definition.object_id = key_column.object_id
                         AND column_definition.column_id = key_column.column_id
                        WHERE key_column.object_id = index_definition.object_id
                          AND key_column.index_id = index_definition.index_id
                          AND key_column.key_ordinal = 1
                          AND (key_column.is_included_column <> 0
                               OR key_column.is_descending_key <> 0
                               OR column_definition.name <> N'RequestNumber')
                    )
             )
       )
        THROW 63831, N'BMS expense-request numbering refused: filtered unique index contract is drifted.', 1;

    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = @CountersId) <> 3
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (1, N'CostCenterId', N'uniqueidentifier', 16, 0, 0, 0, CAST(NULL AS nvarchar(128))),
                   (2, N'Prefix', N'nvarchar', 200, 0, 0, 0, N'Latin1_General_100_BIN2'),
                   (3, N'LastIssuedOrdinal', N'bigint', 8, 19, 0, 0, CAST(NULL AS nvarchar(128)))
           ) AS expected(Ordinal, ColumnName, TypeName, MaxLength, PrecisionValue, ScaleValue, IsNullable, CollationName)
           LEFT JOIN sys.columns AS actual
             ON actual.object_id = @CountersId AND actual.column_id = expected.Ordinal
           WHERE actual.column_id IS NULL
              OR actual.name <> expected.ColumnName
              OR TYPE_NAME(actual.user_type_id) <> expected.TypeName
              OR actual.max_length <> expected.MaxLength
              OR actual.precision <> expected.PrecisionValue
              OR actual.scale <> expected.ScaleValue
              OR actual.is_nullable <> expected.IsNullable
              OR ISNULL(actual.collation_name, N'') <> ISNULL(expected.CollationName, N'')
              OR actual.is_identity <> 0
              OR actual.is_computed <> 0
              OR actual.default_object_id <> 0
              OR actual.rule_object_id <> 0
              OR actual.generated_always_type <> 0
              OR actual.encryption_type IS NOT NULL
              OR actual.is_hidden <> 0
              OR actual.is_masked <> 0
       )
        THROW 63832, N'BMS expense-request numbering refused: counter columns are drifted.', 1;

    IF (SELECT COUNT(*) FROM sys.indexes WHERE object_id = @CountersId AND index_id > 0) <> 1
       OR NOT EXISTS
       (
           SELECT 1
           FROM sys.indexes AS index_definition
           WHERE index_definition.object_id = @CountersId
             AND index_definition.name = N'PK_bms_web_ExpenseRequestCostCenterCounters'
             AND index_definition.type = 1
             AND index_definition.is_primary_key = 1
             AND index_definition.is_unique = 1
             AND index_definition.has_filter = 0
             AND (SELECT COUNT(*) FROM sys.index_columns AS key_column
                  WHERE key_column.object_id = index_definition.object_id
                    AND key_column.index_id = index_definition.index_id
                    AND key_column.key_ordinal > 0) = 1
             AND EXISTS
             (
                 SELECT 1
                 FROM sys.index_columns AS key_column
                 INNER JOIN sys.columns AS column_definition
                   ON column_definition.object_id = key_column.object_id
                  AND column_definition.column_id = key_column.column_id
                 WHERE key_column.object_id = index_definition.object_id
                   AND key_column.index_id = index_definition.index_id
                   AND key_column.key_ordinal = 1
                   AND key_column.is_included_column = 0
                   AND key_column.is_descending_key = 0
                   AND column_definition.name = N'CostCenterId'
             )
       )
        THROW 63833, N'BMS expense-request numbering refused: counter primary key is drifted.', 1;

    IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = @CountersId) <> 1
       OR NOT EXISTS
       (
           SELECT 1
           FROM sys.foreign_keys AS foreign_key
           WHERE foreign_key.parent_object_id = @CountersId
             AND foreign_key.name = N'FK_bms_web_ExpenseRequestCostCenterCounters_CostCenter'
             AND foreign_key.referenced_object_id = @CostCentersId
             AND foreign_key.delete_referential_action = 0
             AND foreign_key.update_referential_action = 0
             AND (SELECT COUNT(*) FROM sys.foreign_key_columns AS key_column
                  WHERE key_column.constraint_object_id = foreign_key.object_id) = 1
             AND EXISTS
             (
                 SELECT 1
                 FROM sys.foreign_key_columns AS key_column
                 INNER JOIN sys.columns AS parent_column
                   ON parent_column.object_id = key_column.parent_object_id
                  AND parent_column.column_id = key_column.parent_column_id
                 INNER JOIN sys.columns AS referenced_column
                   ON referenced_column.object_id = key_column.referenced_object_id
                  AND referenced_column.column_id = key_column.referenced_column_id
                 WHERE key_column.constraint_object_id = foreign_key.object_id
                   AND parent_column.name = N'CostCenterId'
                   AND referenced_column.name = N'Id'
             )
       )
        THROW 63834, N'BMS expense-request numbering refused: counter foreign key is drifted.', 1;

    IF (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = @CountersId) <> 1
       OR NOT EXISTS
       (
           SELECT 1
           FROM sys.check_constraints AS check_constraint
           WHERE check_constraint.parent_object_id = @CountersId
             AND check_constraint.name = N'CK_bms_web_ExpenseRequestCostCenterCounters_ordinal'
             AND check_constraint.is_disabled = 0
             AND check_constraint.is_not_trusted = 0
             AND LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(check_constraint.definition, N''), N' ', N''), N'(', N''), N')', N''), N'[', N''), N']', N''))
                 COLLATE Latin1_General_100_BIN2 = N'lastissuedordinal>=0'
       )
        THROW 63835, N'BMS expense-request numbering refused: counter check constraint is drifted.', 1;

    IF @VersionRegistered = 0
        INSERT INTO bms_web.schema_version (version_no, script_name)
        VALUES (38, N'038_expense_request_numbering.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
