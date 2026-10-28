-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    W603: dedicated in-app red-zone notification queue and recipient issues.
    Standalone, after SQL001-SQL026. No external delivery, role grants, payment
    intake, historical backfill, or changes to the workflow notification queue.
    Existing unregistered or drifted objects are refused, never overwritten.
*/
IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS W603 refused: run SQL027 outside an ambient transaction.', 16, 1);
    RETURN;
END;
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 62301, N'BMS W603 refused: current database must be the explicitly owned BMS database.', 1;
IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 62302, N'BMS W603 refused: current database is not READ_WRITE.', 1;
IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.ExpenseRequests', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.ExpenseRequestAccrualClosingCycles', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.Employees', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.EmployeeActorMappings', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.Organizations', N'U') IS NULL
    THROW 62303, N'BMS W603 refused: required source tables are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK) WHERE version_no > 27)
        THROW 62304, N'BMS W603 refused: a future schema version is registered.', 1;
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
                (26, N'026_expense_request_closing_activation.sql')
        ) AS expected(version_no, script_name)
        JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK)
          ON actual.version_no = expected.version_no
         AND actual.script_name COLLATE Latin1_General_100_BIN2 =
             expected.script_name COLLATE Latin1_General_100_BIN2
         AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
    ) <> 26
        THROW 62305, N'BMS W603 refused: exact schema versions 1 through 26 are required.', 1;
    IF EXISTS
    (
        SELECT 1 FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 27
          AND (script_name COLLATE Latin1_General_100_BIN2 <>
                N'027_accrual_red_zone_notifications.sql'
               OR DATALENGTH(script_name) <> DATALENGTH(N'027_accrual_red_zone_notifications.sql'))
    )
        THROW 62306, N'BMS W603 refused: schema version 27 belongs to another script.', 1;

    DECLARE @AlreadyApplied bit = CASE WHEN EXISTS
        (SELECT 1 FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK) WHERE version_no = 27)
        THEN 1 ELSE 0 END;
    IF (@AlreadyApplied = 0 AND
          (OBJECT_ID(N'bms_web.AccrualNotifications') IS NOT NULL
           OR OBJECT_ID(N'bms_web.AccrualNotificationIssues') IS NOT NULL))
       OR (@AlreadyApplied = 1 AND
          (OBJECT_ID(N'bms_web.AccrualNotifications', N'U') IS NULL
           OR OBJECT_ID(N'bms_web.AccrualNotificationIssues', N'U') IS NULL))
        THROW 62307, N'BMS W603 refused: notification registration and objects disagree.', 1;

    IF @AlreadyApplied = 0
    BEGIN
        CREATE TABLE bms_web.AccrualNotifications
        (
            Id uniqueidentifier NOT NULL,
            NotificationType nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
            ExpenseRequestId uniqueidentifier NOT NULL,
            AccrualCycleId uniqueidentifier NOT NULL,
            RecipientEmployeeId uniqueidentifier NOT NULL,
            OrganizationId uniqueidentifier NOT NULL,
            RecipientWasInitiator bit NOT NULL,
            RecipientWasResponsibleAccountant bit NOT NULL,
            CreatedAtUtc datetimeoffset(3) NOT NULL,
            FullyPaidAtUtc datetimeoffset(3) NOT NULL,
            PaymentBusinessDate date NOT NULL,
            AsOfDate date NOT NULL,
            AgeDays int NOT NULL,
            RedFromDays int NOT NULL,
            RemainingAmountKzt decimal(18, 2) NOT NULL,
            CONSTRAINT PK_bms_web_AccrualNotifications PRIMARY KEY CLUSTERED (Id),
            CONSTRAINT FK_bms_web_AccrualNotifications_Cycle
                FOREIGN KEY (ExpenseRequestId, AccrualCycleId)
                REFERENCES bms_web.ExpenseRequestAccrualClosingCycles (ExpenseRequestId, CycleId),
            CONSTRAINT FK_bms_web_AccrualNotifications_Employee
                FOREIGN KEY (RecipientEmployeeId) REFERENCES bms_web.Employees (Id),
            CONSTRAINT FK_bms_web_AccrualNotifications_Organization
                FOREIGN KEY (OrganizationId) REFERENCES bms_web.Organizations (Id),
            CONSTRAINT CK_bms_web_AccrualNotifications_Identity CHECK
            (
                Id <> '00000000-0000-0000-0000-000000000000'
                AND ExpenseRequestId <> '00000000-0000-0000-0000-000000000000'
                AND AccrualCycleId <> '00000000-0000-0000-0000-000000000000'
                AND RecipientEmployeeId <> '00000000-0000-0000-0000-000000000000'
                AND OrganizationId <> '00000000-0000-0000-0000-000000000000'
                AND NotificationType = N'ACCRUAL_RED_ZONE'
                AND DATALENGTH(NotificationType) = 32
                AND (RecipientWasInitiator = 1 OR RecipientWasResponsibleAccountant = 1)
            ),
            CONSTRAINT CK_bms_web_AccrualNotifications_Age CHECK
            (
                RedFromDays > 0 AND AgeDays >= RedFromDays
                AND DATEDIFF(day, PaymentBusinessDate, AsOfDate) = AgeDays
                AND RemainingAmountKzt > 0
            ),
            CONSTRAINT CK_bms_web_AccrualNotifications_Time CHECK
            (
                DATEPART(TZOFFSET, CreatedAtUtc) = 0
                AND DATEPART(TZOFFSET, FullyPaidAtUtc) = 0
                AND CreatedAtUtc >= FullyPaidAtUtc
            )
        );
        CREATE UNIQUE NONCLUSTERED INDEX UQ_bms_web_AccrualNotifications_BusinessKey
            ON bms_web.AccrualNotifications
                (NotificationType, ExpenseRequestId, AccrualCycleId, RecipientEmployeeId);
        CREATE NONCLUSTERED INDEX IX_bms_web_AccrualNotifications_Recipient_Created
            ON bms_web.AccrualNotifications (RecipientEmployeeId, CreatedAtUtc, Id);
        CREATE NONCLUSTERED INDEX IX_bms_web_AccrualNotifications_Request_Cycle
            ON bms_web.AccrualNotifications (ExpenseRequestId, AccrualCycleId);
        CREATE NONCLUSTERED INDEX IX_bms_web_AccrualNotifications_Organization
            ON bms_web.AccrualNotifications (OrganizationId);

        CREATE TABLE bms_web.AccrualNotificationIssues
        (
            ExpenseRequestId uniqueidentifier NOT NULL,
            Code nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
            AccrualCycleId uniqueidentifier NULL,
            FirstDetectedAtUtc datetimeoffset(3) NOT NULL,
            LastDetectedAtUtc datetimeoffset(3) NOT NULL,
            ResolvedAtUtc datetimeoffset(3) NULL,
            CONSTRAINT PK_bms_web_AccrualNotificationIssues PRIMARY KEY CLUSTERED (ExpenseRequestId, Code),
            CONSTRAINT FK_bms_web_AccrualNotificationIssues_Request
                FOREIGN KEY (ExpenseRequestId) REFERENCES bms_web.ExpenseRequests (Id),
            CONSTRAINT CK_bms_web_AccrualNotificationIssues_Identity CHECK
            (
                ExpenseRequestId <> '00000000-0000-0000-0000-000000000000'
                AND (AccrualCycleId IS NULL OR AccrualCycleId <> '00000000-0000-0000-0000-000000000000')
                AND ((Code = N'NO_RESPONSIBLE_ACCOUNTANT' AND DATALENGTH(Code) = 50)
                     OR (Code = N'INITIATOR_MAPPING_UNAVAILABLE' AND DATALENGTH(Code) = 58))
            ),
            CONSTRAINT CK_bms_web_AccrualNotificationIssues_Time CHECK
            (
                DATEPART(TZOFFSET, FirstDetectedAtUtc) = 0
                AND DATEPART(TZOFFSET, LastDetectedAtUtc) = 0
                AND LastDetectedAtUtc >= FirstDetectedAtUtc
                AND (ResolvedAtUtc IS NULL OR (DATEPART(TZOFFSET, ResolvedAtUtc) = 0
                     AND ResolvedAtUtc >= FirstDetectedAtUtc AND ResolvedAtUtc = LastDetectedAtUtc))
            )
        );
        CREATE NONCLUSTERED INDEX IX_bms_web_AccrualNotificationIssues_Unresolved
            ON bms_web.AccrualNotificationIssues (ResolvedAtUtc, Code);
    END;

    -- Complete column shape, including nullability, collation and hidden behavior.
    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'AccrualNotifications', N'Id', N'uniqueidentifier', 16, 0, 0, 0, N''),
                (N'AccrualNotifications', N'NotificationType', N'nvarchar', 64, 0, 0, 0, N'Latin1_General_100_BIN2'),
                (N'AccrualNotifications', N'ExpenseRequestId', N'uniqueidentifier', 16, 0, 0, 0, N''),
                (N'AccrualNotifications', N'AccrualCycleId', N'uniqueidentifier', 16, 0, 0, 0, N''),
                (N'AccrualNotifications', N'RecipientEmployeeId', N'uniqueidentifier', 16, 0, 0, 0, N''),
                (N'AccrualNotifications', N'OrganizationId', N'uniqueidentifier', 16, 0, 0, 0, N''),
                (N'AccrualNotifications', N'RecipientWasInitiator', N'bit', 1, 0, 1, 0, N''),
                (N'AccrualNotifications', N'RecipientWasResponsibleAccountant', N'bit', 1, 0, 1, 0, N''),
                (N'AccrualNotifications', N'CreatedAtUtc', N'datetimeoffset', 9, 0, 30, 3, N''),
                (N'AccrualNotifications', N'FullyPaidAtUtc', N'datetimeoffset', 9, 0, 30, 3, N''),
                (N'AccrualNotifications', N'PaymentBusinessDate', N'date', 3, 0, 10, 0, N''),
                (N'AccrualNotifications', N'AsOfDate', N'date', 3, 0, 10, 0, N''),
                (N'AccrualNotifications', N'AgeDays', N'int', 4, 0, 10, 0, N''),
                (N'AccrualNotifications', N'RedFromDays', N'int', 4, 0, 10, 0, N''),
                (N'AccrualNotifications', N'RemainingAmountKzt', N'decimal', 9, 0, 18, 2, N''),
                (N'AccrualNotificationIssues', N'ExpenseRequestId', N'uniqueidentifier', 16, 0, 0, 0, N''),
                (N'AccrualNotificationIssues', N'Code', N'nvarchar', 128, 0, 0, 0, N'Latin1_General_100_BIN2'),
                (N'AccrualNotificationIssues', N'AccrualCycleId', N'uniqueidentifier', 16, 1, 0, 0, N''),
                (N'AccrualNotificationIssues', N'FirstDetectedAtUtc', N'datetimeoffset', 9, 0, 30, 3, N''),
                (N'AccrualNotificationIssues', N'LastDetectedAtUtc', N'datetimeoffset', 9, 0, 30, 3, N''),
                (N'AccrualNotificationIssues', N'ResolvedAtUtc', N'datetimeoffset', 9, 1, 30, 3, N'')
        ) AS expected(TableName, ColumnName, TypeName, MaxLength, IsNullable, [Precision], Scale, CollationName)
        LEFT JOIN sys.columns AS actual
          ON actual.object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
         AND actual.name = expected.ColumnName
        WHERE actual.object_id IS NULL
           OR TYPE_NAME(actual.user_type_id) <> expected.TypeName
           OR actual.max_length <> expected.MaxLength OR actual.is_nullable <> expected.IsNullable
           OR actual.[precision] <> expected.[Precision] OR actual.scale <> expected.Scale
           OR ISNULL(actual.collation_name, N'') <> expected.CollationName
           OR actual.is_identity <> 0 OR actual.is_computed <> 0
           OR actual.default_object_id <> 0 OR actual.rule_object_id <> 0
           OR actual.generated_always_type <> 0 OR actual.is_rowguidcol <> 0
           OR actual.is_filestream <> 0 OR actual.is_sparse <> 0 OR actual.is_column_set <> 0
           OR actual.encryption_type IS NOT NULL OR actual.is_hidden <> 0 OR actual.is_masked <> 0
    )
       OR (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'bms_web.AccrualNotifications')) <> 15
       OR (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'bms_web.AccrualNotificationIssues')) <> 6
        THROW 62308, N'BMS W603 refused: notification column contract is drifted.', 1;

    DECLARE @ExpectedIndexes TABLE
        (TableName sysname, IndexName sysname, IsUnique bit, IsPrimaryKey bit, IndexType tinyint);
    INSERT INTO @ExpectedIndexes VALUES
        (N'AccrualNotifications', N'PK_bms_web_AccrualNotifications', 1, 1, 1),
        (N'AccrualNotifications', N'UQ_bms_web_AccrualNotifications_BusinessKey', 1, 0, 2),
        (N'AccrualNotifications', N'IX_bms_web_AccrualNotifications_Recipient_Created', 0, 0, 2),
        (N'AccrualNotifications', N'IX_bms_web_AccrualNotifications_Request_Cycle', 0, 0, 2),
        (N'AccrualNotifications', N'IX_bms_web_AccrualNotifications_Organization', 0, 0, 2),
        (N'AccrualNotificationIssues', N'PK_bms_web_AccrualNotificationIssues', 1, 1, 1),
        (N'AccrualNotificationIssues', N'IX_bms_web_AccrualNotificationIssues_Unresolved', 0, 0, 2);
    DECLARE @ExpectedIndexColumns TABLE (IndexName sysname, Ordinal int, ColumnName sysname);
    INSERT INTO @ExpectedIndexColumns VALUES
        (N'PK_bms_web_AccrualNotifications', 1, N'Id'),
        (N'UQ_bms_web_AccrualNotifications_BusinessKey', 1, N'NotificationType'),
        (N'UQ_bms_web_AccrualNotifications_BusinessKey', 2, N'ExpenseRequestId'),
        (N'UQ_bms_web_AccrualNotifications_BusinessKey', 3, N'AccrualCycleId'),
        (N'UQ_bms_web_AccrualNotifications_BusinessKey', 4, N'RecipientEmployeeId'),
        (N'IX_bms_web_AccrualNotifications_Recipient_Created', 1, N'RecipientEmployeeId'),
        (N'IX_bms_web_AccrualNotifications_Recipient_Created', 2, N'CreatedAtUtc'),
        (N'IX_bms_web_AccrualNotifications_Recipient_Created', 3, N'Id'),
        (N'IX_bms_web_AccrualNotifications_Request_Cycle', 1, N'ExpenseRequestId'),
        (N'IX_bms_web_AccrualNotifications_Request_Cycle', 2, N'AccrualCycleId'),
        (N'IX_bms_web_AccrualNotifications_Organization', 1, N'OrganizationId'),
        (N'PK_bms_web_AccrualNotificationIssues', 1, N'ExpenseRequestId'),
        (N'PK_bms_web_AccrualNotificationIssues', 2, N'Code'),
        (N'IX_bms_web_AccrualNotificationIssues_Unresolved', 1, N'ResolvedAtUtc'),
        (N'IX_bms_web_AccrualNotificationIssues_Unresolved', 2, N'Code');
    IF EXISTS
    (
        SELECT 1 FROM @ExpectedIndexes AS expected
        LEFT JOIN sys.indexes AS actual
          ON actual.object_id = OBJECT_ID(N'bms_web.' + expected.TableName) AND actual.name = expected.IndexName
        WHERE actual.index_id IS NULL OR actual.is_unique <> expected.IsUnique
           OR actual.is_primary_key <> expected.IsPrimaryKey OR actual.type <> expected.IndexType
           OR actual.is_disabled <> 0 OR actual.has_filter <> 0 OR actual.filter_definition IS NOT NULL
           OR actual.is_hypothetical <> 0 OR actual.ignore_dup_key <> 0
           OR (SELECT COUNT(*) FROM sys.index_columns AS cols
               WHERE cols.object_id = actual.object_id AND cols.index_id = actual.index_id) <>
              (SELECT COUNT(*) FROM @ExpectedIndexColumns AS cols WHERE cols.IndexName = expected.IndexName)
           OR EXISTS
           (
               SELECT 1 FROM @ExpectedIndexColumns AS cols
               LEFT JOIN sys.index_columns AS actual_col ON actual_col.object_id = actual.object_id
                  AND actual_col.index_id = actual.index_id AND actual_col.key_ordinal = cols.Ordinal
               LEFT JOIN sys.columns AS col ON col.object_id = actual_col.object_id AND col.column_id = actual_col.column_id
               WHERE cols.IndexName = expected.IndexName AND
                 (col.name IS NULL OR col.name <> cols.ColumnName OR actual_col.is_descending_key <> 0
                  OR actual_col.is_included_column <> 0)
           )
    )
       OR (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'bms_web.AccrualNotifications')) <> 5
       OR (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'bms_web.AccrualNotificationIssues')) <> 2
        THROW 62309, N'BMS W603 refused: notification index contract is drifted.', 1;

    DECLARE @ExpectedForeignKeys TABLE
        (TableName sysname, ConstraintName sysname, ReferencedTable sysname, Ordinal int,
         ParentColumn sysname, ReferencedColumn sysname);
    INSERT INTO @ExpectedForeignKeys VALUES
        (N'AccrualNotifications', N'FK_bms_web_AccrualNotifications_Cycle', N'ExpenseRequestAccrualClosingCycles', 1, N'ExpenseRequestId', N'ExpenseRequestId'),
        (N'AccrualNotifications', N'FK_bms_web_AccrualNotifications_Cycle', N'ExpenseRequestAccrualClosingCycles', 2, N'AccrualCycleId', N'CycleId'),
        (N'AccrualNotifications', N'FK_bms_web_AccrualNotifications_Employee', N'Employees', 1, N'RecipientEmployeeId', N'Id'),
        (N'AccrualNotifications', N'FK_bms_web_AccrualNotifications_Organization', N'Organizations', 1, N'OrganizationId', N'Id'),
        (N'AccrualNotificationIssues', N'FK_bms_web_AccrualNotificationIssues_Request', N'ExpenseRequests', 1, N'ExpenseRequestId', N'Id');
    IF EXISTS
    (
        SELECT 1 FROM @ExpectedForeignKeys AS expected
        LEFT JOIN sys.foreign_keys AS actual ON actual.parent_object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
           AND actual.name = expected.ConstraintName
        LEFT JOIN sys.foreign_key_columns AS fkcol ON fkcol.constraint_object_id = actual.object_id
           AND fkcol.constraint_column_id = expected.Ordinal
        LEFT JOIN sys.columns AS parent ON parent.object_id = fkcol.parent_object_id AND parent.column_id = fkcol.parent_column_id
        LEFT JOIN sys.columns AS referenced ON referenced.object_id = fkcol.referenced_object_id AND referenced.column_id = fkcol.referenced_column_id
        WHERE actual.object_id IS NULL OR actual.referenced_object_id <> OBJECT_ID(N'bms_web.' + expected.ReferencedTable)
           OR actual.is_disabled <> 0 OR actual.is_not_trusted <> 0 OR actual.is_not_for_replication <> 0
           OR actual.is_system_named <> 0 OR actual.delete_referential_action <> 0 OR actual.update_referential_action <> 0
           OR parent.name IS NULL OR parent.name <> expected.ParentColumn
           OR referenced.name IS NULL OR referenced.name <> expected.ReferencedColumn
           OR (SELECT COUNT(*) FROM sys.foreign_key_columns WHERE constraint_object_id = actual.object_id) <>
              (SELECT COUNT(*) FROM @ExpectedForeignKeys WHERE ConstraintName = expected.ConstraintName)
    )
       OR (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'bms_web.AccrualNotifications')) <> 3
       OR (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'bms_web.AccrualNotificationIssues')) <> 1
        THROW 62310, N'BMS W603 refused: notification foreign key contract is drifted.', 1;

    -- Let this SQL Server canonicalize the expected expressions too. Parentheses
    -- MUST be retained: removing them could hide a weakened AND/OR boundary.
    -- Unnamed constraints on private temporary tables are safe for concurrent
    -- sessions and give the engine's exact expression spelling without dynamic SQL.
    CREATE TABLE #W603Identity
    (
        Id uniqueidentifier, ExpenseRequestId uniqueidentifier, AccrualCycleId uniqueidentifier,
        RecipientEmployeeId uniqueidentifier, OrganizationId uniqueidentifier,
        NotificationType nvarchar(32) COLLATE Latin1_General_100_BIN2,
        RecipientWasInitiator bit, RecipientWasResponsibleAccountant bit,
        CHECK (Id <> '00000000-0000-0000-0000-000000000000'
            AND ExpenseRequestId <> '00000000-0000-0000-0000-000000000000'
            AND AccrualCycleId <> '00000000-0000-0000-0000-000000000000'
            AND RecipientEmployeeId <> '00000000-0000-0000-0000-000000000000'
            AND OrganizationId <> '00000000-0000-0000-0000-000000000000'
            AND NotificationType = N'ACCRUAL_RED_ZONE' AND DATALENGTH(NotificationType) = 32
            AND (RecipientWasInitiator = 1 OR RecipientWasResponsibleAccountant = 1))
    );
    CREATE TABLE #W603Age
    (
        RedFromDays int, AgeDays int, PaymentBusinessDate date, AsOfDate date, RemainingAmountKzt decimal(18, 2),
        CHECK (RedFromDays > 0 AND AgeDays >= RedFromDays
            AND DATEDIFF(day, PaymentBusinessDate, AsOfDate) = AgeDays AND RemainingAmountKzt > 0)
    );
    CREATE TABLE #W603Time
    (
        CreatedAtUtc datetimeoffset(3), FullyPaidAtUtc datetimeoffset(3),
        CHECK (DATEPART(TZOFFSET, CreatedAtUtc) = 0 AND DATEPART(TZOFFSET, FullyPaidAtUtc) = 0
            AND CreatedAtUtc >= FullyPaidAtUtc)
    );
    CREATE TABLE #W603IssueIdentity
    (
        ExpenseRequestId uniqueidentifier, AccrualCycleId uniqueidentifier,
        Code nvarchar(64) COLLATE Latin1_General_100_BIN2,
        CHECK (ExpenseRequestId <> '00000000-0000-0000-0000-000000000000'
            AND (AccrualCycleId IS NULL OR AccrualCycleId <> '00000000-0000-0000-0000-000000000000')
            AND ((Code = N'NO_RESPONSIBLE_ACCOUNTANT' AND DATALENGTH(Code) = 50)
                OR (Code = N'INITIATOR_MAPPING_UNAVAILABLE' AND DATALENGTH(Code) = 58)))
    );
    CREATE TABLE #W603IssueTime
    (
        FirstDetectedAtUtc datetimeoffset(3), LastDetectedAtUtc datetimeoffset(3), ResolvedAtUtc datetimeoffset(3),
        CHECK (DATEPART(TZOFFSET, FirstDetectedAtUtc) = 0 AND DATEPART(TZOFFSET, LastDetectedAtUtc) = 0
            AND LastDetectedAtUtc >= FirstDetectedAtUtc AND (ResolvedAtUtc IS NULL OR
                (DATEPART(TZOFFSET, ResolvedAtUtc) = 0 AND ResolvedAtUtc >= FirstDetectedAtUtc
                    AND ResolvedAtUtc = LastDetectedAtUtc)))
    );
    DECLARE @ExpectedChecks TABLE (TableName sysname, ConstraintName sysname, CanonicalDefinition nvarchar(max));
    INSERT INTO @ExpectedChecks
    SELECT expected.TableName, expected.ConstraintName, actual.definition
    FROM (VALUES
        (N'AccrualNotifications', N'CK_bms_web_AccrualNotifications_Identity', N'tempdb..#W603Identity'),
        (N'AccrualNotifications', N'CK_bms_web_AccrualNotifications_Age', N'tempdb..#W603Age'),
        (N'AccrualNotifications', N'CK_bms_web_AccrualNotifications_Time', N'tempdb..#W603Time'),
        (N'AccrualNotificationIssues', N'CK_bms_web_AccrualNotificationIssues_Identity', N'tempdb..#W603IssueIdentity'),
        (N'AccrualNotificationIssues', N'CK_bms_web_AccrualNotificationIssues_Time', N'tempdb..#W603IssueTime')
    ) AS expected(TableName, ConstraintName, TemporaryTable)
    LEFT JOIN tempdb.sys.check_constraints AS actual ON actual.parent_object_id = OBJECT_ID(expected.TemporaryTable);
    IF EXISTS
    (
        SELECT 1 FROM @ExpectedChecks AS expected
        LEFT JOIN sys.check_constraints AS actual ON actual.parent_object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
           AND actual.name = expected.ConstraintName
        WHERE actual.object_id IS NULL OR actual.definition IS NULL OR expected.CanonicalDefinition IS NULL
           OR actual.is_disabled <> 0
           OR actual.is_not_trusted <> 0 OR actual.is_not_for_replication <> 0
           OR actual.is_system_named <> 0 OR actual.parent_column_id <> 0
           OR
           (SELECT COUNT(DISTINCT normalized_def COLLATE Latin1_General_100_BIN2)
            FROM (VALUES
              (REPLACE(REPLACE(REPLACE(REPLACE(actual.definition,
                N' ', N''), NCHAR(13), N''), NCHAR(10), N''), NCHAR(9), N'')),
              (REPLACE(REPLACE(REPLACE(REPLACE(expected.CanonicalDefinition,
                N' ', N''), NCHAR(13), N''), NCHAR(10), N''), NCHAR(9), N''))
            ) AS definitions(normalized_def)) <> 1
    )
       OR (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'bms_web.AccrualNotifications')) <> 3
       OR (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'bms_web.AccrualNotificationIssues')) <> 2
        THROW 62311, N'BMS W603 refused: notification CHECK contract is drifted.', 1;
    IF EXISTS
    (
        SELECT 1 FROM sys.triggers WHERE is_ms_shipped = 0 AND parent_id IN
            (OBJECT_ID(N'bms_web.AccrualNotifications'), OBJECT_ID(N'bms_web.AccrualNotificationIssues'))
    )
       OR EXISTS
       (
           SELECT 1 FROM sys.tables WHERE object_id IN
               (OBJECT_ID(N'bms_web.AccrualNotifications'), OBJECT_ID(N'bms_web.AccrualNotificationIssues'))
             AND (temporal_type <> 0 OR is_memory_optimized <> 0)
       )
        THROW 62312, N'BMS W603 refused: notification tables have unsupported behavior.', 1;

    IF @AlreadyApplied = 0
        INSERT INTO bms_web.schema_version (version_no, script_name)
        VALUES (27, N'027_accrual_red_zone_notifications.sql');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
