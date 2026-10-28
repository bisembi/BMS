-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web W61 — structure-only currency-rate snapshot fields on expense requests.

    IMPORTANT
    - Apply 001 through 016 first and run this script standalone.
    - Existing and new drafts remain NULL/NULL/NULL until a separately approved
      atomic currency snapshot writer exists.
    - This script performs no business-row DML, backfill, import or seed.
    - The three columns are server-owned and are not a freshness or submit claim.
    - It has not been executed against an accounting server.
*/

IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS W61 refused: run 017_expense_request_currency_rate_snapshot.sql outside an ambient transaction.', 16, 1);
    RETURN;
END;

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 59801, N'BMS W61 refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 59802, N'BMS W61 refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
    THROW 59803, N'BMS W61 refused: managed schema bms_web is absent.', 1;

IF OBJECT_ID(N'bms_web.ExpenseRequests', N'U') IS NULL
    THROW 59804, N'BMS W61 refused: bms_web.ExpenseRequests is absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no > 17
    )
        THROW 59805, N'BMS W61 refused: a future schema version is already registered.', 1;

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
                (16, N'016_currencies_and_daily_rates.sql')
        ) AS expected(version_no, script_name)
        INNER JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK)
          ON actual.version_no = expected.version_no
         AND actual.script_name COLLATE Latin1_General_100_BIN2 =
             expected.script_name COLLATE Latin1_General_100_BIN2
         AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
    ) <> 16
        THROW 59806, N'BMS W61 refused: exact schema versions 1 through 16 are required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 17
          AND
          (
              script_name COLLATE Latin1_General_100_BIN2 <>
                  N'017_expense_request_currency_rate_snapshot.sql'
              OR DATALENGTH(script_name) <>
                  DATALENGTH(N'017_expense_request_currency_rate_snapshot.sql')
          )
    )
        THROW 59807, N'BMS W61 refused: schema version 17 belongs to another script.', 1;

    DECLARE @ExpenseRequestLockProbe uniqueidentifier;
    SELECT TOP (1) @ExpenseRequestLockProbe = Id
    FROM bms_web.ExpenseRequests WITH (TABLOCKX, HOLDLOCK)
    ORDER BY Id;

    DECLARE @RateDateColumnExists int = CASE WHEN COL_LENGTH(
        N'bms_web.ExpenseRequests', N'CurrencyRateDate') IS NULL THEN 0 ELSE 1 END;
    DECLARE @RateColumnExists int = CASE WHEN COL_LENGTH(
        N'bms_web.ExpenseRequests', N'CurrencyRate') IS NULL THEN 0 ELSE 1 END;
    DECLARE @MultiplicityColumnExists int = CASE WHEN COL_LENGTH(
        N'bms_web.ExpenseRequests', N'CurrencyMultiplicity') IS NULL THEN 0 ELSE 1 END;

    IF @RateDateColumnExists + @RateColumnExists + @MultiplicityColumnExists NOT IN (0, 3)
        THROW 59808, N'BMS W61 refused: currency-rate snapshot columns are partially present.', 1;

    IF @RateDateColumnExists = 0
       AND EXISTS
       (
           SELECT 1
           FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
           WHERE version_no = 17
       )
        THROW 59809, N'BMS W61 refused: version 17 is registered without its physical contract.', 1;

    IF @RateDateColumnExists = 0
    BEGIN
        ALTER TABLE bms_web.ExpenseRequests ADD
            CurrencyRateDate date NULL,
            CurrencyRate decimal(18,6) NULL,
            CurrencyMultiplicity decimal(10,0) NULL;

        EXEC(N'ALTER TABLE bms_web.ExpenseRequests WITH CHECK ADD
            CONSTRAINT CK_bms_web_ExpenseRequests_CurrencyRateSnapshot CHECK
            (
                CASE
                    WHEN CurrencyRateDate IS NULL
                         AND CurrencyRate IS NULL
                         AND CurrencyMultiplicity IS NULL THEN 1
                    WHEN CurrencyCode = N''KZT''
                         AND CurrencyRateDate IS NULL
                         AND CurrencyRate = 1
                         AND CurrencyMultiplicity = 1 THEN 1
                    WHEN CurrencyCode <> N''KZT''
                         AND CurrencyRateDate IS NOT NULL
                         AND CurrencyRate > 0
                         AND CurrencyMultiplicity > 0 THEN 1
                    ELSE 0
                END = 1
            );
        ALTER TABLE bms_web.ExpenseRequests
            CHECK CONSTRAINT CK_bms_web_ExpenseRequests_CurrencyRateSnapshot;');
    END;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'CurrencyRateDate', N'date', 3, 1, 10, 0),
                (N'CurrencyRate', N'decimal', 9, 1, 18, 6),
                (N'CurrencyMultiplicity', N'decimal', 9, 1, 10, 0)
        ) AS expected(ColumnName, TypeName, MaxLength, IsNullable, PrecisionValue, ScaleValue)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
         AND c.name = expected.ColumnName
        WHERE c.object_id IS NULL
           OR TYPE_NAME(c.user_type_id) <> expected.TypeName
           OR c.max_length <> expected.MaxLength
           OR c.is_nullable <> expected.IsNullable
           OR c.precision <> expected.PrecisionValue
           OR c.scale <> expected.ScaleValue
           OR c.collation_name IS NOT NULL
           OR c.is_computed <> 0 OR c.is_identity <> 0
           OR c.is_rowguidcol <> 0 OR c.is_filestream <> 0
           OR c.default_object_id <> 0 OR c.rule_object_id <> 0
           OR c.generated_always_type <> 0
           OR c.is_sparse <> 0 OR c.is_column_set <> 0
           OR c.encryption_type IS NOT NULL OR c.is_hidden <> 0
           OR c.is_masked <> 0
    )
        THROW 59810, N'BMS W61 refused: currency-rate snapshot column contract is drifted.', 1;

    DECLARE @ExpectedCurrencyRateSnapshotCheck nvarchar(max) =
        N'casewhenCurrencyRateDateISNULLANDCurrencyRateISNULLANDCurrencyMultiplicityISNULLthen1whenCurrencyCode=N''KZT''ANDCurrencyRateDateISNULLANDCurrencyRate=1ANDCurrencyMultiplicity=1then1whenCurrencyCode<>N''KZT''ANDCurrencyRateDateISNOTNULLANDCurrencyRate>0ANDCurrencyMultiplicity>0then1else0end=1';

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints AS cc
        CROSS APPLY
        (
            VALUES
            (
                REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                    cc.definition, N'[', N''), N']', N''), N'(', N''), N')', N''),
                    N' ', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'')
            )
        ) AS normalized(Definition)
        WHERE cc.parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
          AND cc.name = N'CK_bms_web_ExpenseRequests_CurrencyRateSnapshot'
          AND cc.is_disabled = 0
          AND cc.is_not_trusted = 0
          AND cc.is_not_for_replication = 0
          AND cc.is_system_named = 0
          AND cc.parent_column_id = 0
          AND normalized.Definition COLLATE Latin1_General_100_BIN2 =
              @ExpectedCurrencyRateSnapshotCheck COLLATE Latin1_General_100_BIN2
    )
       OR
       (
           SELECT COUNT(*)
           FROM sys.check_constraints
           WHERE parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
             AND
             (
                 definition COLLATE Latin1_General_100_CI_AS LIKE N'%CurrencyRateDate%'
                 OR definition COLLATE Latin1_General_100_CI_AS LIKE N'%CurrencyRate%'
                 OR definition COLLATE Latin1_General_100_CI_AS LIKE N'%CurrencyMultiplicity%'
             )
       ) <> 1
        THROW 59811, N'BMS W61 refused: currency-rate snapshot CHECK contract is missing or drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.index_columns AS ic
        INNER JOIN sys.columns AS c
          ON c.object_id = ic.object_id
         AND c.column_id = ic.column_id
        WHERE ic.object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
          AND c.name IN (N'CurrencyRateDate', N'CurrencyRate', N'CurrencyMultiplicity')
    )
       OR EXISTS
       (
           SELECT 1
           FROM sys.indexes AS i
           WHERE i.object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
             AND
             (
                 i.filter_definition COLLATE Latin1_General_100_CI_AS LIKE N'%CurrencyRateDate%'
                 OR i.filter_definition COLLATE Latin1_General_100_CI_AS LIKE N'%CurrencyRate%'
                 OR i.filter_definition COLLATE Latin1_General_100_CI_AS LIKE N'%CurrencyMultiplicity%'
             )
       )
        THROW 59812, N'BMS W61 refused: currency-rate snapshot columns must have no index semantics.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.foreign_key_columns AS fkc
        WHERE
        (
            fkc.parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
            AND fkc.parent_column_id IN
            (
                COLUMNPROPERTY(OBJECT_ID(N'bms_web.ExpenseRequests'), N'CurrencyRateDate', 'ColumnId'),
                COLUMNPROPERTY(OBJECT_ID(N'bms_web.ExpenseRequests'), N'CurrencyRate', 'ColumnId'),
                COLUMNPROPERTY(OBJECT_ID(N'bms_web.ExpenseRequests'), N'CurrencyMultiplicity', 'ColumnId')
            )
        )
        OR
        (
            fkc.referenced_object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
            AND fkc.referenced_column_id IN
            (
                COLUMNPROPERTY(OBJECT_ID(N'bms_web.ExpenseRequests'), N'CurrencyRateDate', 'ColumnId'),
                COLUMNPROPERTY(OBJECT_ID(N'bms_web.ExpenseRequests'), N'CurrencyRate', 'ColumnId'),
                COLUMNPROPERTY(OBJECT_ID(N'bms_web.ExpenseRequests'), N'CurrencyMultiplicity', 'ColumnId')
            )
        )
    )
        THROW 59813, N'BMS W61 refused: currency-rate snapshot columns must have no foreign key.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.triggers
        WHERE parent_id = OBJECT_ID(N'bms_web.ExpenseRequests')
          AND is_ms_shipped = 0
    )
        THROW 59814, N'BMS W61 refused: ExpenseRequests has a user DML trigger.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 17
    )
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (17, N'017_expense_request_currency_rate_snapshot.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
