-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    SQL036: durable watermark for complete identity-reference snapshots.
    Exact SQL001-SQL035 provenance is required. The table may be empty until
    the first successful import; its single row is written last in the same
    serializable transaction as the five identity-reference projections.
    No reference data is seeded or changed by this migration.
*/
IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS identity-reference watermark refused: run SQL036 outside an ambient transaction.', 16, 1);
    RETURN;
END;

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 63701, N'BMS identity-reference watermark refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 63702, N'BMS identity-reference watermark refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.Employees', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.FunctionalRoles', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.EmployeeFunctionalRoles', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.CostCenters', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.BudgetOwnerCostCenterAssignments', N'U') IS NULL
    THROW 63703, N'BMS identity-reference watermark refused: required managed tables are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

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
        (35, N'035_budget_instance_approval.sql');

    IF
    (
        SELECT COUNT(*)
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no BETWEEN 1 AND 35
    ) <> 35
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
        THROW 63705, N'BMS identity-reference watermark refused: exact SQL001-SQL035 predecessors are required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 36
          AND
          (
              script_name COLLATE Latin1_General_100_BIN2 <>
                  N'036_identity_reference_import_watermark.sql'
              OR DATALENGTH(script_name) <>
                  DATALENGTH(N'036_identity_reference_import_watermark.sql')
          )
    )
        THROW 63706, N'BMS identity-reference watermark refused: SQL036 belongs to another script.', 1;

    DECLARE @VersionRegistered bit = CASE WHEN EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 36
    ) THEN 1 ELSE 0 END;

    IF @VersionRegistered = 0
       AND OBJECT_ID(N'bms_web.NsiIdentityReferenceImportWatermark', N'U') IS NOT NULL
        THROW 63707, N'BMS identity-reference watermark refused: an unmanaged target table already exists.', 1;

    -- A fully migrated database legitimately has SQL037 after SQL036. Once
    -- SQL036 is registered, a repeat is a no-op and must remain idempotent.
    -- For a missing registration, keep the ordering guard before creating a
    -- new target, while the unmanaged-target check above reports its precise
    -- contract violation first.
    IF @VersionRegistered = 0
       AND EXISTS
       (
           SELECT 1
           FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
           WHERE version_no > 36
       )
        THROW 63704, N'BMS identity-reference watermark refused: a future schema version is registered.', 1;

    IF @VersionRegistered = 0
    BEGIN
        CREATE TABLE bms_web.NsiIdentityReferenceImportWatermark
        (
            SingletonId tinyint NOT NULL,
            AsOf date NOT NULL,
            CapturedAtUtc datetimeoffset(7) NOT NULL,
            SourceConfigurationSha256 char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
            SourceSnapshotSha256 char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
            CONSTRAINT PK_bms_web_NsiIdentityReferenceImportWatermark
                PRIMARY KEY CLUSTERED (SingletonId ASC),
            CONSTRAINT CK_bms_web_NsiIdentityReferenceImportWatermark_Singleton
                CHECK (SingletonId = 1),
            CONSTRAINT CK_bms_web_NsiIdentityReferenceImportWatermark_AsOf
                CHECK (AsOf <> CONVERT(date, '00010101', 112)),
            CONSTRAINT CK_bms_web_NsiIdentityReferenceImportWatermark_CapturedAtUtc
                CHECK (DATEPART(TZOFFSET, CapturedAtUtc) = 0),
            CONSTRAINT CK_bms_web_NsiIdentityReferenceImportWatermark_CapturedAtUtcNotDefault
                CHECK
                (
                    CapturedAtUtc <>
                        CONVERT(datetimeoffset(7), '0001-01-01T00:00:00+00:00', 127)
                ),
            CONSTRAINT CK_bms_web_NsiIdentityReferenceImportWatermark_ConfigurationHash
                CHECK
                (
                    LEN(SourceConfigurationSha256) = 64
                    AND DATALENGTH(SourceConfigurationSha256) = 64
                    AND SourceConfigurationSha256 NOT LIKE '%[^0-9A-F]%'
                        COLLATE Latin1_General_100_BIN2
                ),
            CONSTRAINT CK_bms_web_NsiIdentityReferenceImportWatermark_SnapshotHash
                CHECK
                (
                    LEN(SourceSnapshotSha256) = 64
                    AND DATALENGTH(SourceSnapshotSha256) = 64
                    AND SourceSnapshotSha256 NOT LIKE '%[^0-9A-F]%'
                        COLLATE Latin1_General_100_BIN2
                )
        );
    END;

    DECLARE @WatermarkId int =
        OBJECT_ID(N'bms_web.NsiIdentityReferenceImportWatermark', N'U');

    IF @WatermarkId IS NULL
        THROW 63708, N'BMS identity-reference watermark refused: target table is absent.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'SingletonId', N'tinyint', 1, 3, 0, 0, CAST(NULL AS nvarchar(128))),
                (N'AsOf', N'date', 3, 10, 0, 0, CAST(NULL AS nvarchar(128))),
                (N'CapturedAtUtc', N'datetimeoffset', 10, 34, 7, 0, CAST(NULL AS nvarchar(128))),
                (N'SourceConfigurationSha256', N'char', 64, 0, 0, 0, N'Latin1_General_100_BIN2'),
                (N'SourceSnapshotSha256', N'char', 64, 0, 0, 0, N'Latin1_General_100_BIN2')
        ) AS expected(ColumnName, TypeName, MaxLength, PrecisionValue, ScaleValue, IsNullable, CollationName)
        LEFT JOIN sys.columns AS actual
          ON actual.object_id = @WatermarkId
         AND actual.name = expected.ColumnName
        WHERE actual.object_id IS NULL
           OR TYPE_NAME(actual.user_type_id) <> expected.TypeName
           OR actual.max_length <> expected.MaxLength
           OR actual.precision <> expected.PrecisionValue
           OR actual.scale <> expected.ScaleValue
           OR actual.is_nullable <> expected.IsNullable
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
       OR (SELECT COUNT(*) FROM sys.columns WHERE object_id = @WatermarkId) <> 5
        THROW 63709, N'BMS identity-reference watermark refused: column contract is drifted.', 1;

    IF (SELECT COUNT(*) FROM sys.indexes WHERE object_id = @WatermarkId AND index_id > 0) <> 1
       OR NOT EXISTS
       (
           SELECT 1
           FROM sys.indexes AS index_definition
           WHERE index_definition.object_id = @WatermarkId
             AND index_definition.name = N'PK_bms_web_NsiIdentityReferenceImportWatermark'
             AND index_definition.type = 1
             AND index_definition.is_primary_key = 1
             AND index_definition.is_unique = 1
             AND index_definition.is_unique_constraint = 0
             AND index_definition.is_disabled = 0
             AND index_definition.is_hypothetical = 0
             AND index_definition.has_filter = 0
             AND index_definition.filter_definition IS NULL
             AND (SELECT COUNT(*) FROM sys.index_columns AS key_column
                  WHERE key_column.object_id = @WatermarkId
                    AND key_column.index_id = index_definition.index_id
                    AND key_column.key_ordinal > 0) = 1
             AND EXISTS
             (
                 SELECT 1
                 FROM sys.index_columns AS key_column
                 INNER JOIN sys.columns AS column_definition
                   ON column_definition.object_id = key_column.object_id
                  AND column_definition.column_id = key_column.column_id
                 WHERE key_column.object_id = @WatermarkId
                   AND key_column.index_id = index_definition.index_id
                   AND key_column.key_ordinal = 1
                   AND key_column.is_descending_key = 0
                   AND key_column.is_included_column = 0
                   AND column_definition.name = N'SingletonId'
             )
       )
        THROW 63710, N'BMS identity-reference watermark refused: primary-key contract is drifted.', 1;

    DECLARE @ExpectedChecks TABLE
    (
        ConstraintName sysname NOT NULL PRIMARY KEY,
        Definition nvarchar(max) NOT NULL
    );

    INSERT INTO @ExpectedChecks (ConstraintName, Definition)
    VALUES
        (N'CK_bms_web_NsiIdentityReferenceImportWatermark_AsOf',
            N'([AsOf]<>CONVERT([date],''00010101'',(112)))'),
        (N'CK_bms_web_NsiIdentityReferenceImportWatermark_CapturedAtUtc',
            N'(datepart(tzoffset,[CapturedAtUtc])=(0))'),
        (N'CK_bms_web_NsiIdentityReferenceImportWatermark_CapturedAtUtcNotDefault',
            N'([CapturedAtUtc]<>CONVERT([datetimeoffset](7),''0001-01-01T00:00:00+00:00'',(127)))'),
        (N'CK_bms_web_NsiIdentityReferenceImportWatermark_ConfigurationHash',
            N'(len([SourceConfigurationSha256])=(64) AND datalength([SourceConfigurationSha256])=(64) AND NOT [SourceConfigurationSha256] like (''%[^0-9A-F]%'') collate Latin1_General_100_BIN2)'),
        (N'CK_bms_web_NsiIdentityReferenceImportWatermark_Singleton',
            N'([SingletonId]=(1))'),
        (N'CK_bms_web_NsiIdentityReferenceImportWatermark_SnapshotHash',
            N'(len([SourceSnapshotSha256])=(64) AND datalength([SourceSnapshotSha256])=(64) AND NOT [SourceSnapshotSha256] like (''%[^0-9A-F]%'') collate Latin1_General_100_BIN2)');

    IF (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = @WatermarkId) <> 6
       OR EXISTS
       (
           SELECT 1
           FROM @ExpectedChecks AS expected
           LEFT JOIN sys.check_constraints AS actual
             ON actual.parent_object_id = @WatermarkId
            AND actual.name = expected.ConstraintName
           WHERE actual.object_id IS NULL
              OR actual.is_disabled <> 0
              OR actual.is_not_trusted <> 0
              OR actual.definition COLLATE Latin1_General_100_BIN2 <>
                 expected.Definition COLLATE Latin1_General_100_BIN2
              OR DATALENGTH(actual.definition) <> DATALENGTH(expected.Definition)
       )
        THROW 63711, N'BMS identity-reference watermark refused: check-constraint contract is drifted.', 1;

    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = @WatermarkId)
       OR EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = @WatermarkId)
       OR (SELECT COUNT_BIG(*) FROM bms_web.NsiIdentityReferenceImportWatermark WITH (HOLDLOCK)) > 1
        THROW 63712, N'BMS identity-reference watermark refused: table isolation contract is drifted.', 1;

    IF @VersionRegistered = 0
        INSERT INTO bms_web.schema_version (version_no, script_name)
        VALUES (36, N'036_identity_reference_import_watermark.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
