-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web W16 — internal expense-item projection for accrual mapping validation.

    IMPORTANT
    - Apply 001 through 009 first.
    - This script creates no database, login, user, role, permission or seed row.
    - ExpenseItems remains empty until a separately controlled source import.
    - It has not been executed against an accounting server.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 59101, N'BMS W16 refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 59102, N'BMS W16 refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
    THROW 59103, N'BMS W16 refused: managed schema bms_web is absent.', 1;

IF EXISTS (SELECT 1 FROM bms_web.schema_version WHERE version_no > 10)
    THROW 59104, N'BMS W16 refused: a future schema version is already registered.', 1;

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
            (9, N'009_cash_flow_items_accrual_source.sql')
    ) AS expected(version_no, script_name)
    INNER JOIN bms_web.schema_version AS actual
      ON actual.version_no = expected.version_no
     AND actual.script_name COLLATE Latin1_General_100_BIN2 =
         expected.script_name COLLATE Latin1_General_100_BIN2
     AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
) <> 9
    THROW 59105, N'BMS W16 refused: exact schema versions 1 through 9 are required.', 1;

IF EXISTS
(
    SELECT 1
    FROM bms_web.schema_version
    WHERE version_no = 10
      AND
      (
          script_name COLLATE Latin1_General_100_BIN2 <>
              N'010_expense_items_accrual_mapping.sql'
          OR DATALENGTH(script_name) <>
              DATALENGTH(N'010_expense_items_accrual_mapping.sql')
      )
)
    THROW 59106, N'BMS W16 refused: schema version 10 belongs to another script.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'bms_web.ExpenseItems', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.ExpenseItems
        (
            Id      uniqueidentifier NOT NULL,
            Code    nvarchar(9) COLLATE Latin1_General_100_BIN2 NOT NULL,
            Name    nvarchar(25) NOT NULL,
            CONSTRAINT PK_bms_web_ExpenseItems PRIMARY KEY CLUSTERED (Id ASC),
            CONSTRAINT UQ_bms_web_ExpenseItems_Code UNIQUE NONCLUSTERED (Code ASC)
        );
    END;

    /* Refuse success against a partial or manually drifted W16 table. */
    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'Id', N'uniqueidentifier', 16, 0, CAST(NULL AS nvarchar(128))),
                (N'Code', N'nvarchar', 18, 0, N'Latin1_General_100_BIN2'),
                (N'Name', N'nvarchar', 50, 0, CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128)))
        ) AS expected(ColumnName, TypeName, MaxLength, IsNullable, CollationName)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(N'bms_web.ExpenseItems')
         AND c.name = expected.ColumnName
        WHERE c.object_id IS NULL
           OR TYPE_NAME(c.user_type_id) <> expected.TypeName
           OR c.max_length <> expected.MaxLength
           OR c.is_nullable <> expected.IsNullable
           OR c.is_computed <> 0
           OR c.default_object_id <> 0
           OR c.rule_object_id <> 0
           OR c.generated_always_type <> 0
           OR c.is_sparse <> 0
           OR c.is_column_set <> 0
           OR c.encryption_type IS NOT NULL
           OR c.is_hidden <> 0
           OR c.is_masked <> 0
           OR
           (
               expected.CollationName IS NULL AND c.collation_name IS NOT NULL
               OR expected.CollationName IS NOT NULL AND
                  c.collation_name COLLATE Latin1_General_100_BIN2 <>
                  expected.CollationName COLLATE Latin1_General_100_BIN2
           )
    )
       OR (SELECT COUNT(*) FROM sys.columns
           WHERE object_id = OBJECT_ID(N'bms_web.ExpenseItems')) <> 3
        THROW 59107, N'BMS W16 refused: ExpenseItems column contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'PK_bms_web_ExpenseItems', 1, 1, 1, 0, 1),
                (N'UQ_bms_web_ExpenseItems_Code', 2, 1, 0, 1, 1)
        ) AS expected(IndexName, IndexType, IsUnique, IsPrimaryKey, IsUniqueConstraint, KeyCount)
        LEFT JOIN sys.indexes AS i
          ON i.object_id = OBJECT_ID(N'bms_web.ExpenseItems')
         AND i.name = expected.IndexName
        WHERE i.object_id IS NULL OR i.type <> expected.IndexType
           OR i.is_unique <> expected.IsUnique
           OR i.is_primary_key <> expected.IsPrimaryKey
           OR i.is_unique_constraint <> expected.IsUniqueConstraint
           OR i.is_disabled <> 0 OR i.is_hypothetical <> 0
           OR i.has_filter <> 0 OR i.filter_definition IS NOT NULL
           OR i.ignore_dup_key <> 0
           OR (SELECT COUNT(*) FROM sys.index_columns AS ic
               WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id) <>
              expected.KeyCount
    )
       OR (SELECT COUNT(*) FROM sys.key_constraints
           WHERE parent_object_id = OBJECT_ID(N'bms_web.ExpenseItems')) <> 2
       OR (SELECT COUNT(*) FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'bms_web.ExpenseItems')
             AND index_id > 0 AND is_unique = 1) <> 2
        THROW 59108, N'BMS W16 refused: ExpenseItems key/index contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'PK_bms_web_ExpenseItems', 1, N'Id'),
                (N'UQ_bms_web_ExpenseItems_Code', 1, N'Code')
        ) AS expected(IndexName, KeyOrdinal, ColumnName)
        LEFT JOIN sys.indexes AS i
          ON i.object_id = OBJECT_ID(N'bms_web.ExpenseItems')
         AND i.name = expected.IndexName
        LEFT JOIN sys.index_columns AS ic
          ON ic.object_id = i.object_id AND ic.index_id = i.index_id
         AND ic.key_ordinal = expected.KeyOrdinal
        LEFT JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE c.name <> expected.ColumnName OR c.name IS NULL
           OR ic.is_descending_key <> 0 OR ic.is_included_column <> 0
    )
        THROW 59109, N'BMS W16 refused: ExpenseItems key order is drifted.', 1;

    IF (SELECT COUNT(*) FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'bms_web.ExpenseItems')
           OR referenced_object_id = OBJECT_ID(N'bms_web.ExpenseItems')) <> 0
        THROW 59110, N'BMS W16 refused: ExpenseItems must have no inbound or outbound foreign keys.', 1;

    IF (SELECT COUNT(*) FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.ExpenseItems')) <> 0
        THROW 59111, N'BMS W16 refused: ExpenseItems must have no invented CHECK rules.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.triggers
        WHERE parent_id = OBJECT_ID(N'bms_web.ExpenseItems')
          AND is_ms_shipped = 0
    )
        THROW 59114, N'BMS W16 refused: ExpenseItems has a user DML trigger.', 1;

    IF NOT EXISTS (SELECT 1 FROM bms_web.schema_version WHERE version_no = 10)
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (10, N'010_expense_items_accrual_mapping.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
