-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web W47 — read-only contract reference projection.

    IMPORTANT
    - Apply 001 through 013 first and run this script standalone.
    - This script creates no database, login, user, role, permission or seed row.
    - Contracts remains empty until a separately controlled source import.
    - Integration source, external key and other non-public source attributes
      are deliberately outside the web projection.
    - It has not been executed against an accounting server.
*/

IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS W47 refused: run 014_contracts_reference.sql outside an ambient transaction.', 16, 1);
    RETURN;
END;

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 59501, N'BMS W47 refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 59502, N'BMS W47 refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
    THROW 59503, N'BMS W47 refused: managed schema bms_web is absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no > 14
    )
        THROW 59504, N'BMS W47 refused: a future schema version is already registered.', 1;

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
                (13, N'013_counterparties_reference.sql')
        ) AS expected(version_no, script_name)
        INNER JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK)
          ON actual.version_no = expected.version_no
         AND actual.script_name COLLATE Latin1_General_100_BIN2 =
             expected.script_name COLLATE Latin1_General_100_BIN2
         AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
    ) <> 13
        THROW 59505, N'BMS W47 refused: exact schema versions 1 through 13 are required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 14
          AND
          (
              script_name COLLATE Latin1_General_100_BIN2 <>
                  N'014_contracts_reference.sql'
              OR DATALENGTH(script_name) <>
                  DATALENGTH(N'014_contracts_reference.sql')
          )
    )
        THROW 59506, N'BMS W47 refused: schema version 14 belongs to another script.', 1;

    IF OBJECT_ID(N'bms_web.Contracts', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.Contracts
        (
            Id              uniqueidentifier NOT NULL,
            CounterpartyId  uniqueidentifier NOT NULL,
            OrganizationId  uniqueidentifier NOT NULL,
            Code            nvarchar(30) COLLATE Latin1_General_100_BIN2 NOT NULL,
            Name            nvarchar(150) NOT NULL,
            ContractDate    date NOT NULL,
            CurrencyId      uniqueidentifier NOT NULL,
            IsActive        bit NOT NULL,
            CONSTRAINT PK_bms_web_Contracts PRIMARY KEY CLUSTERED (Id ASC),
            CONSTRAINT UQ_bms_web_Contracts_Code UNIQUE NONCLUSTERED (Code ASC)
        );

        CREATE NONCLUSTERED INDEX IX_bms_web_Contracts_Counterparty_Organization_Active_Code
            ON bms_web.Contracts (CounterpartyId ASC, OrganizationId ASC, IsActive ASC, Code ASC);
    END;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'Id', N'uniqueidentifier', 16, 0, CAST(NULL AS nvarchar(128))),
                (N'CounterpartyId', N'uniqueidentifier', 16, 0, CAST(NULL AS nvarchar(128))),
                (N'OrganizationId', N'uniqueidentifier', 16, 0, CAST(NULL AS nvarchar(128))),
                (N'Code', N'nvarchar', 60, 0, N'Latin1_General_100_BIN2'),
                (N'Name', N'nvarchar', 300, 0,
                    CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128))),
                (N'ContractDate', N'date', 3, 0, CAST(NULL AS nvarchar(128))),
                (N'CurrencyId', N'uniqueidentifier', 16, 0, CAST(NULL AS nvarchar(128))),
                (N'IsActive', N'bit', 1, 0, CAST(NULL AS nvarchar(128)))
        ) AS expected(ColumnName, TypeName, MaxLength, IsNullable, CollationName)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(N'bms_web.Contracts')
         AND c.name = expected.ColumnName
        WHERE c.object_id IS NULL
           OR TYPE_NAME(c.user_type_id) <> expected.TypeName
           OR c.max_length <> expected.MaxLength
           OR c.is_nullable <> expected.IsNullable
           OR c.is_computed <> 0 OR c.is_identity <> 0
           OR c.is_rowguidcol <> 0 OR c.is_filestream <> 0
           OR c.default_object_id <> 0 OR c.rule_object_id <> 0
           OR c.generated_always_type <> 0
           OR c.is_sparse <> 0 OR c.is_column_set <> 0
           OR c.encryption_type IS NOT NULL OR c.is_hidden <> 0
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
           WHERE object_id = OBJECT_ID(N'bms_web.Contracts')) <> 8
        THROW 59507, N'BMS W47 refused: Contracts column contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'PK_bms_web_Contracts', 1, 1, 1, 0, 1),
                (N'UQ_bms_web_Contracts_Code', 2, 1, 0, 1, 1),
                (N'IX_bms_web_Contracts_Counterparty_Organization_Active_Code', 2, 0, 0, 0, 4)
        ) AS expected(IndexName, IndexType, IsUnique, IsPrimaryKey, IsUniqueConstraint, KeyCount)
        LEFT JOIN sys.indexes AS i
          ON i.object_id = OBJECT_ID(N'bms_web.Contracts')
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
           WHERE parent_object_id = OBJECT_ID(N'bms_web.Contracts')) <> 2
       OR (SELECT COUNT(*) FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'bms_web.Contracts')
             AND index_id > 0 AND is_unique = 1) <> 2
       OR (SELECT COUNT(*) FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'bms_web.Contracts')
             AND index_id > 0) <> 3
        THROW 59508, N'BMS W47 refused: Contracts key/index contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'PK_bms_web_Contracts', 1, N'Id'),
                (N'UQ_bms_web_Contracts_Code', 1, N'Code'),
                (N'IX_bms_web_Contracts_Counterparty_Organization_Active_Code', 1, N'CounterpartyId'),
                (N'IX_bms_web_Contracts_Counterparty_Organization_Active_Code', 2, N'OrganizationId'),
                (N'IX_bms_web_Contracts_Counterparty_Organization_Active_Code', 3, N'IsActive'),
                (N'IX_bms_web_Contracts_Counterparty_Organization_Active_Code', 4, N'Code')
        ) AS expected(IndexName, KeyOrdinal, ColumnName)
        LEFT JOIN sys.indexes AS i
          ON i.object_id = OBJECT_ID(N'bms_web.Contracts')
         AND i.name = expected.IndexName
        LEFT JOIN sys.index_columns AS ic
          ON ic.object_id = i.object_id AND ic.index_id = i.index_id
         AND ic.key_ordinal = expected.KeyOrdinal
        LEFT JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE c.name <> expected.ColumnName OR c.name IS NULL
           OR ic.is_descending_key <> 0 OR ic.is_included_column <> 0
    )
        THROW 59509, N'BMS W47 refused: Contracts key order is drifted.', 1;

    IF (SELECT COUNT(*) FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'bms_web.Contracts')
           OR referenced_object_id = OBJECT_ID(N'bms_web.Contracts')) <> 0
        THROW 59510, N'BMS W47 refused: Contracts must have no inbound or outbound foreign keys.', 1;

    IF (SELECT COUNT(*) FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.Contracts')) <> 0
        THROW 59511, N'BMS W47 refused: Contracts must have no invented CHECK rules.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.triggers
        WHERE parent_id = OBJECT_ID(N'bms_web.Contracts')
          AND is_ms_shipped = 0
    )
        THROW 59512, N'BMS W47 refused: Contracts has a user DML trigger.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 14
    )
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (14, N'014_contracts_reference.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
