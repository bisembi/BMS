-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    SQL035: nullable immutable approval evidence on budget-instance headers.
    Exact trusted SQL034 predecessor is required; repeat requires the complete SQL035 shape.
    Old Draft data, numbering, membership, period, lines and all financial projections are preserved.
    No actor/role/approval seed; ordinal allocation and Financial Director authority belong to the service.
*/
IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS budget approval storage refused: run SQL035 outside an ambient transaction.', 16, 1);
    RETURN;
END;
SET NOCOUNT ON;
-- Required session options for the filtered unique ordinal index.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 63501, N'BMS budget approval storage refused: current database must be the explicitly owned BMS database.', 1;
IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 63502, N'BMS budget approval storage refused: current database is not READ_WRITE.', 1;
IF SCHEMA_ID(N'bms_web') IS NULL OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.CostCenters', N'U') IS NULL OR OBJECT_ID(N'bms_web.Employees', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.CashFlowItems', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.BudgetInstances', N'U') IS NULL OR OBJECT_ID(N'bms_web.BudgetInstanceLines', N'U') IS NULL
    THROW 63503, N'BMS budget approval storage refused: required managed tables are absent.', 1;
BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK) WHERE version_no > 35)
        THROW 63504, N'BMS budget approval storage refused: a future schema version is registered.', 1;
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
        (33, N'033_budget_instance_monthly_period.sql'),
        (34, N'034_budget_cost_center_directory.sql');
    IF (SELECT COUNT(*) FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK) WHERE version_no <> 35) <> 34
       OR EXISTS
       (
           SELECT 1 FROM @Predecessors AS expected
           LEFT JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK) ON actual.version_no = expected.version_no
           WHERE actual.version_no IS NULL
              OR actual.script_name COLLATE Latin1_General_100_BIN2 <> expected.script_name COLLATE Latin1_General_100_BIN2
              OR DATALENGTH(actual.script_name) <> DATALENGTH(expected.script_name)
       )
        THROW 63505, N'BMS budget approval storage refused: exact SQL001-SQL034 predecessors are required.', 1;
    IF EXISTS
    (
        SELECT 1 FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK) WHERE version_no = 35 AND
        (script_name COLLATE Latin1_General_100_BIN2 <> N'035_budget_instance_approval.sql'
         OR DATALENGTH(script_name) <> DATALENGTH(N'035_budget_instance_approval.sql'))
    )
        THROW 63506, N'BMS budget approval storage refused: SQL035 belongs to another script.', 1;
    DECLARE @VersionRegistered bit = CASE WHEN EXISTS
        (SELECT 1 FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK) WHERE version_no = 35) THEN 1 ELSE 0 END;
    DECLARE @HeaderId int = OBJECT_ID(N'bms_web.BudgetInstances');
    DECLARE @LineId int = OBJECT_ID(N'bms_web.BudgetInstanceLines');
    -- Refuse all canonical names before creating anything; never adopt or drop a caller's temp.
    IF OBJECT_ID(N'tempdb..#BI035PreHeaderYear') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BI035PreHeaderMonth') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BI035PreHeaderStatus') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BI035PreLineMonth') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BI035PreLineLimit') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BI035PreLineSort') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BI035ApprovalHeaderMonth') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BI035ApprovalHeaderStatus') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BI035PostLineMonth') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BI035PostLineLimit') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BI035PostLineSort') IS NOT NULL
        THROW 63507, N'BMS budget approval storage refused: canonical local temp name is already in use.', 1;

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
        THROW 63510, N'BMS budget approval storage refused: exact trusted SQL034 directory schema differs.', 1;

    IF @VersionRegistered = 0
    BEGIN
    /* SQL035 EXACT SQL034 PREDECESSOR SHAPE BEGIN */
    -- Retain SQL033's complete read-only monthly repeat checks, not its DDL or registration.
    -- The immutable predecessor file stays unchanged; this invocation owns different temp names.
    IF OBJECT_ID(N'tempdb..#BI035PreHeaderYear') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BI035PreHeaderMonth') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BI035PreHeaderStatus') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BI035PreLineMonth') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BI035PreLineLimit') IS NOT NULL
       OR OBJECT_ID(N'tempdb..#BI035PreLineSort') IS NOT NULL
        THROW 63411, N'BMS budget directory refused: predecessor canonical temp name is already in use.', 1;
    DECLARE @BI035PredecessorVersionRegistered bit = 1;
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

    CREATE TABLE #BI035PreHeaderYear
    (
        ValidFrom date, ValidTo date,
        CHECK (
            DAY(ValidFrom) = 1 AND MONTH(ValidFrom) = 1
            AND DAY(ValidTo) = 31 AND MONTH(ValidTo) = 12
            AND YEAR(ValidFrom) = YEAR(ValidTo))
    );
    CREATE TABLE #BI035PreHeaderMonth
    (
        ValidFrom date, ValidTo date,
        CHECK (DAY(ValidFrom) = 1 AND ValidTo = EOMONTH(ValidTo) AND ValidFrom <= ValidTo)
    );
    CREATE TABLE #BI035PreHeaderStatus
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
        (CASE WHEN @BI035PredecessorVersionRegistered = 0 THEN N'CK_bms_web_BudgetInstances_year_bounds'
              ELSE N'CK_bms_web_BudgetInstances_month_bounds' END,
         CAST(NULL AS sysname),
         CASE WHEN @BI035PredecessorVersionRegistered = 0 THEN N'tempdb..#BI035PreHeaderYear'
              ELSE N'tempdb..#BI035PreHeaderMonth' END),
        (N'CK_bms_web_BudgetInstances_status_draft_slice',
         N'Status', N'tempdb..#BI035PreHeaderStatus')
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

    CREATE TABLE #BI035PreLineMonth (BudgetMonth date, CHECK (DAY(BudgetMonth) = 1));
    CREATE TABLE #BI035PreLineLimit (LimitAmountKzt decimal(18, 2), CHECK (LimitAmountKzt >= 0));
    CREATE TABLE #BI035PreLineSort (SortOrder int, CHECK (SortOrder >= 0));
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
         N'BudgetMonth', N'tempdb..#BI035PreLineMonth'),
        (N'CK_bms_web_BudgetInstanceLines_limit',
         N'LimitAmountKzt', N'tempdb..#BI035PreLineLimit'),
        (N'CK_bms_web_BudgetInstanceLines_sort',
         N'SortOrder', N'tempdb..#BI035PreLineSort')
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
    DROP TABLE #BI035PreHeaderYear;
    DROP TABLE #BI035PreHeaderMonth;
    DROP TABLE #BI035PreHeaderStatus;
    DROP TABLE #BI035PreLineMonth;
    DROP TABLE #BI035PreLineLimit;
    DROP TABLE #BI035PreLineSort;
    /* SQL035 EXACT SQL034 PREDECESSOR SHAPE END */
        /* SQL035 OWN DDL BEGIN */
        -- Separate batches prevent first-apply compilation from binding columns which do not yet exist.
        EXEC sys.sp_executesql N'
            ALTER TABLE bms_web.BudgetInstances ADD
                ApprovalOrdinal bigint NULL,
                ApprovedAtUtc datetimeoffset(3) NULL,
                ApprovedByEmployeeId uniqueidentifier NULL,
                ApprovedByExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2 NULL;';
        EXEC sys.sp_executesql N'
            ALTER TABLE bms_web.BudgetInstances DROP CONSTRAINT CK_bms_web_BudgetInstances_status_draft_slice;
            ALTER TABLE bms_web.BudgetInstances WITH CHECK
                ADD CONSTRAINT CK_bms_web_BudgetInstances_approval_state CHECK ((
            Status COLLATE Latin1_General_100_BIN2 = N''Draft'' AND DATALENGTH(Status) = 10
            AND ApprovalOrdinal IS NULL AND ApprovedAtUtc IS NULL
            AND ApprovedByEmployeeId IS NULL AND ApprovedByExternalId IS NULL
        )
        OR
        (
            Status COLLATE Latin1_General_100_BIN2 = N''Approved'' AND DATALENGTH(Status) = 16
            AND ApprovalOrdinal IS NOT NULL AND ApprovalOrdinal > 0
            AND ApprovedAtUtc IS NOT NULL AND DATEPART(TZOFFSET, ApprovedAtUtc) = 0
            AND ApprovedByEmployeeId IS NOT NULL
            AND ApprovedByEmployeeId <> CONVERT(uniqueidentifier, ''00000000-0000-0000-0000-000000000000'')
            AND ApprovedByExternalId IS NOT NULL
            AND DATALENGTH(ApprovedByExternalId) BETWEEN 2 AND 512
            AND DATALENGTH(ApprovedByExternalId) = DATALENGTH(LTRIM(RTRIM(ApprovedByExternalId)))
        ));
            ALTER TABLE bms_web.BudgetInstances WITH CHECK
                ADD CONSTRAINT FK_bms_web_BudgetInstances_ApprovedByEmployee
                FOREIGN KEY (ApprovedByEmployeeId) REFERENCES bms_web.Employees (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION;
            CREATE UNIQUE NONCLUSTERED INDEX UX_bms_web_BudgetInstances_CostCenter_ApprovalOrdinal
                ON bms_web.BudgetInstances (CostCenterId, ApprovalOrdinal)
                WHERE ApprovalOrdinal IS NOT NULL;';
        /* SQL035 OWN DDL END */
    END;

    /* SQL035 LATEST STORAGE SHAPE BEGIN */
/* SQL035 APPROVAL SHAPE BEGIN */
-- Shared latest metadata predicate for SQL035 repeat and runtime readiness.
-- No business data writes or reads. The canonical filtered index is on an owned empty temp only.
-- These session options are required by SQL Server for that canonical filtered index.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
IF OBJECT_ID(N'tempdb..#BI035ApprovalHeaderMonth') IS NOT NULL
   OR OBJECT_ID(N'tempdb..#BI035ApprovalHeaderStatus') IS NOT NULL
    THROW 63516, N'BMS budget approval storage refused: approval canonical local temp name is already in use.', 1;
DECLARE @BudgetInstanceApprovalShapeReady bit = 1;
DECLARE @BI035ApprovalHeaderId int = OBJECT_ID(N'bms_web.BudgetInstances', N'U');
IF @BI035ApprovalHeaderId IS NULL SET @BudgetInstanceApprovalShapeReady = 0;
    /* ---- BudgetInstances structural drift (create path and repeat apply) ---- */
    IF EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = @BI035ApprovalHeaderId AND is_ms_shipped = 0)
        SET @BudgetInstanceApprovalShapeReady = 0;
    IF EXISTS
    (
        SELECT 1 FROM sys.tables
        WHERE object_id = @BI035ApprovalHeaderId AND (temporal_type <> 0 OR is_memory_optimized <> 0)
    )
        SET @BudgetInstanceApprovalShapeReady = 0;
    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = @BI035ApprovalHeaderId) <> 13
        SET @BudgetInstanceApprovalShapeReady = 0;
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
                (N'ConcurrencyStamp', N'uniqueidentifier', 16, 0, 0, 0, NULL),
                (N'ApprovalOrdinal', N'bigint', 8, 1, 19, 0, NULL),
                (N'ApprovedAtUtc', N'datetimeoffset', 9, 1, 30, 3, NULL),
                (N'ApprovedByEmployeeId', N'uniqueidentifier', 16, 1, 0, 0, NULL),
                (N'ApprovedByExternalId', N'nvarchar', 512, 1, 0, 0, N'Latin1_General_100_BIN2')
        ) AS expected(Name, TypeName, MaxLength, IsNullable, [Precision], Scale, CollationName)
        LEFT JOIN sys.columns AS actual
          ON actual.object_id = @BI035ApprovalHeaderId AND actual.name = expected.Name
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
        SET @BudgetInstanceApprovalShapeReady = 0;

    IF (SELECT COUNT(*) FROM sys.indexes WHERE object_id = @BI035ApprovalHeaderId AND index_id > 0) <> 5
        SET @BudgetInstanceApprovalShapeReady = 0;
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = @BI035ApprovalHeaderId
          AND name = N'PK_bms_web_BudgetInstances'
          AND type = 1 AND is_primary_key = 1 AND is_unique = 1
          AND is_unique_constraint = 0 AND is_disabled = 0 AND is_hypothetical = 0
          AND has_filter = 0 AND filter_definition IS NULL AND ignore_dup_key = 0
    )
        SET @BudgetInstanceApprovalShapeReady = 0;
    IF
    (
        SELECT STRING_AGG(c.name, N',') WITHIN GROUP (ORDER BY ic.key_ordinal)
        FROM sys.indexes AS i
        INNER JOIN sys.index_columns AS ic
          ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        INNER JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = @BI035ApprovalHeaderId
          AND i.name = N'PK_bms_web_BudgetInstances'
          AND ic.key_ordinal > 0
    ) COLLATE Latin1_General_100_BIN2 <> N'Id'
        SET @BudgetInstanceApprovalShapeReady = 0;
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = @BI035ApprovalHeaderId
          AND name = N'UX_bms_web_BudgetInstances_Number'
          AND type = 2 AND is_primary_key = 0 AND is_unique = 1
          AND is_unique_constraint = 1 AND is_disabled = 0 AND is_hypothetical = 0
          AND has_filter = 0 AND filter_definition IS NULL AND ignore_dup_key = 0
    )
        SET @BudgetInstanceApprovalShapeReady = 0;
    IF
    (
        SELECT STRING_AGG(c.name, N',') WITHIN GROUP (ORDER BY ic.key_ordinal)
        FROM sys.indexes AS i
        INNER JOIN sys.index_columns AS ic
          ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        INNER JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = @BI035ApprovalHeaderId
          AND i.name = N'UX_bms_web_BudgetInstances_Number'
          AND ic.key_ordinal > 0
    ) COLLATE Latin1_General_100_BIN2 <> N'Number'
        SET @BudgetInstanceApprovalShapeReady = 0;
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = @BI035ApprovalHeaderId
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
           WHERE i.object_id = @BI035ApprovalHeaderId
             AND i.name = N'IX_bms_web_BudgetInstances_Owner_Status_Year'
             AND ic.key_ordinal > 0
       ) COLLATE Latin1_General_100_BIN2 <> N'OwnerEmployeeId:0,Status:0,ValidFrom:0'
        SET @BudgetInstanceApprovalShapeReady = 0;
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = @BI035ApprovalHeaderId
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
           WHERE i.object_id = @BI035ApprovalHeaderId
             AND i.name = N'IX_bms_web_BudgetInstances_CostCenter_Year_Status'
             AND ic.key_ordinal > 0
       ) COLLATE Latin1_General_100_BIN2 <> N'CostCenterId:0,ValidFrom:0,Status:0'
        SET @BudgetInstanceApprovalShapeReady = 0;
    IF EXISTS
    (
        SELECT 1
        FROM sys.indexes AS i
        INNER JOIN sys.index_columns AS ic
          ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        WHERE i.object_id = @BI035ApprovalHeaderId
          AND i.name IN
          (
              N'PK_bms_web_BudgetInstances',
              N'UX_bms_web_BudgetInstances_Number',
              N'IX_bms_web_BudgetInstances_Owner_Status_Year',
              N'IX_bms_web_BudgetInstances_CostCenter_Year_Status',
              N'UX_bms_web_BudgetInstances_CostCenter_ApprovalOrdinal'
          )
          AND (ic.is_included_column <> 0 OR ic.is_descending_key <> 0)
    )
        SET @BudgetInstanceApprovalShapeReady = 0;

    IF (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = @BI035ApprovalHeaderId) <> 2
        SET @BudgetInstanceApprovalShapeReady = 0;

    CREATE TABLE #BI035ApprovalHeaderMonth
    (
        ValidFrom date, ValidTo date,
        CHECK (DAY(ValidFrom) = 1 AND ValidTo = EOMONTH(ValidTo) AND ValidFrom <= ValidTo)
    );
    CREATE TABLE #BI035ApprovalHeaderStatus
    (
        CostCenterId uniqueidentifier NOT NULL,
        Status nvarchar(40) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ApprovalOrdinal bigint NULL,
        ApprovedAtUtc datetimeoffset(3) NULL,
        ApprovedByEmployeeId uniqueidentifier NULL,
        ApprovedByExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2 NULL,
        CHECK ((
            Status COLLATE Latin1_General_100_BIN2 = N'Draft' AND DATALENGTH(Status) = 10
            AND ApprovalOrdinal IS NULL AND ApprovedAtUtc IS NULL
            AND ApprovedByEmployeeId IS NULL AND ApprovedByExternalId IS NULL
        )
        OR
        (
            Status COLLATE Latin1_General_100_BIN2 = N'Approved' AND DATALENGTH(Status) = 16
            AND ApprovalOrdinal IS NOT NULL AND ApprovalOrdinal > 0
            AND ApprovedAtUtc IS NOT NULL AND DATEPART(TZOFFSET, ApprovedAtUtc) = 0
            AND ApprovedByEmployeeId IS NOT NULL
            AND ApprovedByEmployeeId <> CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000')
            AND ApprovedByExternalId IS NOT NULL
            AND DATALENGTH(ApprovedByExternalId) BETWEEN 2 AND 512
            AND DATALENGTH(ApprovedByExternalId) = DATALENGTH(LTRIM(RTRIM(ApprovedByExternalId)))
        ))
    );
    CREATE UNIQUE NONCLUSTERED INDEX IX_BI035CanonicalApprovalFilter
        ON #BI035ApprovalHeaderStatus (CostCenterId, ApprovalOrdinal)
        WHERE ApprovalOrdinal IS NOT NULL;
    DECLARE @BI035ApprovalHeaderChecks TABLE
        (ConstraintName sysname NOT NULL, ParentColumnId int NOT NULL,
         CanonicalDefinition nvarchar(max) NULL);
    INSERT INTO @BI035ApprovalHeaderChecks
    SELECT expected.ConstraintName,
           CASE WHEN expected.ParentColumnName IS NULL THEN 0
                ELSE ISNULL(COLUMNPROPERTY(
                    @BI035ApprovalHeaderId, expected.ParentColumnName, N'ColumnId'), -1) END,
           actual.definition
    FROM (VALUES
        (N'CK_bms_web_BudgetInstances_month_bounds',
         CAST(NULL AS sysname), N'tempdb..#BI035ApprovalHeaderMonth'),
        (N'CK_bms_web_BudgetInstances_approval_state',
         CAST(NULL AS sysname), N'tempdb..#BI035ApprovalHeaderStatus')
    ) AS expected(ConstraintName, ParentColumnName, TemporaryTable)
    LEFT JOIN tempdb.sys.check_constraints AS actual
      ON actual.parent_object_id = OBJECT_ID(expected.TemporaryTable);
    IF EXISTS
    (
        SELECT 1 FROM @BI035ApprovalHeaderChecks AS expected
        LEFT JOIN sys.check_constraints AS actual
          ON actual.parent_object_id = @BI035ApprovalHeaderId
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
        SET @BudgetInstanceApprovalShapeReady = 0;

    /* SQL035 APPROVAL METADATA BEGIN */
    -- Exact precision/scale includes zero: bigint(19,0) must not pass a weakened facet.
    IF EXISTS
    (
        SELECT 1
        FROM (VALUES
            (N'ApprovalOrdinal', N'bigint', 8, 1, 19, 0, CAST(NULL AS sysname)),
            (N'ApprovedAtUtc', N'datetimeoffset', 9, 1, 30, 3, CAST(NULL AS sysname)),
            (N'ApprovedByEmployeeId', N'uniqueidentifier', 16, 1, 0, 0, CAST(NULL AS sysname)),
            (N'ApprovedByExternalId', N'nvarchar', 512, 1, 0, 0, N'Latin1_General_100_BIN2')
        ) AS expected(ColumnName, TypeName, MaxLength, IsNullable, [Precision], Scale, CollationName)
        LEFT JOIN sys.columns AS actual ON actual.object_id = @BI035ApprovalHeaderId
            AND actual.name COLLATE Latin1_General_100_BIN2 = expected.ColumnName
        WHERE actual.column_id IS NULL OR DATALENGTH(actual.name) <> DATALENGTH(expected.ColumnName)
           OR TYPE_NAME(actual.user_type_id) COLLATE Latin1_General_100_BIN2 <> expected.TypeName
           OR actual.max_length <> expected.MaxLength OR actual.is_nullable <> expected.IsNullable
           OR actual.[precision] <> expected.[Precision] OR actual.scale <> expected.Scale
           OR ISNULL(actual.collation_name, N'') COLLATE Latin1_General_100_BIN2 <> ISNULL(expected.CollationName, N'')
    )
        SET @BudgetInstanceApprovalShapeReady = 0;

    -- Do not strip spaces from the new lifecycle definition: doing so also changes string literals.
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints AS actual
        INNER JOIN tempdb.sys.check_constraints AS canonical
            ON canonical.parent_object_id = OBJECT_ID(N'tempdb..#BI035ApprovalHeaderStatus')
        WHERE actual.parent_object_id = @BI035ApprovalHeaderId
          AND actual.name COLLATE Latin1_General_100_BIN2 = N'CK_bms_web_BudgetInstances_approval_state'
          AND DATALENGTH(actual.name) = DATALENGTH(N'CK_bms_web_BudgetInstances_approval_state')
          AND actual.parent_column_id = 0 AND actual.is_disabled = 0 AND actual.is_not_trusted = 0
          AND actual.is_not_for_replication = 0 AND actual.is_system_named = 0
          AND actual.definition COLLATE Latin1_General_100_BIN2 = canonical.definition COLLATE Latin1_General_100_BIN2
          AND DATALENGTH(actual.definition) = DATALENGTH(canonical.definition)
    )
        SET @BudgetInstanceApprovalShapeReady = 0;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes AS actual
        INNER JOIN tempdb.sys.indexes AS canonical
            ON canonical.object_id = OBJECT_ID(N'tempdb..#BI035ApprovalHeaderStatus')
           AND canonical.name = N'IX_BI035CanonicalApprovalFilter'
        WHERE actual.object_id = @BI035ApprovalHeaderId
          AND actual.name COLLATE Latin1_General_100_BIN2 = N'UX_bms_web_BudgetInstances_CostCenter_ApprovalOrdinal'
          AND DATALENGTH(actual.name) = DATALENGTH(N'UX_bms_web_BudgetInstances_CostCenter_ApprovalOrdinal')
          AND actual.type = 2 AND actual.is_primary_key = 0 AND actual.is_unique = 1
          AND actual.is_unique_constraint = 0 AND actual.is_disabled = 0 AND actual.is_hypothetical = 0
          AND actual.has_filter = 1 AND actual.ignore_dup_key = 0
          AND actual.filter_definition COLLATE Latin1_General_100_BIN2 = canonical.filter_definition COLLATE Latin1_General_100_BIN2
          AND DATALENGTH(actual.filter_definition) = DATALENGTH(canonical.filter_definition)
          AND ISNULL((
                SELECT STRING_AGG(CONCAT(c.name, N':', ic.is_descending_key), N',')
                    WITHIN GROUP (ORDER BY ic.key_ordinal)
                FROM sys.index_columns AS ic
                INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE ic.object_id = actual.object_id AND ic.index_id = actual.index_id AND ic.key_ordinal > 0
              ), N'') COLLATE Latin1_General_100_BIN2 = N'CostCenterId:0,ApprovalOrdinal:0'
          AND NOT EXISTS
              (SELECT 1 FROM sys.index_columns AS ic
               WHERE ic.object_id = actual.object_id AND ic.index_id = actual.index_id
                 AND (ic.is_included_column <> 0 OR ic.is_descending_key <> 0))
    )
        SET @BudgetInstanceApprovalShapeReady = 0;
    /* SQL035 APPROVAL METADATA END */

    IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = @BI035ApprovalHeaderId) <> 3
        SET @BudgetInstanceApprovalShapeReady = 0;
    IF EXISTS
    (
        SELECT 1
        FROM (VALUES
            (N'FK_bms_web_BudgetInstances_CostCenter', N'CostCenterId', N'Id',
             OBJECT_ID(N'bms_web.CostCenters'), 0),
            (N'FK_bms_web_BudgetInstances_OwnerEmployee', N'OwnerEmployeeId', N'Id',
             OBJECT_ID(N'bms_web.Employees'), 0),
            (N'FK_bms_web_BudgetInstances_ApprovedByEmployee', N'ApprovedByEmployeeId', N'Id',
             OBJECT_ID(N'bms_web.Employees'), 0)
        ) AS expected(ConstraintName, ParentColumnName, ReferencedColumnName,
                      ReferencedObjectId, DeleteAction)
        LEFT JOIN sys.foreign_keys AS actual_fk
          ON actual_fk.parent_object_id = @BI035ApprovalHeaderId
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
        SET @BudgetInstanceApprovalShapeReady = 0;

    -- The SQL-owned approver FK has an exact name as well as the complete common FK facets above.
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.foreign_keys
        WHERE parent_object_id = @BI035ApprovalHeaderId
          AND name COLLATE Latin1_General_100_BIN2 = N'FK_bms_web_BudgetInstances_ApprovedByEmployee'
          AND DATALENGTH(name) = DATALENGTH(N'FK_bms_web_BudgetInstances_ApprovedByEmployee')
    )
        SET @BudgetInstanceApprovalShapeReady = 0;

