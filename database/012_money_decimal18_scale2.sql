-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web W26 — canonical decimal(18,2) money storage.

    IMPORTANT
    - Apply 001 through 011 first and run this script standalone.
    - Existing values are never rounded, truncated, backfilled or normalized.
    - The script refuses non-representable data and unknown schema dependencies.
    - The only business-table operation is guarded ALTER COLUMN metadata DDL.
    - It has not been executed against an accounting server.
*/

IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS W26 refused: run 012_money_decimal18_scale2.sql outside an ambient transaction.', 16, 1);
    RETURN;
END;

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 59301, N'BMS W26 refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 59302, N'BMS W26 refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
    THROW 59303, N'BMS W26 refused: managed schema bms_web is absent.', 1;

IF OBJECT_ID(N'bms_web.ExpenseRequestLines', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.BudgetPositions', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.BudgetRequestProjections', N'U') IS NULL
    THROW 59304, N'BMS W26 refused: required money-bearing tables are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no > 12
    )
        THROW 59305, N'BMS W26 refused: a future schema version is already registered.', 1;

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
                (11, N'011_expense_request_line_accrual_snapshot.sql')
        ) AS expected(version_no, script_name)
        INNER JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK)
          ON actual.version_no = expected.version_no
         AND actual.script_name COLLATE Latin1_General_100_BIN2 =
             expected.script_name COLLATE Latin1_General_100_BIN2
         AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
    ) <> 11
        THROW 59306, N'BMS W26 refused: exact schema versions 1 through 11 are required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 12
          AND
          (
              script_name COLLATE Latin1_General_100_BIN2 <>
                  N'012_money_decimal18_scale2.sql'
              OR DATALENGTH(script_name) <>
                  DATALENGTH(N'012_money_decimal18_scale2.sql')
          )
    )
        THROW 59307, N'BMS W26 refused: schema version 12 belongs to another script.', 1;

    /* Match the application write order while the deployment window is drained. */
    DECLARE @LineLock uniqueidentifier;
    SELECT TOP (1) @LineLock = Id
    FROM bms_web.ExpenseRequestLines WITH (TABLOCKX, HOLDLOCK)
    ORDER BY Id;

    DECLARE @ProjectionLock uniqueidentifier;
    SELECT TOP (1) @ProjectionLock = Id
    FROM bms_web.BudgetRequestProjections WITH (TABLOCKX, HOLDLOCK)
    ORDER BY Id;

    DECLARE @PositionLock uniqueidentifier;
    SELECT TOP (1) @PositionLock = CostCenterId
    FROM bms_web.BudgetPositions WITH (TABLOCKX, HOLDLOCK)
    ORDER BY CostCenterId, CashFlowItemId, BudgetMonth;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.ExpenseRequestLines', N'Amount', 0),
                (N'bms_web.ExpenseRequestLines', N'VatAmount', 1),
                (N'bms_web.ExpenseRequestLines', N'AmountExcludingVat', 1),
                (N'bms_web.ExpenseRequestLines', N'AmountKzt', 0),
                (N'bms_web.BudgetPositions', N'LimitAmountKzt', 1),
                (N'bms_web.BudgetPositions', N'ReservedAmountKzt', 0),
                (N'bms_web.BudgetPositions', N'PaidAmountKzt', 0),
                (N'bms_web.BudgetRequestProjections', N'ReservedAmountKzt', 0)
        ) AS expected(ObjectName, ColumnName, IsNullable)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(expected.ObjectName)
         AND c.name = expected.ColumnName
        WHERE c.object_id IS NULL
           OR TYPE_NAME(c.user_type_id) <> N'decimal'
           OR c.is_nullable <> expected.IsNullable
           OR c.is_computed <> 0
           OR c.is_identity <> 0
           OR c.is_rowguidcol <> 0
           OR c.is_filestream <> 0
           OR c.rule_object_id <> 0
           OR c.generated_always_type <> 0
           OR c.is_sparse <> 0
           OR c.is_column_set <> 0
           OR c.encryption_type IS NOT NULL
           OR c.is_hidden <> 0
           OR c.is_masked <> 0
    )
        THROW 59308, N'BMS W26 refused: money column metadata is missing or drifted.', 1;

    DECLARE @LegacyColumnCount int =
    (
        SELECT COUNT(*)
        FROM
        (
            VALUES
                (N'bms_web.ExpenseRequestLines', N'Amount'),
                (N'bms_web.ExpenseRequestLines', N'VatAmount'),
                (N'bms_web.ExpenseRequestLines', N'AmountExcludingVat'),
                (N'bms_web.ExpenseRequestLines', N'AmountKzt'),
                (N'bms_web.BudgetPositions', N'LimitAmountKzt'),
                (N'bms_web.BudgetPositions', N'ReservedAmountKzt'),
                (N'bms_web.BudgetPositions', N'PaidAmountKzt'),
                (N'bms_web.BudgetRequestProjections', N'ReservedAmountKzt')
        ) AS expected(ObjectName, ColumnName)
        INNER JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(expected.ObjectName)
         AND c.name = expected.ColumnName
        WHERE c.[precision] = 19 AND c.scale = 4
    );

    DECLARE @TargetColumnCount int =
    (
        SELECT COUNT(*)
        FROM
        (
            VALUES
                (N'bms_web.ExpenseRequestLines', N'Amount'),
                (N'bms_web.ExpenseRequestLines', N'VatAmount'),
                (N'bms_web.ExpenseRequestLines', N'AmountExcludingVat'),
                (N'bms_web.ExpenseRequestLines', N'AmountKzt'),
                (N'bms_web.BudgetPositions', N'LimitAmountKzt'),
                (N'bms_web.BudgetPositions', N'ReservedAmountKzt'),
                (N'bms_web.BudgetPositions', N'PaidAmountKzt'),
                (N'bms_web.BudgetRequestProjections', N'ReservedAmountKzt')
        ) AS expected(ObjectName, ColumnName)
        INNER JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(expected.ObjectName)
         AND c.name = expected.ColumnName
        WHERE c.[precision] = 18 AND c.scale = 2
    );

    IF NOT
    (
        (@LegacyColumnCount = 8 AND @TargetColumnCount = 0)
        OR (@LegacyColumnCount = 0 AND @TargetColumnCount = 8)
    )
        THROW 59309, N'BMS W26 refused: money precision is partial or unknown.', 1;

    IF EXISTS
    (
        SELECT 1 FROM bms_web.ExpenseRequestLines
        WHERE TRY_CONVERT(decimal(18,2), Amount) IS NULL
           OR Amount <> TRY_CONVERT(decimal(18,2), Amount)
           OR (VatAmount IS NOT NULL AND
               (TRY_CONVERT(decimal(18,2), VatAmount) IS NULL
                OR VatAmount <> TRY_CONVERT(decimal(18,2), VatAmount)))
           OR (AmountExcludingVat IS NOT NULL AND
               (TRY_CONVERT(decimal(18,2), AmountExcludingVat) IS NULL
                OR AmountExcludingVat <> TRY_CONVERT(decimal(18,2), AmountExcludingVat)))
           OR TRY_CONVERT(decimal(18,2), AmountKzt) IS NULL
           OR AmountKzt <> TRY_CONVERT(decimal(18,2), AmountKzt)
    )
        THROW 59310, N'BMS W26 refused: request-line money would require rounding or overflow.', 1;

    IF EXISTS
    (
        SELECT 1 FROM bms_web.BudgetPositions
        WHERE (LimitAmountKzt IS NOT NULL AND
               (TRY_CONVERT(decimal(18,2), LimitAmountKzt) IS NULL
                OR LimitAmountKzt <> TRY_CONVERT(decimal(18,2), LimitAmountKzt)))
           OR TRY_CONVERT(decimal(18,2), ReservedAmountKzt) IS NULL
           OR ReservedAmountKzt <> TRY_CONVERT(decimal(18,2), ReservedAmountKzt)
           OR TRY_CONVERT(decimal(18,2), PaidAmountKzt) IS NULL
           OR PaidAmountKzt <> TRY_CONVERT(decimal(18,2), PaidAmountKzt)
    )
        THROW 59311, N'BMS W26 refused: budget-position money would require rounding or overflow.', 1;

    IF EXISTS
    (
        SELECT 1 FROM bms_web.BudgetRequestProjections
        WHERE TRY_CONVERT(decimal(18,2), ReservedAmountKzt) IS NULL
           OR ReservedAmountKzt <> TRY_CONVERT(decimal(18,2), ReservedAmountKzt)
    )
        THROW 59312, N'BMS W26 refused: projection money would require rounding or overflow.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.check_constraints
        WHERE is_disabled = 0
          AND is_not_trusted = 0
          AND is_not_for_replication = 0
          AND is_system_named = 0
          AND
          (
              (parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
               AND name = N'CK_bms_web_ExpenseRequestLines_amount'
               AND parent_column_id = COLUMNPROPERTY(
                   OBJECT_ID(N'bms_web.ExpenseRequestLines'), N'Amount', N'ColumnId'))
              OR (parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
                  AND name = N'CK_ExpenseRequestLines_CalculatedAmounts'
                  AND parent_column_id = 0)
              OR
              (parent_object_id = OBJECT_ID(N'bms_web.BudgetPositions')
               AND name = N'CK_bms_web_BudgetPositions_limit'
               AND parent_column_id = COLUMNPROPERTY(
                   OBJECT_ID(N'bms_web.BudgetPositions'), N'LimitAmountKzt', N'ColumnId'))
              OR (parent_object_id = OBJECT_ID(N'bms_web.BudgetPositions')
                  AND name = N'CK_bms_web_BudgetPositions_reserved'
                  AND parent_column_id = COLUMNPROPERTY(
                      OBJECT_ID(N'bms_web.BudgetPositions'), N'ReservedAmountKzt', N'ColumnId'))
              OR (parent_object_id = OBJECT_ID(N'bms_web.BudgetPositions')
                  AND name = N'CK_bms_web_BudgetPositions_paid'
                  AND parent_column_id = COLUMNPROPERTY(
                      OBJECT_ID(N'bms_web.BudgetPositions'), N'PaidAmountKzt', N'ColumnId'))
              OR
              (parent_object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')
               AND name = N'CK_bms_web_BudgetRequestProjections_reserved'
               AND parent_column_id = COLUMNPROPERTY(
                   OBJECT_ID(N'bms_web.BudgetRequestProjections'), N'ReservedAmountKzt', N'ColumnId'))
          )
    ) <> 6
        THROW 59313, N'BMS W26 refused: exact trusted money CHECK constraints are required.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
          AND name = N'CK_bms_web_ExpenseRequestLines_amount'
          AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(definition,
              N'[', N''), N']', N''), N'(', N''), N')', N''), N' ', N'')
              COLLATE Latin1_General_100_BIN2 = N'Amount>0'
    )
       OR NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
          AND name = N'CK_ExpenseRequestLines_CalculatedAmounts'
          AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(definition,
              N'[', N''), N']', N''), N'(', N''), N')', N''), N' ', N''),
              CHAR(9), N''), CHAR(10), N''), CHAR(13), N'')
              COLLATE Latin1_General_100_BIN2 =
              N'AmountKzt=AmountANDVatRateCodeISNULLANDVatAmountISNULLANDAmountExcludingVatISNULLORVatRateCode=N''WithoutVat''ANDVatAmount=0ANDAmountExcludingVat=AmountORVatRateCode=N''Vat16''ANDVatAmount=roundAmount*16/116,2ANDAmountExcludingVat=Amount-VatAmount'
    )
        THROW 59314, N'BMS W26 refused: request-line money CHECK definitions are drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.BudgetPositions', N'CK_bms_web_BudgetPositions_limit', N'LimitAmountKztISNULLORLimitAmountKzt>=0'),
                (N'bms_web.BudgetPositions', N'CK_bms_web_BudgetPositions_reserved', N'ReservedAmountKzt>=0'),
                (N'bms_web.BudgetPositions', N'CK_bms_web_BudgetPositions_paid', N'PaidAmountKzt>=0'),
                (N'bms_web.BudgetRequestProjections', N'CK_bms_web_BudgetRequestProjections_reserved', N'ReservedAmountKzt>0')
        ) AS expected(ObjectName, ConstraintName, NormalizedDefinition)
        LEFT JOIN sys.check_constraints AS cc
          ON cc.parent_object_id = OBJECT_ID(expected.ObjectName)
         AND cc.name = expected.ConstraintName
        CROSS APPLY
        (
            VALUES
            (
                REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                    cc.definition, N'[', N''), N']', N''), N'(', N''), N')', N''),
                    N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N'')
            )
        ) AS normalized(Definition)
        WHERE cc.object_id IS NULL
           OR normalized.Definition COLLATE Latin1_General_100_BIN2 <>
              expected.NormalizedDefinition COLLATE Latin1_General_100_BIN2
    )
        THROW 59315, N'BMS W26 refused: budget money CHECK definitions are drifted.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.default_constraints AS dc
        WHERE
            (dc.parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
             AND dc.name = N'DF_bms_web_ExpenseRequestLines_AmountKzt'
             AND COL_NAME(dc.parent_object_id, dc.parent_column_id) = N'AmountKzt')
         OR (dc.parent_object_id = OBJECT_ID(N'bms_web.BudgetPositions')
             AND dc.name = N'DF_bms_web_BudgetPositions_Reserved'
             AND COL_NAME(dc.parent_object_id, dc.parent_column_id) = N'ReservedAmountKzt')
         OR (dc.parent_object_id = OBJECT_ID(N'bms_web.BudgetPositions')
             AND dc.name = N'DF_bms_web_BudgetPositions_Paid'
             AND COL_NAME(dc.parent_object_id, dc.parent_column_id) = N'PaidAmountKzt')
    ) <> 3
        THROW 59316, N'BMS W26 refused: exact money DEFAULT constraints are required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.default_constraints AS dc
        WHERE dc.object_id IN
        (
            OBJECT_ID(N'bms_web.DF_bms_web_ExpenseRequestLines_AmountKzt', N'D'),
            OBJECT_ID(N'bms_web.DF_bms_web_BudgetPositions_Reserved', N'D'),
            OBJECT_ID(N'bms_web.DF_bms_web_BudgetPositions_Paid', N'D')
        )
          AND REPLACE(REPLACE(REPLACE(dc.definition, N'(', N''), N')', N''), N' ', N'')
              COLLATE Latin1_General_100_BIN2 <> N'0'
    )
        THROW 59317, N'BMS W26 refused: money DEFAULT definitions are drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.ExpenseRequestLines', N'Amount', NULL),
                (N'bms_web.ExpenseRequestLines', N'VatAmount', NULL),
                (N'bms_web.ExpenseRequestLines', N'AmountExcludingVat', NULL),
                (N'bms_web.ExpenseRequestLines', N'AmountKzt', N'DF_bms_web_ExpenseRequestLines_AmountKzt'),
                (N'bms_web.BudgetPositions', N'LimitAmountKzt', NULL),
                (N'bms_web.BudgetPositions', N'ReservedAmountKzt', N'DF_bms_web_BudgetPositions_Reserved'),
                (N'bms_web.BudgetPositions', N'PaidAmountKzt', N'DF_bms_web_BudgetPositions_Paid'),
                (N'bms_web.BudgetRequestProjections', N'ReservedAmountKzt', NULL)
        ) AS expected(ObjectName, ColumnName, DefaultName)
        INNER JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(expected.ObjectName)
         AND c.name = expected.ColumnName
        LEFT JOIN sys.default_constraints AS dc
          ON dc.object_id = c.default_object_id
        WHERE (expected.DefaultName IS NULL AND c.default_object_id <> 0)
           OR (expected.DefaultName IS NOT NULL AND
               (dc.object_id IS NULL OR dc.name <> expected.DefaultName))
    )
        THROW 59324, N'BMS W26 refused: money column DEFAULT ownership is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints AS cc
        WHERE
        (
            (cc.parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
             AND cc.definition COLLATE Latin1_General_100_CI_AS LIKE N'%Amount%')
         OR (cc.parent_object_id = OBJECT_ID(N'bms_web.BudgetPositions')
             AND
             (cc.definition COLLATE Latin1_General_100_CI_AS LIKE N'%LimitAmountKzt%'
              OR cc.definition COLLATE Latin1_General_100_CI_AS LIKE N'%ReservedAmountKzt%'
              OR cc.definition COLLATE Latin1_General_100_CI_AS LIKE N'%PaidAmountKzt%'))
         OR (cc.parent_object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')
             AND cc.definition COLLATE Latin1_General_100_CI_AS LIKE N'%ReservedAmountKzt%')
        )
          AND cc.name NOT IN
          (
              N'CK_bms_web_ExpenseRequestLines_amount',
              N'CK_ExpenseRequestLines_CalculatedAmounts',
              N'CK_bms_web_BudgetPositions_limit',
              N'CK_bms_web_BudgetPositions_reserved',
              N'CK_bms_web_BudgetPositions_paid',
              N'CK_bms_web_BudgetRequestProjections_reserved'
          )
    )
        THROW 59325, N'BMS W26 refused: money columns have unknown CHECK dependencies.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.sql_expression_dependencies AS dependency
        WHERE dependency.referenced_id IN
        (
            OBJECT_ID(N'bms_web.ExpenseRequestLines'),
            OBJECT_ID(N'bms_web.BudgetPositions'),
            OBJECT_ID(N'bms_web.BudgetRequestProjections')
        )
          AND dependency.referenced_minor_id IN
          (
              COLUMNPROPERTY(dependency.referenced_id, N'Amount', 'ColumnId'),
              COLUMNPROPERTY(dependency.referenced_id, N'VatAmount', 'ColumnId'),
              COLUMNPROPERTY(dependency.referenced_id, N'AmountExcludingVat', 'ColumnId'),
              COLUMNPROPERTY(dependency.referenced_id, N'AmountKzt', 'ColumnId'),
              COLUMNPROPERTY(dependency.referenced_id, N'LimitAmountKzt', 'ColumnId'),
              COLUMNPROPERTY(dependency.referenced_id, N'ReservedAmountKzt', 'ColumnId'),
              COLUMNPROPERTY(dependency.referenced_id, N'PaidAmountKzt', 'ColumnId')
          )
          AND dependency.referencing_id NOT IN
          (
              OBJECT_ID(N'bms_web.CK_bms_web_ExpenseRequestLines_amount', N'C'),
              OBJECT_ID(N'bms_web.CK_ExpenseRequestLines_CalculatedAmounts', N'C'),
              OBJECT_ID(N'bms_web.CK_bms_web_BudgetPositions_limit', N'C'),
              OBJECT_ID(N'bms_web.CK_bms_web_BudgetPositions_reserved', N'C'),
              OBJECT_ID(N'bms_web.CK_bms_web_BudgetPositions_paid', N'C'),
              OBJECT_ID(N'bms_web.CK_bms_web_BudgetRequestProjections_reserved', N'C'),
              OBJECT_ID(N'bms_web.DF_bms_web_ExpenseRequestLines_AmountKzt', N'D'),
              OBJECT_ID(N'bms_web.DF_bms_web_BudgetPositions_Reserved', N'D'),
              OBJECT_ID(N'bms_web.DF_bms_web_BudgetPositions_Paid', N'D')
          )
    )
        THROW 59326, N'BMS W26 refused: money columns have unknown expression dependencies.', 1;

    DECLARE @BudgetKeyIndexId int = INDEXPROPERTY(
        OBJECT_ID(N'bms_web.BudgetRequestProjections'),
        N'IX_BudgetRequestProjections_BudgetKey',
        'IndexId');

    IF @BudgetKeyIndexId IS NULL
       OR EXISTS
       (
           SELECT 1
           FROM sys.indexes AS i
           WHERE i.object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')
             AND i.index_id = @BudgetKeyIndexId
             AND
             (
                 i.[type] <> 2 OR i.is_unique <> 0 OR i.is_primary_key <> 0
                 OR i.is_unique_constraint <> 0 OR i.is_disabled <> 0
                 OR i.is_hypothetical <> 0 OR i.has_filter <> 0
                 OR i.filter_definition IS NOT NULL OR i.ignore_dup_key <> 0
                 OR i.is_padded <> 0 OR i.fill_factor <> 0
                 OR i.allow_row_locks <> 1 OR i.allow_page_locks <> 1
                 OR i.data_space_id <>
                    (SELECT data_space_id FROM sys.filegroups WHERE is_default = 1)
             )
       )
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (1, 1, 0, N'CostCenterId'),
                   (2, 2, 0, N'CashFlowItemId'),
                   (3, 3, 0, N'BudgetMonth'),
                   (4, 0, 1, N'ReservedAmountKzt'),
                   (5, 0, 1, N'ExpenseRequestId')
           ) AS expected(IndexColumnId, KeyOrdinal, IsIncluded, ColumnName)
           LEFT JOIN sys.index_columns AS ic
             ON ic.object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')
            AND ic.index_id = @BudgetKeyIndexId
            AND ic.index_column_id = expected.IndexColumnId
           LEFT JOIN sys.columns AS c
             ON c.object_id = ic.object_id AND c.column_id = ic.column_id
           WHERE ic.object_id IS NULL
              OR ic.key_ordinal <> expected.KeyOrdinal
              OR ic.is_included_column <> expected.IsIncluded
              OR ic.is_descending_key <> 0
              OR c.name <> expected.ColumnName
       )
       OR
       (
           SELECT COUNT(*)
           FROM sys.index_columns
           WHERE object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')
             AND index_id = @BudgetKeyIndexId
       ) <> 5
       OR EXISTS
       (
           SELECT 1
           FROM sys.partitions
           WHERE object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')
             AND index_id = @BudgetKeyIndexId
             AND data_compression <> 0
       )
        THROW 59318, N'BMS W26 refused: dependent budget-key index is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.stats_columns AS sc
        INNER JOIN sys.columns AS c
          ON c.object_id = sc.object_id AND c.column_id = sc.column_id
        WHERE
            (sc.object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
             AND c.name IN (N'Amount', N'VatAmount', N'AmountExcludingVat', N'AmountKzt'))
         OR (sc.object_id = OBJECT_ID(N'bms_web.BudgetPositions')
             AND c.name IN (N'LimitAmountKzt', N'ReservedAmountKzt', N'PaidAmountKzt'))
         OR (sc.object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')
             AND c.name = N'ReservedAmountKzt')
    )
        THROW 59319, N'BMS W26 refused: money columns have unsupported statistics dependencies.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.index_columns AS ic
        INNER JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE
            (
                (ic.object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
                 AND c.name IN (N'Amount', N'VatAmount', N'AmountExcludingVat', N'AmountKzt'))
             OR (ic.object_id = OBJECT_ID(N'bms_web.BudgetPositions')
                 AND c.name IN (N'LimitAmountKzt', N'ReservedAmountKzt', N'PaidAmountKzt'))
             OR (ic.object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')
                 AND c.name = N'ReservedAmountKzt')
            )
          AND NOT
          (
              ic.object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')
              AND ic.index_id = @BudgetKeyIndexId
              AND c.name = N'ReservedAmountKzt'
              AND ic.is_included_column = 1
          )
    )
        THROW 59320, N'BMS W26 refused: money columns have unknown index dependencies.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.indexes AS i
        WHERE i.object_id IN
        (
            OBJECT_ID(N'bms_web.ExpenseRequestLines'),
            OBJECT_ID(N'bms_web.BudgetPositions'),
            OBJECT_ID(N'bms_web.BudgetRequestProjections')
        )
          AND i.filter_definition IS NOT NULL
          AND
          (
              i.filter_definition LIKE N'%Amount%'
              OR i.filter_definition LIKE N'%LimitAmountKzt%'
              OR i.filter_definition LIKE N'%ReservedAmountKzt%'
              OR i.filter_definition LIKE N'%PaidAmountKzt%'
          )
    )
        THROW 59321, N'BMS W26 refused: money columns have filtered-index dependencies.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.triggers
        WHERE parent_id IN
        (
            OBJECT_ID(N'bms_web.ExpenseRequestLines'),
            OBJECT_ID(N'bms_web.BudgetPositions'),
            OBJECT_ID(N'bms_web.BudgetRequestProjections')
        )
          AND is_ms_shipped = 0
    )
        THROW 59322, N'BMS W26 refused: money-bearing tables have user triggers.', 1;

    IF @LegacyColumnCount = 8
    BEGIN
        DROP INDEX IX_BudgetRequestProjections_BudgetKey
            ON bms_web.BudgetRequestProjections;

        ALTER TABLE bms_web.ExpenseRequestLines DROP CONSTRAINT
            CK_bms_web_ExpenseRequestLines_amount,
            CK_ExpenseRequestLines_CalculatedAmounts,
            DF_bms_web_ExpenseRequestLines_AmountKzt;
        ALTER TABLE bms_web.BudgetPositions DROP CONSTRAINT
            CK_bms_web_BudgetPositions_limit,
            CK_bms_web_BudgetPositions_reserved,
            CK_bms_web_BudgetPositions_paid,
            DF_bms_web_BudgetPositions_Reserved,
            DF_bms_web_BudgetPositions_Paid;
        ALTER TABLE bms_web.BudgetRequestProjections DROP CONSTRAINT
            CK_bms_web_BudgetRequestProjections_reserved;

        ALTER TABLE bms_web.ExpenseRequestLines ALTER COLUMN Amount decimal(18,2) NOT NULL;
        ALTER TABLE bms_web.ExpenseRequestLines ALTER COLUMN VatAmount decimal(18,2) NULL;
        ALTER TABLE bms_web.ExpenseRequestLines ALTER COLUMN AmountExcludingVat decimal(18,2) NULL;
        ALTER TABLE bms_web.ExpenseRequestLines ALTER COLUMN AmountKzt decimal(18,2) NOT NULL;
        ALTER TABLE bms_web.BudgetPositions ALTER COLUMN LimitAmountKzt decimal(18,2) NULL;
        ALTER TABLE bms_web.BudgetPositions ALTER COLUMN ReservedAmountKzt decimal(18,2) NOT NULL;
        ALTER TABLE bms_web.BudgetPositions ALTER COLUMN PaidAmountKzt decimal(18,2) NOT NULL;
        ALTER TABLE bms_web.BudgetRequestProjections ALTER COLUMN ReservedAmountKzt decimal(18,2) NOT NULL;

        ALTER TABLE bms_web.ExpenseRequestLines ADD
            CONSTRAINT DF_bms_web_ExpenseRequestLines_AmountKzt DEFAULT (0) FOR AmountKzt;
        ALTER TABLE bms_web.BudgetPositions ADD
            CONSTRAINT DF_bms_web_BudgetPositions_Reserved DEFAULT (0) FOR ReservedAmountKzt,
            CONSTRAINT DF_bms_web_BudgetPositions_Paid DEFAULT (0) FOR PaidAmountKzt;

        ALTER TABLE bms_web.ExpenseRequestLines WITH CHECK ADD
            CONSTRAINT CK_bms_web_ExpenseRequestLines_amount CHECK (Amount > 0),
            CONSTRAINT CK_ExpenseRequestLines_CalculatedAmounts CHECK
            (
                AmountKzt = Amount
                AND
                (
                    (VatRateCode IS NULL AND VatAmount IS NULL AND AmountExcludingVat IS NULL)
                    OR (VatRateCode = N'WithoutVat' AND VatAmount = 0 AND AmountExcludingVat = Amount)
                    OR (VatRateCode = N'Vat16' AND VatAmount = ROUND(Amount * 16 / 116, 2)
                        AND AmountExcludingVat = Amount - VatAmount)
                )
            );
        ALTER TABLE bms_web.BudgetPositions WITH CHECK ADD
            CONSTRAINT CK_bms_web_BudgetPositions_limit CHECK
                (LimitAmountKzt IS NULL OR LimitAmountKzt >= 0),
            CONSTRAINT CK_bms_web_BudgetPositions_reserved CHECK (ReservedAmountKzt >= 0),
            CONSTRAINT CK_bms_web_BudgetPositions_paid CHECK (PaidAmountKzt >= 0);
        ALTER TABLE bms_web.BudgetRequestProjections WITH CHECK ADD
            CONSTRAINT CK_bms_web_BudgetRequestProjections_reserved CHECK
                (ReservedAmountKzt > 0);

        ALTER TABLE bms_web.ExpenseRequestLines CHECK CONSTRAINT
            CK_bms_web_ExpenseRequestLines_amount,
            CK_ExpenseRequestLines_CalculatedAmounts;
        ALTER TABLE bms_web.BudgetPositions CHECK CONSTRAINT
            CK_bms_web_BudgetPositions_limit,
            CK_bms_web_BudgetPositions_reserved,
            CK_bms_web_BudgetPositions_paid;
        ALTER TABLE bms_web.BudgetRequestProjections CHECK CONSTRAINT
            CK_bms_web_BudgetRequestProjections_reserved;

        CREATE INDEX IX_BudgetRequestProjections_BudgetKey
            ON bms_web.BudgetRequestProjections
                (CostCenterId, CashFlowItemId, BudgetMonth)
            INCLUDE (ReservedAmountKzt, ExpenseRequestId);
    END;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.ExpenseRequestLines', N'Amount', 0),
                (N'bms_web.ExpenseRequestLines', N'VatAmount', 1),
                (N'bms_web.ExpenseRequestLines', N'AmountExcludingVat', 1),
                (N'bms_web.ExpenseRequestLines', N'AmountKzt', 0),
                (N'bms_web.BudgetPositions', N'LimitAmountKzt', 1),
                (N'bms_web.BudgetPositions', N'ReservedAmountKzt', 0),
                (N'bms_web.BudgetPositions', N'PaidAmountKzt', 0),
                (N'bms_web.BudgetRequestProjections', N'ReservedAmountKzt', 0)
        ) AS expected(ObjectName, ColumnName, IsNullable)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(expected.ObjectName)
         AND c.name = expected.ColumnName
        WHERE c.object_id IS NULL
           OR TYPE_NAME(c.user_type_id) <> N'decimal'
           OR c.[precision] <> 18
           OR c.scale <> 2
           OR c.is_nullable <> expected.IsNullable
    )
        THROW 59323, N'BMS W26 refused: final decimal(18,2) contract is not ready.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.ExpenseRequestLines', N'AmountKzt',
                 N'DF_bms_web_ExpenseRequestLines_AmountKzt'),
                (N'bms_web.BudgetPositions', N'ReservedAmountKzt',
                 N'DF_bms_web_BudgetPositions_Reserved'),
                (N'bms_web.BudgetPositions', N'PaidAmountKzt',
                 N'DF_bms_web_BudgetPositions_Paid')
        ) AS expected(ObjectName, ColumnName, ConstraintName)
        LEFT JOIN sys.default_constraints AS dc
          ON dc.parent_object_id = OBJECT_ID(expected.ObjectName)
         AND dc.parent_column_id =
             COLUMNPROPERTY(OBJECT_ID(expected.ObjectName), expected.ColumnName, 'ColumnId')
         AND dc.name = expected.ConstraintName
        WHERE dc.object_id IS NULL
           OR REPLACE(REPLACE(REPLACE(dc.definition, N'(', N''), N')', N''), N' ', N'')
              COLLATE Latin1_General_100_BIN2 <> N'0'
    )
        THROW 59327, N'BMS W26 refused: final money DEFAULT contract is not ready.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.ExpenseRequestLines',
                 N'CK_bms_web_ExpenseRequestLines_amount',
                 N'Amount>0', N'Amount'),
                (N'bms_web.ExpenseRequestLines',
                 N'CK_ExpenseRequestLines_CalculatedAmounts',
                 N'AmountKzt=AmountANDVatRateCodeISNULLANDVatAmountISNULLANDAmountExcludingVatISNULLORVatRateCode=N''WithoutVat''ANDVatAmount=0ANDAmountExcludingVat=AmountORVatRateCode=N''Vat16''ANDVatAmount=roundAmount*16/116,2ANDAmountExcludingVat=Amount-VatAmount',
                 NULL),
                (N'bms_web.BudgetPositions',
                 N'CK_bms_web_BudgetPositions_limit',
                 N'LimitAmountKztISNULLORLimitAmountKzt>=0', N'LimitAmountKzt'),
                (N'bms_web.BudgetPositions',
                 N'CK_bms_web_BudgetPositions_reserved',
                 N'ReservedAmountKzt>=0', N'ReservedAmountKzt'),
                (N'bms_web.BudgetPositions',
                 N'CK_bms_web_BudgetPositions_paid',
                 N'PaidAmountKzt>=0', N'PaidAmountKzt'),
                (N'bms_web.BudgetRequestProjections',
                 N'CK_bms_web_BudgetRequestProjections_reserved',
                 N'ReservedAmountKzt>0', N'ReservedAmountKzt')
        ) AS expected(ObjectName, ConstraintName, NormalizedDefinition, ParentColumnName)
        LEFT JOIN sys.check_constraints AS cc
          ON cc.parent_object_id = OBJECT_ID(expected.ObjectName)
         AND cc.name = expected.ConstraintName
        CROSS APPLY
        (
            VALUES
            (
                REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                    cc.definition, N'[', N''), N']', N''), N'(', N''), N')', N''),
                    N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N'')
            )
        ) AS normalized(Definition)
        WHERE cc.object_id IS NULL
           OR cc.is_disabled <> 0 OR cc.is_not_trusted <> 0
           OR cc.is_not_for_replication <> 0 OR cc.is_system_named <> 0
           OR cc.parent_column_id <>
              CASE WHEN expected.ParentColumnName IS NULL THEN 0
                   ELSE COLUMNPROPERTY(
                       OBJECT_ID(expected.ObjectName), expected.ParentColumnName, N'ColumnId')
              END
           OR normalized.Definition COLLATE Latin1_General_100_BIN2 <>
              expected.NormalizedDefinition COLLATE Latin1_General_100_BIN2
    )
        THROW 59328, N'BMS W26 refused: final money CHECK contract is not ready.', 1;

    SET @BudgetKeyIndexId = INDEXPROPERTY(
        OBJECT_ID(N'bms_web.BudgetRequestProjections'),
        N'IX_BudgetRequestProjections_BudgetKey',
        'IndexId');

    IF @BudgetKeyIndexId IS NULL
       OR EXISTS
       (
           SELECT 1
           FROM sys.indexes AS i
           WHERE i.object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')
             AND i.index_id = @BudgetKeyIndexId
             AND
             (
                 i.[type] <> 2 OR i.is_unique <> 0 OR i.is_primary_key <> 0
                 OR i.is_unique_constraint <> 0 OR i.is_disabled <> 0
                 OR i.is_hypothetical <> 0 OR i.has_filter <> 0
                 OR i.filter_definition IS NOT NULL OR i.ignore_dup_key <> 0
                 OR i.is_padded <> 0 OR i.fill_factor <> 0
                 OR i.allow_row_locks <> 1 OR i.allow_page_locks <> 1
                 OR i.data_space_id <>
                    (SELECT data_space_id FROM sys.filegroups WHERE is_default = 1)
             )
       )
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (1, 1, 0, N'CostCenterId'),
                   (2, 2, 0, N'CashFlowItemId'),
                   (3, 3, 0, N'BudgetMonth'),
                   (4, 0, 1, N'ReservedAmountKzt'),
                   (5, 0, 1, N'ExpenseRequestId')
           ) AS expected(IndexColumnId, KeyOrdinal, IsIncluded, ColumnName)
           LEFT JOIN sys.index_columns AS ic
             ON ic.object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')
            AND ic.index_id = @BudgetKeyIndexId
            AND ic.index_column_id = expected.IndexColumnId
           LEFT JOIN sys.columns AS c
             ON c.object_id = ic.object_id AND c.column_id = ic.column_id
           WHERE ic.object_id IS NULL
              OR ic.key_ordinal <> expected.KeyOrdinal
              OR ic.is_included_column <> expected.IsIncluded
              OR ic.is_descending_key <> 0
              OR c.name <> expected.ColumnName
       )
       OR
       (
           SELECT COUNT(*)
           FROM sys.index_columns
           WHERE object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')
             AND index_id = @BudgetKeyIndexId
       ) <> 5
       OR EXISTS
       (
           SELECT 1
           FROM sys.partitions
           WHERE object_id = OBJECT_ID(N'bms_web.BudgetRequestProjections')
             AND index_id = @BudgetKeyIndexId
             AND data_compression <> 0
       )
        THROW 59329, N'BMS W26 refused: final budget-key index contract is not ready.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 12
    )
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (12, N'012_money_decimal18_scale2.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
