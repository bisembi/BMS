-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web W58 — read-only currencies and daily exchange-rate projection.

    IMPORTANT
    - Apply 001 through 015 first and run this script standalone.
    - This script creates no database, login, user, role, permission, import or seed row.
    - Currencies and CurrencyRates remain empty until a separately controlled
      source import with freshness and reconciliation evidence is approved.
    - The physical source rate remains decimal(15,6), as defined by the source
      information register BMS_КурсыВалют. A future expense-request snapshot
      uses its separate decimal(18,6) contract and is not written here.
    - PublishedAtSource is a source-local timestamp and carries no UTC claim.
    - It has not been executed against an accounting server.
*/

IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS W58 refused: run 016_currencies_and_daily_rates.sql outside an ambient transaction.', 16, 1);
    RETURN;
END;

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 59701, N'BMS W58 refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 59702, N'BMS W58 refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
    THROW 59703, N'BMS W58 refused: managed schema bms_web is absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no > 16
    )
        THROW 59704, N'BMS W58 refused: a future schema version is already registered.', 1;

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
                (15, N'015_employee_functional_roles.sql')
        ) AS expected(version_no, script_name)
        INNER JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK)
          ON actual.version_no = expected.version_no
         AND actual.script_name COLLATE Latin1_General_100_BIN2 =
             expected.script_name COLLATE Latin1_General_100_BIN2
         AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
    ) <> 15
        THROW 59705, N'BMS W58 refused: exact schema versions 1 through 15 are required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 16
          AND
          (
              script_name COLLATE Latin1_General_100_BIN2 <>
                  N'016_currencies_and_daily_rates.sql'
              OR DATALENGTH(script_name) <>
                  DATALENGTH(N'016_currencies_and_daily_rates.sql')
          )
    )
        THROW 59706, N'BMS W58 refused: schema version 16 belongs to another script.', 1;

    IF
    (
        CASE WHEN OBJECT_ID(N'bms_web.Currencies', N'U') IS NULL THEN 0 ELSE 1 END +
        CASE WHEN OBJECT_ID(N'bms_web.CurrencyRates', N'U') IS NULL THEN 0 ELSE 1 END
    ) = 1
        THROW 59707, N'BMS W58 refused: a partial currency projection already exists.', 1;

    IF OBJECT_ID(N'bms_web.Currencies', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.Currencies
        (
            Id           uniqueidentifier NOT NULL,
            Code         nvarchar(3) COLLATE Latin1_General_100_BIN2 NOT NULL,
            Name         nvarchar(50) NOT NULL,
            NumericCode  nchar(3) COLLATE Latin1_General_100_BIN2 NOT NULL,
            IsActive     bit NOT NULL,
            CONSTRAINT PK_bms_web_Currencies PRIMARY KEY CLUSTERED (Id ASC),
            CONSTRAINT UQ_bms_web_Currencies_Code UNIQUE NONCLUSTERED (Code ASC)
        );

        CREATE NONCLUSTERED INDEX IX_bms_web_Currencies_Active_Code
            ON bms_web.Currencies (IsActive ASC, Code ASC);

        CREATE TABLE bms_web.CurrencyRates
        (
            CurrencyId        uniqueidentifier NOT NULL,
            RateDate          date NOT NULL,
            Rate              decimal(15,6) NOT NULL,
            Multiplicity      decimal(10,0) NOT NULL,
            PublishedAtSource datetime2(0) NOT NULL,
            SourceName        nvarchar(100) NOT NULL,
            CONSTRAINT PK_bms_web_CurrencyRates PRIMARY KEY CLUSTERED
                (CurrencyId ASC, RateDate ASC),
            CONSTRAINT CK_bms_web_CurrencyRates_Rate_Nonnegative
                CHECK (Rate >= 0),
            CONSTRAINT CK_bms_web_CurrencyRates_Multiplicity_Nonnegative
                CHECK (Multiplicity >= 0),
            CONSTRAINT FK_bms_web_CurrencyRates_Currency
                FOREIGN KEY (CurrencyId) REFERENCES bms_web.Currencies (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION
        );
    END;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'Id', N'uniqueidentifier', 16, 0, 0, 0,
                    CAST(NULL AS nvarchar(128))),
                (N'Code', N'nvarchar', 6, 0, 0, 0,
                    N'Latin1_General_100_BIN2'),
                (N'Name', N'nvarchar', 100, 0, 0, 0,
                    CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128))),
                (N'NumericCode', N'nchar', 6, 0, 0, 0,
                    N'Latin1_General_100_BIN2'),
                (N'IsActive', N'bit', 1, 0, 0, 0,
                    CAST(NULL AS nvarchar(128)))
        ) AS expected(ColumnName, TypeName, MaxLength, IsNullable, PrecisionValue,
            ScaleValue, CollationName)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(N'bms_web.Currencies')
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
           WHERE object_id = OBJECT_ID(N'bms_web.Currencies')) <> 5
        THROW 59708, N'BMS W58 refused: Currencies column contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'CurrencyId', N'uniqueidentifier', 16, 0, 0, 0,
                    CAST(NULL AS nvarchar(128))),
                (N'RateDate', N'date', 3, 0, 10, 0,
                    CAST(NULL AS nvarchar(128))),
                (N'Rate', N'decimal', 9, 0, 15, 6,
                    CAST(NULL AS nvarchar(128))),
                (N'Multiplicity', N'decimal', 9, 0, 10, 0,
                    CAST(NULL AS nvarchar(128))),
                (N'PublishedAtSource', N'datetime2', 6, 0, 19, 0,
                    CAST(NULL AS nvarchar(128))),
                (N'SourceName', N'nvarchar', 200, 0, 0, 0,
                    CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128)))
        ) AS expected(ColumnName, TypeName, MaxLength, IsNullable, PrecisionValue,
            ScaleValue, CollationName)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(N'bms_web.CurrencyRates')
         AND c.name = expected.ColumnName
        WHERE c.object_id IS NULL
           OR TYPE_NAME(c.user_type_id) <> expected.TypeName
           OR c.max_length <> expected.MaxLength
           OR c.is_nullable <> expected.IsNullable
           OR c.precision <> expected.PrecisionValue
           OR c.scale <> expected.ScaleValue
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
           WHERE object_id = OBJECT_ID(N'bms_web.CurrencyRates')) <> 6
        THROW 59709, N'BMS W58 refused: CurrencyRates column contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.Currencies', N'PK_bms_web_Currencies', 1, 1, 1, 0, 1),
                (N'bms_web.Currencies', N'UQ_bms_web_Currencies_Code', 2, 1, 0, 1, 1),
                (N'bms_web.Currencies', N'IX_bms_web_Currencies_Active_Code', 2, 0, 0, 0, 2),
                (N'bms_web.CurrencyRates', N'PK_bms_web_CurrencyRates', 1, 1, 1, 0, 2)
        ) AS expected(TableName, IndexName, IndexType, IsUnique, IsPrimaryKey,
            IsUniqueConstraint, KeyCount)
        LEFT JOIN sys.indexes AS i
          ON i.object_id = OBJECT_ID(expected.TableName)
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
           WHERE parent_object_id = OBJECT_ID(N'bms_web.Currencies')) <> 2
       OR (SELECT COUNT(*) FROM sys.key_constraints
           WHERE parent_object_id = OBJECT_ID(N'bms_web.CurrencyRates')) <> 1
       OR (SELECT COUNT(*) FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'bms_web.Currencies') AND index_id > 0) <> 3
       OR (SELECT COUNT(*) FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'bms_web.CurrencyRates') AND index_id > 0) <> 1
       OR (SELECT COUNT(*) FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'bms_web.Currencies')
             AND index_id > 0 AND is_unique = 1) <> 2
       OR (SELECT COUNT(*) FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'bms_web.CurrencyRates')
             AND index_id > 0 AND is_unique = 1) <> 1
        THROW 59710, N'BMS W58 refused: currency key and index contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.Currencies', N'PK_bms_web_Currencies', 1, N'Id'),
                (N'bms_web.Currencies', N'UQ_bms_web_Currencies_Code', 1, N'Code'),
                (N'bms_web.Currencies', N'IX_bms_web_Currencies_Active_Code', 1, N'IsActive'),
                (N'bms_web.Currencies', N'IX_bms_web_Currencies_Active_Code', 2, N'Code'),
                (N'bms_web.CurrencyRates', N'PK_bms_web_CurrencyRates', 1, N'CurrencyId'),
                (N'bms_web.CurrencyRates', N'PK_bms_web_CurrencyRates', 2, N'RateDate')
        ) AS expected(TableName, IndexName, KeyOrdinal, ColumnName)
        LEFT JOIN sys.indexes AS i
          ON i.object_id = OBJECT_ID(expected.TableName)
         AND i.name = expected.IndexName
        LEFT JOIN sys.index_columns AS ic
          ON ic.object_id = i.object_id AND ic.index_id = i.index_id
         AND ic.key_ordinal = expected.KeyOrdinal
        LEFT JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE c.name <> expected.ColumnName OR c.name IS NULL
           OR ic.is_descending_key <> 0 OR ic.is_included_column <> 0
    )
        THROW 59711, N'BMS W58 refused: currency key order is drifted.', 1;

    IF (SELECT COUNT(*) FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'bms_web.CurrencyRates')) <> 1
       OR NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys AS fk
        INNER JOIN sys.foreign_key_columns AS fkc
          ON fkc.constraint_object_id = fk.object_id
        INNER JOIN sys.columns AS parent_column
          ON parent_column.object_id = fkc.parent_object_id
         AND parent_column.column_id = fkc.parent_column_id
        INNER JOIN sys.columns AS referenced_column
          ON referenced_column.object_id = fkc.referenced_object_id
         AND referenced_column.column_id = fkc.referenced_column_id
        WHERE fk.name = N'FK_bms_web_CurrencyRates_Currency'
          AND fk.parent_object_id = OBJECT_ID(N'bms_web.CurrencyRates')
          AND fk.referenced_object_id = OBJECT_ID(N'bms_web.Currencies')
          AND parent_column.name = N'CurrencyId'
          AND referenced_column.name = N'Id'
          AND fk.delete_referential_action = 0
          AND fk.update_referential_action = 0
          AND fk.is_disabled = 0
          AND fk.is_not_trusted = 0
          AND fk.is_not_for_replication = 0
          AND fk.is_system_named = 0
          AND (SELECT COUNT(*) FROM sys.foreign_key_columns AS exact_fkc
               WHERE exact_fkc.constraint_object_id = fk.object_id) = 1
    )
       OR (SELECT COUNT(*) FROM sys.foreign_keys
           WHERE parent_object_id = OBJECT_ID(N'bms_web.Currencies')) <> 0
       OR (SELECT COUNT(*) FROM sys.foreign_keys
           WHERE referenced_object_id = OBJECT_ID(N'bms_web.Currencies')) <> 1
       OR (SELECT COUNT(*) FROM sys.foreign_keys
           WHERE referenced_object_id = OBJECT_ID(N'bms_web.CurrencyRates')) <> 0
        THROW 59712, N'BMS W58 refused: currency foreign-key contract is missing or drifted.', 1;

    IF (SELECT COUNT(*) FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.Currencies')) <> 0
       OR (SELECT COUNT(*) FROM sys.check_constraints
           WHERE parent_object_id = OBJECT_ID(N'bms_web.CurrencyRates')) <> 2
       OR EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'CK_bms_web_CurrencyRates_Rate_Nonnegative', N'rate>=0', N'Rate'),
                (N'CK_bms_web_CurrencyRates_Multiplicity_Nonnegative', N'multiplicity>=0', N'Multiplicity')
        ) AS expected(ConstraintName, NormalizedDefinition, ParentColumnName)
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM sys.check_constraints AS cc
            CROSS APPLY
            (
                VALUES
                (
                    LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        cc.definition, N'[', N''), N']', N''), N'(', N''), N')', N''),
                        N' ', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N''))
                )
            ) AS actual(NormalizedDefinition)
            WHERE cc.parent_object_id = OBJECT_ID(N'bms_web.CurrencyRates')
              AND cc.name = expected.ConstraintName
              AND cc.is_disabled = 0
              AND cc.is_not_trusted = 0
              AND cc.is_not_for_replication = 0
              AND cc.is_system_named = 0
              AND cc.parent_column_id = COLUMNPROPERTY(
                  OBJECT_ID(N'bms_web.CurrencyRates'), expected.ParentColumnName, N'ColumnId')
              AND actual.NormalizedDefinition COLLATE Latin1_General_100_BIN2 =
                  expected.NormalizedDefinition COLLATE Latin1_General_100_BIN2
        )
    )
        THROW 59713, N'BMS W58 refused: currency CHECK contract is missing or drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.triggers
        WHERE parent_id IN
        (
            OBJECT_ID(N'bms_web.Currencies'),
            OBJECT_ID(N'bms_web.CurrencyRates')
        )
          AND is_ms_shipped = 0
    )
        THROW 59714, N'BMS W58 refused: a currency table has a user DML trigger.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 16
    )
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (16, N'016_currencies_and_daily_rates.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
