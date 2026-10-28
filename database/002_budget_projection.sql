-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web — creation-only budget position and expense-request projection slice.

    IMPORTANT
    - Apply 001_bootstrap_bms_web.sql first.
    - This script does not create a database, login, user, role or permission.
    - It refuses to run outside the explicitly owned BMS database and outside the managed bms_web schema.
    - It creates objects only in bms_web and contains no destructive DDL/DML.
    - It has not been executed against an accounting server.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 52001, N'BMS budget projection refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 52002, N'BMS budget projection refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
    THROW 52003, N'BMS budget projection refused: managed schema bms_web is absent.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM bms_web.schema_version
    WHERE version_no = 1
      AND script_name = N'001_bootstrap_bms_web.sql'
)
    THROW 52004, N'BMS budget projection refused: schema version 1 is not registered.', 1;

IF EXISTS
(
    SELECT 1
    FROM bms_web.schema_version
    WHERE version_no = 2
      AND script_name <> N'002_budget_projection.sql'
)
    THROW 52009, N'BMS budget projection refused: schema version 2 belongs to another script.', 1;

IF OBJECT_ID(N'bms_web.ExpenseRequests', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.ExpenseRequestLines', N'U') IS NULL
    THROW 52005, N'BMS budget projection refused: draft request tables are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    /*
        W26 later converts the managed money columns to decimal(18,2).
        A rerun after that exact, registered migration must validate the
        canonical shape rather than mistaking it for manual W-03 drift.
    */
    DECLARE @W26CanonicalMoneyScaleApplied bit = CASE WHEN EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version
        WHERE version_no = 12
          AND script_name COLLATE Latin1_General_100_BIN2 =
              N'012_money_decimal18_scale2.sql'
          AND DATALENGTH(script_name) =
              DATALENGTH(N'012_money_decimal18_scale2.sql')
    ) THEN 1 ELSE 0 END;
    DECLARE @ExpectedMoneyPrecision tinyint =
        CASE WHEN @W26CanonicalMoneyScaleApplied = 1 THEN 18 ELSE 19 END;
    DECLARE @ExpectedMoneyScale tinyint =
        CASE WHEN @W26CanonicalMoneyScaleApplied = 1 THEN 2 ELSE 4 END;

    /*
        W-03 adds server-calculated VAT/KZT values to request lines. The script
        intentionally refuses an automatic backfill if legacy rows exist: the
        business values must not be guessed or balanced by a deployment script.
    */
    IF
    (
        COL_LENGTH(N'bms_web.ExpenseRequestLines', N'VatRateCode') IS NULL
        OR COL_LENGTH(N'bms_web.ExpenseRequestLines', N'VatAmount') IS NULL
        OR COL_LENGTH(N'bms_web.ExpenseRequestLines', N'AmountExcludingVat') IS NULL
        OR COL_LENGTH(N'bms_web.ExpenseRequestLines', N'AmountKzt') IS NULL
    )
    AND EXISTS (SELECT 1 FROM bms_web.ExpenseRequestLines)
        THROW 52007, N'BMS W-03 refused: existing request lines require a reviewed VAT/KZT migration.', 1;

    IF COL_LENGTH(N'bms_web.ExpenseRequestLines', N'VatRateCode') IS NULL
        ALTER TABLE bms_web.ExpenseRequestLines
            ADD VatRateCode nvarchar(20) NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequestLines', N'VatAmount') IS NULL
        ALTER TABLE bms_web.ExpenseRequestLines
            ADD VatAmount decimal(19,4) NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequestLines', N'AmountExcludingVat') IS NULL
        ALTER TABLE bms_web.ExpenseRequestLines
            ADD AmountExcludingVat decimal(19,4) NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequestLines', N'AmountKzt') IS NULL
        ALTER TABLE bms_web.ExpenseRequestLines
            ADD AmountKzt decimal(19,4) NOT NULL
                CONSTRAINT DF_bms_web_ExpenseRequestLines_AmountKzt DEFAULT (0);

    /*
        SQL Server binds a CHECK definition before the preceding ALTER TABLE
        additions in this batch. These two literal payloads defer only that
        binding until the four calculated columns exist; the outer transaction
        and the checked constraint definitions remain unchanged.
    */
    IF OBJECT_ID(N'bms_web.CK_ExpenseRequestLines_VatRateCode', N'C') IS NULL
        EXEC(N'ALTER TABLE bms_web.ExpenseRequestLines WITH CHECK
            ADD CONSTRAINT CK_ExpenseRequestLines_VatRateCode CHECK
                (VatRateCode IS NULL OR VatRateCode IN (N''WithoutVat'', N''Vat16''));');
    IF OBJECT_ID(N'bms_web.CK_ExpenseRequestLines_CalculatedAmounts', N'C') IS NULL
        EXEC(N'ALTER TABLE bms_web.ExpenseRequestLines WITH CHECK
            ADD CONSTRAINT CK_ExpenseRequestLines_CalculatedAmounts CHECK
                (AmountKzt = Amount
                 AND
                 (
                     (VatRateCode IS NULL
                      AND VatAmount IS NULL
                      AND AmountExcludingVat IS NULL)
                     OR
                     (VatRateCode = N''WithoutVat''
                      AND VatAmount = 0
                      AND AmountExcludingVat = Amount)
                     OR
                     (VatRateCode = N''Vat16''
                      AND VatAmount = ROUND(Amount * 16 / 116, 2)
                      AND AmountExcludingVat = Amount - VatAmount)
                 ));');

    IF OBJECT_ID(N'bms_web.BudgetPositions', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.BudgetPositions
        (
            CostCenterId       uniqueidentifier NOT NULL,
            CashFlowItemId     uniqueidentifier NOT NULL,
            BudgetMonth        date             NOT NULL,
            LimitAmountKzt     decimal(19,4)    NULL,
            ReservedAmountKzt  decimal(19,4)    NOT NULL
                CONSTRAINT DF_bms_web_BudgetPositions_Reserved DEFAULT (0),
            PaidAmountKzt      decimal(19,4)    NOT NULL
                CONSTRAINT DF_bms_web_BudgetPositions_Paid DEFAULT (0),
            ConcurrencyStamp   uniqueidentifier NOT NULL,
            CONSTRAINT PK_bms_web_BudgetPositions PRIMARY KEY
                (CostCenterId, CashFlowItemId, BudgetMonth),
            CONSTRAINT CK_bms_web_BudgetPositions_month_start CHECK
                (DAY(BudgetMonth) = 1),
            CONSTRAINT CK_bms_web_BudgetPositions_limit CHECK
                (LimitAmountKzt IS NULL OR LimitAmountKzt >= 0),
            CONSTRAINT CK_bms_web_BudgetPositions_reserved CHECK
                (ReservedAmountKzt >= 0),
            CONSTRAINT CK_bms_web_BudgetPositions_paid CHECK
                (PaidAmountKzt >= 0)
        );
    END;

    IF OBJECT_ID(N'bms_web.BudgetRequestProjections', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.BudgetRequestProjections
        (
            Id                   uniqueidentifier NOT NULL,
            ExpenseRequestId     uniqueidentifier NOT NULL,
            ExpenseRequestLineId uniqueidentifier NOT NULL,
            CostCenterId         uniqueidentifier NOT NULL,
            CashFlowItemId       uniqueidentifier NOT NULL,
            BudgetMonth          date             NOT NULL,
            ReservedAmountKzt    decimal(19,4)    NOT NULL,
            CONSTRAINT PK_bms_web_BudgetRequestProjections PRIMARY KEY (Id),
            CONSTRAINT UQ_bms_web_BudgetRequestProjections_Line UNIQUE
                (ExpenseRequestLineId),
            CONSTRAINT CK_bms_web_BudgetRequestProjections_month_start CHECK
                (DAY(BudgetMonth) = 1),
            CONSTRAINT CK_bms_web_BudgetRequestProjections_reserved CHECK
                (ReservedAmountKzt > 0),
            CONSTRAINT FK_bms_web_BudgetRequestProjections_Line FOREIGN KEY
                (ExpenseRequestLineId) REFERENCES bms_web.ExpenseRequestLines (Id)
                ON DELETE NO ACTION,
            CONSTRAINT FK_bms_web_BudgetRequestProjections_Request FOREIGN KEY
                (ExpenseRequestId) REFERENCES bms_web.ExpenseRequests (Id)
                ON DELETE NO ACTION,
            CONSTRAINT FK_bms_web_BudgetRequestProjections_Position FOREIGN KEY
                (CostCenterId, CashFlowItemId, BudgetMonth)
                REFERENCES bms_web.BudgetPositions
                    (CostCenterId, CashFlowItemId, BudgetMonth)
                ON DELETE NO ACTION
        );
    END;

    /* Refuse silent success against a manually drifted W-03 schema. */
    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.BudgetPositions', N'CostCenterId', N'uniqueidentifier', 0, 0),
                (N'bms_web.BudgetPositions', N'CashFlowItemId', N'uniqueidentifier', 0, 0),
                (N'bms_web.BudgetPositions', N'BudgetMonth', N'date', 0, 0),
                (N'bms_web.BudgetPositions', N'LimitAmountKzt', N'decimal', @ExpectedMoneyPrecision, @ExpectedMoneyScale),
                (N'bms_web.BudgetPositions', N'ReservedAmountKzt', N'decimal', @ExpectedMoneyPrecision, @ExpectedMoneyScale),
                (N'bms_web.BudgetPositions', N'PaidAmountKzt', N'decimal', @ExpectedMoneyPrecision, @ExpectedMoneyScale),
                (N'bms_web.BudgetPositions', N'ConcurrencyStamp', N'uniqueidentifier', 0, 0),
                (N'bms_web.BudgetRequestProjections', N'Id', N'uniqueidentifier', 0, 0),
                (N'bms_web.BudgetRequestProjections', N'ExpenseRequestId', N'uniqueidentifier', 0, 0),
                (N'bms_web.BudgetRequestProjections', N'ExpenseRequestLineId', N'uniqueidentifier', 0, 0),
                (N'bms_web.BudgetRequestProjections', N'CostCenterId', N'uniqueidentifier', 0, 0),
                (N'bms_web.BudgetRequestProjections', N'CashFlowItemId', N'uniqueidentifier', 0, 0),
                (N'bms_web.BudgetRequestProjections', N'BudgetMonth', N'date', 0, 0),
                (N'bms_web.BudgetRequestProjections', N'ReservedAmountKzt', N'decimal', @ExpectedMoneyPrecision, @ExpectedMoneyScale)
        ) AS expected(ObjectName, ColumnName, TypeName, [Precision], Scale)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(expected.ObjectName)
         AND c.name = expected.ColumnName
        WHERE c.object_id IS NULL
           OR TYPE_NAME(c.user_type_id) <> expected.TypeName
           OR (expected.TypeName = N'decimal'
               AND (c.[precision] <> expected.[Precision] OR c.scale <> expected.Scale))
    )
        THROW 52006, N'BMS budget projection refused: W-03 column contract is missing or drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'VatRateCode', N'nvarchar', 0, 0, 40),
                (N'VatAmount', N'decimal', @ExpectedMoneyPrecision, @ExpectedMoneyScale, 9),
                (N'AmountExcludingVat', N'decimal', @ExpectedMoneyPrecision, @ExpectedMoneyScale, 9),
                (N'AmountKzt', N'decimal', @ExpectedMoneyPrecision, @ExpectedMoneyScale, 9)
        ) AS expected(ColumnName, TypeName, [Precision], Scale, MaxLength)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
         AND c.name = expected.ColumnName
        WHERE c.object_id IS NULL
           OR TYPE_NAME(c.user_type_id) <> expected.TypeName
           OR c.max_length <> expected.MaxLength
           OR (expected.TypeName = N'decimal'
               AND (c.[precision] <> expected.[Precision] OR c.scale <> expected.Scale))
    )
        THROW 52008, N'BMS W-03 refused: request-line calculated columns are missing or drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.BudgetPositions', N'CostCenterId', 0),
                (N'bms_web.BudgetPositions', N'CashFlowItemId', 0),
                (N'bms_web.BudgetPositions', N'BudgetMonth', 0),
                (N'bms_web.BudgetPositions', N'LimitAmountKzt', 1),
                (N'bms_web.BudgetPositions', N'ReservedAmountKzt', 0),
                (N'bms_web.BudgetPositions', N'PaidAmountKzt', 0),
                (N'bms_web.BudgetPositions', N'ConcurrencyStamp', 0),
                (N'bms_web.BudgetRequestProjections', N'Id', 0),
                (N'bms_web.BudgetRequestProjections', N'ExpenseRequestId', 0),
                (N'bms_web.BudgetRequestProjections', N'ExpenseRequestLineId', 0),
                (N'bms_web.BudgetRequestProjections', N'CostCenterId', 0),
                (N'bms_web.BudgetRequestProjections', N'CashFlowItemId', 0),
                (N'bms_web.BudgetRequestProjections', N'BudgetMonth', 0),
                (N'bms_web.BudgetRequestProjections', N'ReservedAmountKzt', 0)
        ) AS expected(ObjectName, ColumnName, IsNullable)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(expected.ObjectName)
         AND c.name = expected.ColumnName
        WHERE c.object_id IS NULL OR c.is_nullable <> expected.IsNullable
    )
        THROW 52010, N'BMS budget projection refused: W-03 nullability contract is drifted.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.key_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.BudgetPositions')
          AND [type] = N'PK'
          AND name = N'PK_bms_web_BudgetPositions'
    )
       OR NOT EXISTS
    (
        SELECT 1
        FROM sys.key_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')
          AND [type] = N'PK'
          AND name = N'PK_bms_web_BudgetRequestProjections'
    )
       OR NOT EXISTS
    (
        SELECT 1
        FROM sys.key_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')
          AND [type] = N'UQ'
          AND name = N'UQ_bms_web_BudgetRequestProjections_Line'
    )
        THROW 52011, N'BMS budget projection refused: W-03 key constraints are missing.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')
          AND name IN
          (
              N'FK_bms_web_BudgetRequestProjections_Line',
              N'FK_bms_web_BudgetRequestProjections_Request',
              N'FK_bms_web_BudgetRequestProjections_Position'
          )
          AND delete_referential_action = 0
    ) <> 3
        THROW 52012, N'BMS budget projection refused: W-03 NO ACTION foreign keys are missing.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')
          AND name = N'IX_BudgetRequestProjections_ExpenseRequestId'
    )
        CREATE INDEX IX_BudgetRequestProjections_ExpenseRequestId
            ON bms_web.BudgetRequestProjections (ExpenseRequestId);

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')
          AND name = N'IX_BudgetRequestProjections_BudgetKey'
    )
        CREATE INDEX IX_BudgetRequestProjections_BudgetKey
            ON bms_web.BudgetRequestProjections
                (CostCenterId, CashFlowItemId, BudgetMonth)
            INCLUDE (ReservedAmountKzt, ExpenseRequestId);

    /*
        Exact W-03 physical contract. Existing objects must not be accepted by
        name alone: key order, includes, FK mappings, trusted CHECK/default
        bodies and trigger absence are part of the budget-write semantics.
    */
    IF (SELECT COUNT(*) FROM sys.columns
        WHERE object_id = OBJECT_ID(N'bms_web.BudgetPositions')) <> 7
       OR (SELECT COUNT(*) FROM sys.columns
           WHERE object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')) <> 7
        THROW 52013, N'BMS budget projection refused: W-03 column count is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.BudgetPositions', N'CostCenterId', N'uniqueidentifier', 16, -1, -1, 0, CAST(NULL AS nvarchar(128))),
                (N'bms_web.BudgetPositions', N'CashFlowItemId', N'uniqueidentifier', 16, -1, -1, 0, CAST(NULL AS nvarchar(128))),
                (N'bms_web.BudgetPositions', N'BudgetMonth', N'date', 3, -1, -1, 0, CAST(NULL AS nvarchar(128))),
                (N'bms_web.BudgetPositions', N'LimitAmountKzt', N'decimal', 9, @ExpectedMoneyPrecision, @ExpectedMoneyScale, 1, CAST(NULL AS nvarchar(128))),
                (N'bms_web.BudgetPositions', N'ReservedAmountKzt', N'decimal', 9, @ExpectedMoneyPrecision, @ExpectedMoneyScale, 0, N'DF_bms_web_BudgetPositions_Reserved'),
                (N'bms_web.BudgetPositions', N'PaidAmountKzt', N'decimal', 9, @ExpectedMoneyPrecision, @ExpectedMoneyScale, 0, N'DF_bms_web_BudgetPositions_Paid'),
                (N'bms_web.BudgetPositions', N'ConcurrencyStamp', N'uniqueidentifier', 16, -1, -1, 0, CAST(NULL AS nvarchar(128))),
                (N'bms_web.BudgetRequestProjections', N'Id', N'uniqueidentifier', 16, -1, -1, 0, CAST(NULL AS nvarchar(128))),
                (N'bms_web.BudgetRequestProjections', N'ExpenseRequestId', N'uniqueidentifier', 16, -1, -1, 0, CAST(NULL AS nvarchar(128))),
                (N'bms_web.BudgetRequestProjections', N'ExpenseRequestLineId', N'uniqueidentifier', 16, -1, -1, 0, CAST(NULL AS nvarchar(128))),
                (N'bms_web.BudgetRequestProjections', N'CostCenterId', N'uniqueidentifier', 16, -1, -1, 0, CAST(NULL AS nvarchar(128))),
                (N'bms_web.BudgetRequestProjections', N'CashFlowItemId', N'uniqueidentifier', 16, -1, -1, 0, CAST(NULL AS nvarchar(128))),
                (N'bms_web.BudgetRequestProjections', N'BudgetMonth', N'date', 3, -1, -1, 0, CAST(NULL AS nvarchar(128))),
                (N'bms_web.BudgetRequestProjections', N'ReservedAmountKzt', N'decimal', 9, @ExpectedMoneyPrecision, @ExpectedMoneyScale, 0, CAST(NULL AS nvarchar(128)))
        ) AS expected
            (ObjectName, ColumnName, TypeName, MaxLength, [Precision], Scale, IsNullable, DefaultName)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(expected.ObjectName)
         AND c.name = expected.ColumnName
        LEFT JOIN sys.default_constraints AS dc
          ON dc.object_id = c.default_object_id
        WHERE c.object_id IS NULL
           OR TYPE_NAME(c.user_type_id) <> expected.TypeName
           OR c.max_length <> expected.MaxLength
           OR (expected.[Precision] >= 0
               AND (c.[precision] <> expected.[Precision] OR c.scale <> expected.Scale))
           OR c.is_nullable <> expected.IsNullable
           OR c.collation_name IS NOT NULL
           OR c.is_computed <> 0 OR c.is_identity <> 0 OR c.is_rowguidcol <> 0
           OR c.is_filestream <> 0 OR c.rule_object_id <> 0
           OR c.generated_always_type <> 0 OR c.is_sparse <> 0
           OR c.is_column_set <> 0 OR c.encryption_type IS NOT NULL
           OR c.is_hidden <> 0 OR c.is_masked <> 0
           OR (expected.DefaultName IS NULL AND c.default_object_id <> 0)
           OR (expected.DefaultName IS NOT NULL
               AND (c.default_object_id = 0 OR dc.name <> expected.DefaultName))
    )
        THROW 52014, N'BMS budget projection refused: W-03 exact column metadata is drifted.', 1;

    IF (SELECT COUNT(*) FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'bms_web.BudgetPositions') AND index_id > 0) <> 1
       OR (SELECT COUNT(*) FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections') AND index_id > 0) <> 4
       OR EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.BudgetPositions', N'PK_bms_web_BudgetPositions', 1, 1, 1, 0, 3, 0),
                (N'bms_web.BudgetRequestProjections', N'PK_bms_web_BudgetRequestProjections', 1, 1, 1, 0, 1, 0),
                (N'bms_web.BudgetRequestProjections', N'UQ_bms_web_BudgetRequestProjections_Line', 2, 1, 0, 1, 1, 0),
                (N'bms_web.BudgetRequestProjections', N'IX_BudgetRequestProjections_ExpenseRequestId', 2, 0, 0, 0, 1, 0),
                (N'bms_web.BudgetRequestProjections', N'IX_BudgetRequestProjections_BudgetKey', 2, 0, 0, 0, 3, 2)
        ) AS expected
            (ObjectName, IndexName, IndexType, IsUnique, IsPrimaryKey, IsUniqueConstraint, KeyCount, IncludeCount)
        LEFT JOIN sys.indexes AS i
          ON i.object_id = OBJECT_ID(expected.ObjectName)
         AND i.name = expected.IndexName
        WHERE i.object_id IS NULL
           OR i.[type] <> expected.IndexType
           OR i.is_unique <> expected.IsUnique
           OR i.is_primary_key <> expected.IsPrimaryKey
           OR i.is_unique_constraint <> expected.IsUniqueConstraint
           OR i.is_disabled <> 0 OR i.is_hypothetical <> 0
           OR i.has_filter <> 0 OR i.filter_definition IS NOT NULL
           OR i.ignore_dup_key <> 0
           OR (SELECT COUNT(*) FROM sys.index_columns AS key_column
               WHERE key_column.object_id = i.object_id
                 AND key_column.index_id = i.index_id
                 AND key_column.key_ordinal > 0) <> expected.KeyCount
           OR (SELECT COUNT(*) FROM sys.index_columns AS include_column
               WHERE include_column.object_id = i.object_id
                 AND include_column.index_id = i.index_id
                 AND include_column.is_included_column = 1) <> expected.IncludeCount
    )
        THROW 52015, N'BMS budget projection refused: W-03 index inventory/state is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.BudgetPositions', N'PK_bms_web_BudgetPositions', 1, N'CostCenterId'),
                (N'bms_web.BudgetPositions', N'PK_bms_web_BudgetPositions', 2, N'CashFlowItemId'),
                (N'bms_web.BudgetPositions', N'PK_bms_web_BudgetPositions', 3, N'BudgetMonth'),
                (N'bms_web.BudgetRequestProjections', N'PK_bms_web_BudgetRequestProjections', 1, N'Id'),
                (N'bms_web.BudgetRequestProjections', N'UQ_bms_web_BudgetRequestProjections_Line', 1, N'ExpenseRequestLineId'),
                (N'bms_web.BudgetRequestProjections', N'IX_BudgetRequestProjections_ExpenseRequestId', 1, N'ExpenseRequestId'),
                (N'bms_web.BudgetRequestProjections', N'IX_BudgetRequestProjections_BudgetKey', 1, N'CostCenterId'),
                (N'bms_web.BudgetRequestProjections', N'IX_BudgetRequestProjections_BudgetKey', 2, N'CashFlowItemId'),
                (N'bms_web.BudgetRequestProjections', N'IX_BudgetRequestProjections_BudgetKey', 3, N'BudgetMonth')
        ) AS expected(ObjectName, IndexName, KeyOrdinal, ColumnName)
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM sys.indexes AS i
            INNER JOIN sys.index_columns AS ic
              ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            INNER JOIN sys.columns AS c
              ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(expected.ObjectName)
              AND i.name = expected.IndexName
              AND ic.key_ordinal = expected.KeyOrdinal
              AND ic.is_included_column = 0
              AND ic.is_descending_key = 0
              AND c.name = expected.ColumnName
        )
    )
       OR EXISTS
    (
        SELECT 1
        FROM (VALUES (N'ReservedAmountKzt'), (N'ExpenseRequestId')) AS expected(ColumnName)
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM sys.indexes AS i
            INNER JOIN sys.index_columns AS ic
              ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            INNER JOIN sys.columns AS c
              ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')
              AND i.name = N'IX_BudgetRequestProjections_BudgetKey'
              AND ic.is_included_column = 1
              AND c.name = expected.ColumnName
        )
    )
        THROW 52016, N'BMS budget projection refused: W-03 index key/include order is drifted.', 1;

    IF (SELECT COUNT(*) FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'bms_web.BudgetPositions')) <> 0
       OR (SELECT COUNT(*) FROM sys.foreign_keys
           WHERE parent_object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')) <> 3
       OR (SELECT COUNT(*) FROM sys.foreign_keys
           WHERE referenced_object_id = OBJECT_ID(N'bms_web.BudgetPositions')) <> 1
       OR (SELECT COUNT(*) FROM sys.foreign_keys
           WHERE referenced_object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')) <> 0
       OR EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'FK_bms_web_BudgetRequestProjections_Line', N'bms_web.ExpenseRequestLines', 1, N'ExpenseRequestLineId', N'Id', 1),
                (N'FK_bms_web_BudgetRequestProjections_Request', N'bms_web.ExpenseRequests', 1, N'ExpenseRequestId', N'Id', 1),
                (N'FK_bms_web_BudgetRequestProjections_Position', N'bms_web.BudgetPositions', 1, N'CostCenterId', N'CostCenterId', 3),
                (N'FK_bms_web_BudgetRequestProjections_Position', N'bms_web.BudgetPositions', 2, N'CashFlowItemId', N'CashFlowItemId', 3),
                (N'FK_bms_web_BudgetRequestProjections_Position', N'bms_web.BudgetPositions', 3, N'BudgetMonth', N'BudgetMonth', 3)
        ) AS expected(ForeignKeyName, ReferencedObjectName, ColumnOrdinal, ParentColumnName, ReferencedColumnName, ColumnCount)
        WHERE NOT EXISTS
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
            WHERE fk.parent_object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')
              AND fk.name = expected.ForeignKeyName
              AND fk.referenced_object_id = OBJECT_ID(expected.ReferencedObjectName)
              AND fkc.constraint_column_id = expected.ColumnOrdinal
              AND parent_column.name = expected.ParentColumnName
              AND referenced_column.name = expected.ReferencedColumnName
              AND fk.delete_referential_action = 0
              AND fk.update_referential_action = 0
              AND fk.is_disabled = 0 AND fk.is_not_trusted = 0
              AND fk.is_not_for_replication = 0 AND fk.is_system_named = 0
              AND (SELECT COUNT(*) FROM sys.foreign_key_columns AS exact_column
                   WHERE exact_column.constraint_object_id = fk.object_id) = expected.ColumnCount
        )
    )
        THROW 52017, N'BMS budget projection refused: W-03 exact foreign-key mapping is drifted.', 1;

    IF (SELECT COUNT(*) FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.BudgetPositions')) <> 4
       OR (SELECT COUNT(*) FROM sys.check_constraints
           WHERE parent_object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')) <> 2
       OR EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.BudgetPositions', N'CK_bms_web_BudgetPositions_month_start', N'datepartday,budgetmonth=1', 3),
                (N'bms_web.BudgetPositions', N'CK_bms_web_BudgetPositions_limit', N'limitamountkztisnullorlimitamountkzt>=0', 4),
                (N'bms_web.BudgetPositions', N'CK_bms_web_BudgetPositions_reserved', N'reservedamountkzt>=0', 5),
                (N'bms_web.BudgetPositions', N'CK_bms_web_BudgetPositions_paid', N'paidamountkzt>=0', 6),
                (N'bms_web.BudgetRequestProjections', N'CK_bms_web_BudgetRequestProjections_month_start', N'datepartday,budgetmonth=1', 6),
                (N'bms_web.BudgetRequestProjections', N'CK_bms_web_BudgetRequestProjections_reserved', N'reservedamountkzt>0', 7)
        ) AS expected(ObjectName, ConstraintName, NormalizedDefinition, ParentColumnId)
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
            WHERE cc.parent_object_id = OBJECT_ID(expected.ObjectName)
              AND cc.name = expected.ConstraintName
              AND cc.is_disabled = 0 AND cc.is_not_trusted = 0
              AND cc.is_not_for_replication = 0 AND cc.is_system_named = 0
               AND cc.parent_column_id = expected.ParentColumnId
              AND actual.NormalizedDefinition COLLATE Latin1_General_100_BIN2 =
                  expected.NormalizedDefinition COLLATE Latin1_General_100_BIN2
        )
    )
        THROW 52018, N'BMS budget projection refused: W-03 exact CHECK contract is drifted.', 1;

    IF (SELECT COUNT(*) FROM sys.default_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.BudgetPositions')) <> 2
       OR (SELECT COUNT(*) FROM sys.default_constraints
           WHERE parent_object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')) <> 0
       OR EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'DF_bms_web_BudgetPositions_Reserved', N'ReservedAmountKzt'),
                (N'DF_bms_web_BudgetPositions_Paid', N'PaidAmountKzt')
        ) AS expected(ConstraintName, ColumnName)
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM sys.default_constraints AS dc
            INNER JOIN sys.columns AS c
              ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
            CROSS APPLY
            (
                VALUES
                (
                    LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        dc.definition, N'[', N''), N']', N''), N'(', N''), N')', N''),
                        N' ', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N''))
                )
            ) AS actual(NormalizedDefinition)
            WHERE dc.parent_object_id = OBJECT_ID(N'bms_web.BudgetPositions')
              AND dc.name = expected.ConstraintName
              AND c.name = expected.ColumnName
              AND dc.is_system_named = 0
              AND actual.NormalizedDefinition COLLATE Latin1_General_100_BIN2 =
                  N'0' COLLATE Latin1_General_100_BIN2
        )
    )
        THROW 52019, N'BMS budget projection refused: W-03 exact DEFAULT contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.triggers AS tr
        WHERE tr.parent_id IN
        (
            OBJECT_ID(N'bms_web.BudgetPositions'),
            OBJECT_ID(N'bms_web.BudgetRequestProjections')
        )
          AND tr.is_ms_shipped = 0
    )
        THROW 52020, N'BMS budget projection refused: W-03 user trigger is not allowed.', 1;

    IF NOT EXISTS (SELECT 1 FROM bms_web.schema_version WHERE version_no = 2)
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (2, N'002_budget_projection.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