DROP TABLE #BI035ApprovalHeaderMonth;
DROP TABLE #BI035ApprovalHeaderStatus;
/* SQL035 APPROVAL SHAPE END */
    IF @BudgetInstanceApprovalShapeReady = 0
        THROW 63517, N'BMS budget approval storage refused: exact trusted approval header schema differs.', 1;

    /* ---- BudgetInstanceLines structural drift ---- */
    IF EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = @LineId AND is_ms_shipped = 0)
        THROW 63620, N'BMS budget approval storage refused: BudgetInstanceLines has an unmanaged trigger.', 1;
    IF EXISTS
    (
        SELECT 1 FROM sys.tables
        WHERE object_id = @LineId AND (temporal_type <> 0 OR is_memory_optimized <> 0)
    )
        THROW 63620, N'BMS budget approval storage refused: BudgetInstanceLines has unsupported behavior.', 1;
    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = @LineId) <> 6
        THROW 63621, N'BMS budget approval storage refused: BudgetInstanceLines column count differs.', 1;
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
        THROW 63622, N'BMS budget approval storage refused: BudgetInstanceLines column metadata differs.', 1;

    IF (SELECT COUNT(*) FROM sys.indexes WHERE object_id = @LineId AND index_id > 0) <> 2
        THROW 63623, N'BMS budget approval storage refused: BudgetInstanceLines index count differs.', 1;
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = @LineId
          AND name = N'PK_bms_web_BudgetInstanceLines'
          AND type = 1 AND is_primary_key = 1 AND is_unique = 1
          AND is_unique_constraint = 0 AND is_disabled = 0 AND is_hypothetical = 0
          AND has_filter = 0 AND filter_definition IS NULL AND ignore_dup_key = 0
    )
        THROW 63624, N'BMS budget approval storage refused: BudgetInstanceLines primary key differs.', 1;
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
        THROW 63624, N'BMS budget approval storage refused: BudgetInstanceLines primary key shape differs.', 1;
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
        THROW 63625, N'BMS budget approval storage refused: BudgetInstanceLines key uniqueness differs.', 1;
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
        THROW 63625, N'BMS budget approval storage refused: BudgetInstanceLines index columns differ.', 1;

    IF (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = @LineId) <> 3
        THROW 63626, N'BMS budget approval storage refused: BudgetInstanceLines check count differs.', 1;

    CREATE TABLE #BI035PostLineMonth (BudgetMonth date, CHECK (DAY(BudgetMonth) = 1));
    CREATE TABLE #BI035PostLineLimit (LimitAmountKzt decimal(18, 2), CHECK (LimitAmountKzt >= 0));
    CREATE TABLE #BI035PostLineSort (SortOrder int, CHECK (SortOrder >= 0));
    DECLARE @BI035PostLineChecks TABLE
        (ConstraintName sysname NOT NULL, ParentColumnId int NOT NULL,
         CanonicalDefinition nvarchar(max) NULL);
    INSERT INTO @BI035PostLineChecks
    SELECT expected.ConstraintName,
           ISNULL(COLUMNPROPERTY(
               @LineId, expected.ParentColumnName, N'ColumnId'), -1),
           actual.definition
    FROM (VALUES
        (N'CK_bms_web_BudgetInstanceLines_month_start',
         N'BudgetMonth', N'tempdb..#BI035PostLineMonth'),
        (N'CK_bms_web_BudgetInstanceLines_limit',
         N'LimitAmountKzt', N'tempdb..#BI035PostLineLimit'),
        (N'CK_bms_web_BudgetInstanceLines_sort',
         N'SortOrder', N'tempdb..#BI035PostLineSort')
    ) AS expected(ConstraintName, ParentColumnName, TemporaryTable)
    LEFT JOIN tempdb.sys.check_constraints AS actual
      ON actual.parent_object_id = OBJECT_ID(expected.TemporaryTable);
    IF EXISTS
    (
        SELECT 1 FROM @BI035PostLineChecks AS expected
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
        THROW 63626, N'BMS budget approval storage refused: BudgetInstanceLines check shape differs.', 1;

    IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = @LineId) <> 2
        THROW 63627, N'BMS budget approval storage refused: BudgetInstanceLines foreign key count differs.', 1;
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
        THROW 63627, N'BMS budget approval storage refused: BudgetInstanceLines foreign keys differ.', 1;

    -- New columns are bound only after DDL has completed on first apply.
    EXEC sys.sp_executesql N'
        IF EXISTS
        (
            SELECT 1 FROM bms_web.BudgetInstances WITH (HOLDLOCK)
            WHERE CASE WHEN ((
            Status COLLATE Latin1_General_100_BIN2 = N''Draft'' AND DATALENGTH(Status) = 10
            AND ApprovalOrdinal IS NULL AND ApprovedAtUtc IS NULL
            AND ApprovedByEmployeeId IS NULL AND ApprovedByExternalId IS NULL
        )
        OR
        (
            Status COLLATE Latin1_General_100_BIN2 = N''Approved'' AND DATALENGTH(Status) = 16
            AND ApprovalOrdinal IS NOT NULL AND ApprovalOrdinal > 0
            AND ApprovedAtUtc IS NOT NULL AND DATEPART(TZOFFSET, ApprovedAtUtc) = 0
            AND ApprovedByEmployeeId IS NOT NULL
            AND ApprovedByEmployeeId <> CONVERT(uniqueidentifier, ''00000000-0000-0000-0000-000000000000'')
            AND ApprovedByExternalId IS NOT NULL
            AND DATALENGTH(ApprovedByExternalId) BETWEEN 2 AND 512
            AND DATALENGTH(ApprovedByExternalId) = DATALENGTH(LTRIM(RTRIM(ApprovedByExternalId)))
        )) THEN 1 ELSE 0 END <> 1
               OR DAY(ValidFrom) <> 1 OR ValidTo <> EOMONTH(ValidTo) OR ValidFrom > ValidTo
        )
            THROW 63514, N''BMS budget approval storage refused: stored header rows violate the approval contract.'', 1;';

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
        THROW 63628, N'BMS budget approval storage refused: stored line rows violate the draft contract.', 1;
    DROP TABLE #BI035PostLineMonth;
    DROP TABLE #BI035PostLineLimit;
    DROP TABLE #BI035PostLineSort;
    /* SQL035 LATEST STORAGE SHAPE END */

    IF @VersionRegistered = 0
        INSERT INTO bms_web.schema_version (version_no, script_name)
        VALUES (35, N'035_budget_instance_approval.sql');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
