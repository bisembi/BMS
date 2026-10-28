-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web W62 — calculated KZT components for expense-request lines.

    IMPORTANT
    - Apply 001 through 017 first and run this script standalone.
    - The only business-row DML is a deterministic KZT legacy backfill:
      existing rows are accepted only when their request currency is KZT, then
      the already stored source amount/VAT components are copied to KZT fields.
    - Foreign-currency values are calculated by the application writer; this
      migration does not invent or import currency rates.
    - It has not been executed against an accounting server.
*/

IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS W62 refused: run 018_expense_request_line_currency_conversion.sql outside an ambient transaction.', 16, 1);
    RETURN;
END;

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 59901, N'BMS W62 refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 59902, N'BMS W62 refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
    THROW 59903, N'BMS W62 refused: managed schema bms_web is absent.', 1;

IF OBJECT_ID(N'bms_web.ExpenseRequests', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.ExpenseRequestLines', N'U') IS NULL
    THROW 59904, N'BMS W62 refused: expense-request tables are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no > 18
    )
        THROW 59905, N'BMS W62 refused: a future schema version is already registered.', 1;

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
                (17, N'017_expense_request_currency_rate_snapshot.sql')
        ) AS expected(version_no, script_name)
        INNER JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK)
          ON actual.version_no = expected.version_no
         AND actual.script_name COLLATE Latin1_General_100_BIN2 =
             expected.script_name COLLATE Latin1_General_100_BIN2
         AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
    ) <> 17
        THROW 59906, N'BMS W62 refused: exact schema versions 1 through 17 are required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 18
          AND
          (
              script_name COLLATE Latin1_General_100_BIN2 <>
                  N'018_expense_request_line_currency_conversion.sql'
              OR DATALENGTH(script_name) <>
                  DATALENGTH(N'018_expense_request_line_currency_conversion.sql')
          )
    )
        THROW 59907, N'BMS W62 refused: schema version 18 belongs to another script.', 1;

    DECLARE @RequestLock uniqueidentifier;
    SELECT TOP (1) @RequestLock = Id
    FROM bms_web.ExpenseRequests WITH (TABLOCKX, HOLDLOCK)
    ORDER BY Id;

    DECLARE @LineLock uniqueidentifier;
    SELECT TOP (1) @LineLock = Id
    FROM bms_web.ExpenseRequestLines WITH (TABLOCKX, HOLDLOCK)
    ORDER BY Id;

    DECLARE @NewColumnCount int =
    (
        SELECT COUNT(*)
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
          AND name IN (N'VatAmountKzt', N'AmountExcludingVatKzt')
    );

    IF @NewColumnCount NOT IN (0, 2)
        THROW 59908, N'BMS W62 refused: KZT component columns are partially present.', 1;

    DECLARE @TargetAmountKzt bit = CASE WHEN EXISTS
    (
        SELECT 1
        FROM sys.columns AS c
        WHERE c.object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
          AND c.name = N'AmountKzt'
          AND TYPE_NAME(c.user_type_id) = N'decimal'
          AND c.max_length = 9 AND c.precision = 18 AND c.scale = 2
          AND c.is_nullable = 1
          AND c.collation_name IS NULL
          AND c.is_computed = 0 AND c.is_identity = 0
          AND c.is_rowguidcol = 0 AND c.is_filestream = 0
          AND c.default_object_id = 0 AND c.rule_object_id = 0
          AND c.generated_always_type = 0
          AND c.is_sparse = 0 AND c.is_column_set = 0
          AND c.encryption_type IS NULL AND c.is_hidden = 0
          AND c.is_masked = 0
    ) THEN 1 ELSE 0 END;

    DECLARE @LegacyAmountKzt bit = CASE WHEN EXISTS
    (
        SELECT 1
        FROM sys.columns AS c
        INNER JOIN sys.default_constraints AS dc
          ON dc.object_id = c.default_object_id
        WHERE c.object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
          AND c.name = N'AmountKzt'
          AND TYPE_NAME(c.user_type_id) = N'decimal'
          AND c.max_length = 9 AND c.precision = 18 AND c.scale = 2
          AND c.is_nullable = 0
          AND c.collation_name IS NULL
          AND c.is_computed = 0 AND c.is_identity = 0
          AND c.is_rowguidcol = 0 AND c.is_filestream = 0
          AND c.rule_object_id = 0 AND c.generated_always_type = 0
          AND c.is_sparse = 0 AND c.is_column_set = 0
          AND c.encryption_type IS NULL AND c.is_hidden = 0
          AND c.is_masked = 0
          AND dc.name = N'DF_bms_web_ExpenseRequestLines_AmountKzt'
          AND REPLACE(REPLACE(REPLACE(dc.definition, N'(', N''), N')', N''), N' ', N'')
              COLLATE Latin1_General_100_BIN2 = N'0'
    ) THEN 1 ELSE 0 END;

    IF NOT
    (
        (@NewColumnCount = 0 AND @LegacyAmountKzt = 1 AND @TargetAmountKzt = 0)
        OR (@NewColumnCount = 2 AND @LegacyAmountKzt = 0 AND @TargetAmountKzt = 1)
    )
        THROW 59909, N'BMS W62 refused: AmountKzt storage is partial or drifted.', 1;

    DECLARE @LegacyCalculatedCheck nvarchar(max) =
        N'AmountKzt=AmountANDVatRateCodeISNULLANDVatAmountISNULLANDAmountExcludingVatISNULLORVatRateCode=N''WithoutVat''ANDVatAmount=0ANDAmountExcludingVat=AmountORVatRateCode=N''Vat16''ANDVatAmount=roundAmount*16/116,2ANDAmountExcludingVat=Amount-VatAmount';
    DECLARE @TargetCalculatedCheck nvarchar(max) =
        N'casewhenVatRateCodeISNULLANDVatAmountISNULLANDAmountExcludingVatISNULLORVatRateCode=N''WithoutVat''ANDVatAmount=0ANDAmountExcludingVat=AmountORVatRateCode=N''Vat16''ANDVatAmount=roundAmount*16/116,2ANDAmountExcludingVat=Amount-VatAmountANDAmountKztISNULLANDVatAmountKztISNULLANDAmountExcludingVatKztISNULLORAmountKztISNOTNULLANDAmountKzt>=0ANDVatRateCodeISNULLANDVatAmountKztISNULLANDAmountExcludingVatKztISNULLORVatRateCode=N''WithoutVat''ANDVatAmountKzt=0ANDAmountExcludingVatKzt=AmountKztORVatRateCode=N''Vat16''ANDVatAmountKzt>=0ANDAmountExcludingVatKzt>=0ANDVatAmountKzt+AmountExcludingVatKzt=AmountKztthen1else0end=1';

    DECLARE @ActualCalculatedCheck nvarchar(max) =
    (
        SELECT REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
            cc.definition, N'[', N''), N']', N''), N'(', N''), N')', N''),
            N' ', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'')
        FROM sys.check_constraints AS cc
        WHERE cc.parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
          AND cc.name = N'CK_ExpenseRequestLines_CalculatedAmounts'
          AND cc.is_disabled = 0 AND cc.is_not_trusted = 0
          AND cc.is_not_for_replication = 0 AND cc.is_system_named = 0
          AND cc.parent_column_id = 0
    );

    IF @ActualCalculatedCheck IS NULL
       OR
       (
           @NewColumnCount = 0
           AND @ActualCalculatedCheck COLLATE Latin1_General_100_BIN2 <>
               @LegacyCalculatedCheck COLLATE Latin1_General_100_BIN2
       )
       OR
       (
           @NewColumnCount = 2
           AND @ActualCalculatedCheck COLLATE Latin1_General_100_BIN2 <>
               @TargetCalculatedCheck COLLATE Latin1_General_100_BIN2
       )
        THROW 59910, N'BMS W62 refused: calculated-amount CHECK is missing or drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.triggers
        WHERE parent_id IN
        (
            OBJECT_ID(N'bms_web.ExpenseRequests'),
            OBJECT_ID(N'bms_web.ExpenseRequestLines')
        )
          AND is_ms_shipped = 0
    )
        THROW 59911, N'BMS W62 refused: expense-request tables have user DML triggers.', 1;

    IF @NewColumnCount = 0
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM bms_web.ExpenseRequestLines AS line
            INNER JOIN bms_web.ExpenseRequests AS request
              ON request.Id = line.ExpenseRequestId
            WHERE request.CurrencyCode COLLATE Latin1_General_100_BIN2 <> N'KZT'
        )
            THROW 59912, N'BMS W62 refused: a legacy foreign-currency line cannot be backfilled without a source rate.', 1;

        ALTER TABLE bms_web.ExpenseRequestLines DROP CONSTRAINT
            CK_ExpenseRequestLines_CalculatedAmounts,
            DF_bms_web_ExpenseRequestLines_AmountKzt;

        ALTER TABLE bms_web.ExpenseRequestLines
            ALTER COLUMN AmountKzt decimal(18,2) NULL;

        ALTER TABLE bms_web.ExpenseRequestLines ADD
            VatAmountKzt decimal(18,2) NULL,
            AmountExcludingVatKzt decimal(18,2) NULL;

        EXEC(N'UPDATE line WITH (TABLOCKX)
        SET VatAmountKzt = VatAmount,
            AmountExcludingVatKzt = AmountExcludingVat
        FROM bms_web.ExpenseRequestLines AS line;');

        EXEC(N'ALTER TABLE bms_web.ExpenseRequestLines WITH CHECK ADD
            CONSTRAINT CK_ExpenseRequestLines_CalculatedAmounts CHECK
            (
                CASE WHEN
                (
                    (
                        (VatRateCode IS NULL AND VatAmount IS NULL
                            AND AmountExcludingVat IS NULL)
                        OR (VatRateCode = N''WithoutVat'' AND VatAmount = 0
                            AND AmountExcludingVat = Amount)
                        OR (VatRateCode = N''Vat16''
                            AND VatAmount = ROUND(Amount * 16 / 116, 2)
                            AND AmountExcludingVat = Amount - VatAmount)
                    )
                    AND
                    (
                        (AmountKzt IS NULL AND VatAmountKzt IS NULL
                            AND AmountExcludingVatKzt IS NULL)
                        OR
                        (
                            AmountKzt IS NOT NULL AND AmountKzt >= 0
                            AND
                            (
                                (VatRateCode IS NULL AND VatAmountKzt IS NULL
                                    AND AmountExcludingVatKzt IS NULL)
                                OR (VatRateCode = N''WithoutVat'' AND VatAmountKzt = 0
                                    AND AmountExcludingVatKzt = AmountKzt)
                                OR (VatRateCode = N''Vat16'' AND VatAmountKzt >= 0
                                    AND AmountExcludingVatKzt >= 0
                                    AND VatAmountKzt + AmountExcludingVatKzt = AmountKzt)
                            )
                        )
                    )
                )
                THEN 1 ELSE 0 END = 1
            );
        ALTER TABLE bms_web.ExpenseRequestLines
            CHECK CONSTRAINT CK_ExpenseRequestLines_CalculatedAmounts;');
    END;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'AmountKzt'),
                (N'VatAmountKzt'),
                (N'AmountExcludingVatKzt')
        ) AS expected(ColumnName)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
         AND c.name = expected.ColumnName
        WHERE c.object_id IS NULL
           OR TYPE_NAME(c.user_type_id) <> N'decimal'
           OR c.max_length <> 9 OR c.precision <> 18 OR c.scale <> 2
           OR c.is_nullable <> 1 OR c.collation_name IS NOT NULL
           OR c.is_computed <> 0 OR c.is_identity <> 0
           OR c.is_rowguidcol <> 0 OR c.is_filestream <> 0
           OR c.default_object_id <> 0 OR c.rule_object_id <> 0
           OR c.generated_always_type <> 0
           OR c.is_sparse <> 0 OR c.is_column_set <> 0
           OR c.encryption_type IS NOT NULL OR c.is_hidden <> 0
           OR c.is_masked <> 0
    )
        THROW 59913, N'BMS W62 refused: final KZT component column contract is drifted.', 1;

    SET @ActualCalculatedCheck =
    (
        SELECT REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
            cc.definition, N'[', N''), N']', N''), N'(', N''), N')', N''),
            N' ', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'')
        FROM sys.check_constraints AS cc
        WHERE cc.parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
          AND cc.name = N'CK_ExpenseRequestLines_CalculatedAmounts'
          AND cc.is_disabled = 0 AND cc.is_not_trusted = 0
          AND cc.is_not_for_replication = 0 AND cc.is_system_named = 0
          AND cc.parent_column_id = 0
    );

    IF @ActualCalculatedCheck IS NULL
       OR @ActualCalculatedCheck COLLATE Latin1_General_100_BIN2 <>
          @TargetCalculatedCheck COLLATE Latin1_General_100_BIN2
       OR
       (
           SELECT COUNT(*)
           FROM sys.check_constraints
           WHERE parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
             AND
             (
                 definition COLLATE Latin1_General_100_CI_AS LIKE N'%AmountKzt%'
                 OR definition COLLATE Latin1_General_100_CI_AS LIKE N'%VatAmountKzt%'
                 OR definition COLLATE Latin1_General_100_CI_AS LIKE N'%AmountExcludingVatKzt%'
             )
       ) <> 1
        THROW 59914, N'BMS W62 refused: final calculated-amount CHECK contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.index_columns AS ic
        INNER JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE ic.object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
          AND c.name IN (N'AmountKzt', N'VatAmountKzt', N'AmountExcludingVatKzt')
    )
       OR EXISTS
       (
           SELECT 1
           FROM sys.indexes AS i
           WHERE i.object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
             AND i.filter_definition IS NOT NULL
             AND
             (
                 i.filter_definition COLLATE Latin1_General_100_CI_AS LIKE N'%AmountKzt%'
                 OR i.filter_definition COLLATE Latin1_General_100_CI_AS LIKE N'%VatAmountKzt%'
                 OR i.filter_definition COLLATE Latin1_General_100_CI_AS LIKE N'%AmountExcludingVatKzt%'
             )
       )
       OR EXISTS
       (
           SELECT 1
           FROM sys.stats_columns AS sc
           INNER JOIN sys.columns AS c
             ON c.object_id = sc.object_id AND c.column_id = sc.column_id
           WHERE sc.object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
             AND c.name IN (N'AmountKzt', N'VatAmountKzt', N'AmountExcludingVatKzt')
       )
        THROW 59915, N'BMS W62 refused: KZT component columns have unsupported index or statistics semantics.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.foreign_key_columns AS fkc
        WHERE
        (
            fkc.parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
            AND fkc.parent_column_id IN
            (
                COLUMNPROPERTY(OBJECT_ID(N'bms_web.ExpenseRequestLines'), N'AmountKzt', 'ColumnId'),
                COLUMNPROPERTY(OBJECT_ID(N'bms_web.ExpenseRequestLines'), N'VatAmountKzt', 'ColumnId'),
                COLUMNPROPERTY(OBJECT_ID(N'bms_web.ExpenseRequestLines'), N'AmountExcludingVatKzt', 'ColumnId')
            )
        )
        OR
        (
            fkc.referenced_object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
            AND fkc.referenced_column_id IN
            (
                COLUMNPROPERTY(OBJECT_ID(N'bms_web.ExpenseRequestLines'), N'AmountKzt', 'ColumnId'),
                COLUMNPROPERTY(OBJECT_ID(N'bms_web.ExpenseRequestLines'), N'VatAmountKzt', 'ColumnId'),
                COLUMNPROPERTY(OBJECT_ID(N'bms_web.ExpenseRequestLines'), N'AmountExcludingVatKzt', 'ColumnId')
            )
        )
    )
        THROW 59916, N'BMS W62 refused: KZT component columns must have no foreign key.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 18
    )
       AND (@NewColumnCount <> 2 OR @TargetAmountKzt <> 1)
        THROW 59917, N'BMS W62 refused: version 18 is registered without its physical contract.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 18
    )
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (18, N'018_expense_request_line_currency_conversion.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
