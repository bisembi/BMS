-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web W487 — dormant immutable storage for accrual closing cycles.

    IMPORTANT
    - Apply SQL001 through SQL024 first and run this script standalone.
    - This migration creates empty source-evidence storage only. It does not
      enable CLOSE_PARTIAL, CLOSE_FULL or REOPEN, change an expense request,
      select gross/net basis, allocate an amount, write an accrual, or grant a
      role or command.
    - Every later close/reopen cycle must receive a new CycleId and a strictly
      increasing request-local CycleNumber inside the future request lock and
      Serializable business transaction.
    - It has not been executed against an accounting server.
*/

IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS W487 refused: run 025_expense_request_accrual_closing_cycles.sql outside an ambient transaction.', 16, 1);
    RETURN;
END;

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 62101, N'BMS W487 refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 62102, N'BMS W487 refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.ExpenseRequests', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.Organizations', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.CostCenters', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.CashFlowItems', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.ExpenseItems', N'U') IS NULL
    THROW 62103, N'BMS W487 refused: required managed source tables are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no > 25
    )
        THROW 62104, N'BMS W487 refused: a future schema version is already registered.', 1;

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
                (24, N'024_expense_request_workflow_activation.sql')
        ) AS expected(version_no, script_name)
        INNER JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK)
          ON actual.version_no = expected.version_no
         AND actual.script_name COLLATE Latin1_General_100_BIN2 =
             expected.script_name COLLATE Latin1_General_100_BIN2
         AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
    ) <> 24
        THROW 62105, N'BMS W487 refused: exact schema versions 1 through 24 are required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 25
          AND
          (
              script_name COLLATE Latin1_General_100_BIN2 <>
                  N'025_expense_request_accrual_closing_cycles.sql'
              OR DATALENGTH(script_name) <>
                  DATALENGTH(N'025_expense_request_accrual_closing_cycles.sql')
          )
    )
        THROW 62106, N'BMS W487 refused: schema version 25 belongs to another script.', 1;

    IF OBJECT_ID(N'bms_web.ExpenseRequestAccrualClosingCycles', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.ExpenseRequestAccrualClosingCycles
        (
            ExpenseRequestId          uniqueidentifier NOT NULL,
            CycleId                   uniqueidentifier NOT NULL,
            CycleNumber               int NOT NULL,
            OrganizationId            uniqueidentifier NOT NULL,
            CostCenterId              uniqueidentifier NOT NULL,
            RequestVersion            uniqueidentifier NOT NULL,
            SourceUpdatedAtUtc        datetimeoffset(3) NOT NULL,
            CapturedAtUtc             datetimeoffset(3) NOT NULL,
            GrossAmountKzt            decimal(18, 2) NOT NULL,
            VatAmountKzt              decimal(18, 2) NOT NULL,
            AmountExcludingVatKzt     decimal(18, 2) NOT NULL,
            CONSTRAINT PK_bms_web_ExpenseRequestAccrualClosingCycles
                PRIMARY KEY CLUSTERED (ExpenseRequestId ASC, CycleId ASC),
            CONSTRAINT CK_bms_web_ExpenseRequestAccrualClosingCycles_Identity CHECK
            (
                CycleId <> CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000')
                AND CycleNumber > 0
                AND RequestVersion <> CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000')
            ),
            CONSTRAINT CK_bms_web_ExpenseRequestAccrualClosingCycles_Amounts CHECK
            (
                GrossAmountKzt >= 0 AND VatAmountKzt >= 0
                AND AmountExcludingVatKzt >= 0
                AND VatAmountKzt + AmountExcludingVatKzt = GrossAmountKzt
            ),
            CONSTRAINT CK_bms_web_ExpenseRequestAccrualClosingCycles_Time CHECK
            (
                DATEPART(TZOFFSET, SourceUpdatedAtUtc) = 0
                AND DATEPART(TZOFFSET, CapturedAtUtc) = 0
                AND CapturedAtUtc >= SourceUpdatedAtUtc
            ),
            CONSTRAINT FK_bms_web_ExpenseRequestAccrualClosingCycles_ExpenseRequest
                FOREIGN KEY (ExpenseRequestId) REFERENCES bms_web.ExpenseRequests (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_ExpenseRequestAccrualClosingCycles_Organization
                FOREIGN KEY (OrganizationId) REFERENCES bms_web.Organizations (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_ExpenseRequestAccrualClosingCycles_CostCenter
                FOREIGN KEY (CostCenterId) REFERENCES bms_web.CostCenters (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION
        );

        CREATE UNIQUE NONCLUSTERED INDEX
            UQ_bms_web_ExpenseRequestAccrualClosingCycles_Request_Number
            ON bms_web.ExpenseRequestAccrualClosingCycles
                (ExpenseRequestId ASC, CycleNumber ASC);
        CREATE UNIQUE NONCLUSTERED INDEX
            UQ_bms_web_ExpenseRequestAccrualClosingCycles_CycleId
            ON bms_web.ExpenseRequestAccrualClosingCycles (CycleId ASC);
        CREATE NONCLUSTERED INDEX
            IX_bms_web_ExpenseRequestAccrualClosingCycles_CapturedAtUtc
            ON bms_web.ExpenseRequestAccrualClosingCycles (CapturedAtUtc ASC);
        CREATE NONCLUSTERED INDEX
            IX_bms_web_ExpenseRequestAccrualClosingCycles_Organization
            ON bms_web.ExpenseRequestAccrualClosingCycles (OrganizationId ASC);
        CREATE NONCLUSTERED INDEX
            IX_bms_web_ExpenseRequestAccrualClosingCycles_CostCenter
            ON bms_web.ExpenseRequestAccrualClosingCycles (CostCenterId ASC);
    END;

    IF OBJECT_ID(N'bms_web.ExpenseRequestAccrualClosingCycleLines', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.ExpenseRequestAccrualClosingCycleLines
        (
            ExpenseRequestId          uniqueidentifier NOT NULL,
            CycleId                   uniqueidentifier NOT NULL,
            LineId                    uniqueidentifier NOT NULL,
            SortOrder                 int NOT NULL,
            CashFlowItemId            uniqueidentifier NOT NULL,
            CostCenterId              uniqueidentifier NOT NULL,
            ExpenseItemId             uniqueidentifier NOT NULL,
            AccrualPeriod             date NOT NULL,
            GrossAmountKzt            decimal(18, 2) NOT NULL,
            VatAmountKzt              decimal(18, 2) NOT NULL,
            AmountExcludingVatKzt     decimal(18, 2) NOT NULL,
            CONSTRAINT PK_bms_web_ExpenseRequestAccrualClosingCycleLines
                PRIMARY KEY CLUSTERED (ExpenseRequestId ASC, CycleId ASC, LineId ASC),
            CONSTRAINT CK_bms_web_ExpenseRequestAccrualClosingCycleLines_Identity CHECK
            (
                LineId <> CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000')
                AND SortOrder > 0
                AND CashFlowItemId <> CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000')
                AND CostCenterId <> CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000')
                AND ExpenseItemId <> CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000')
            ),
            CONSTRAINT CK_bms_web_ExpenseRequestAccrualClosingCycleLines_Period CHECK
                (DAY(AccrualPeriod) = 1),
            CONSTRAINT CK_bms_web_ExpenseRequestAccrualClosingCycleLines_Amounts CHECK
            (
                GrossAmountKzt >= 0 AND VatAmountKzt >= 0
                AND AmountExcludingVatKzt >= 0
                AND VatAmountKzt + AmountExcludingVatKzt = GrossAmountKzt
            ),
            CONSTRAINT FK_bms_web_ExpenseRequestAccrualClosingCycleLines_Cycle
                FOREIGN KEY (ExpenseRequestId, CycleId)
                REFERENCES bms_web.ExpenseRequestAccrualClosingCycles
                    (ExpenseRequestId, CycleId)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_ExpenseRequestAccrualClosingCycleLines_CashFlowItem
                FOREIGN KEY (CashFlowItemId) REFERENCES bms_web.CashFlowItems (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_ExpenseRequestAccrualClosingCycleLines_CostCenter
                FOREIGN KEY (CostCenterId) REFERENCES bms_web.CostCenters (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION
        );

        CREATE UNIQUE NONCLUSTERED INDEX
            UQ_bms_web_ExpenseRequestAccrualClosingCycleLines_Cycle_SortOrder
            ON bms_web.ExpenseRequestAccrualClosingCycleLines
                (ExpenseRequestId ASC, CycleId ASC, SortOrder ASC);
        CREATE NONCLUSTERED INDEX
            IX_bms_web_ExpenseRequestAccrualClosingCycleLines_ExpenseItem_Period
            ON bms_web.ExpenseRequestAccrualClosingCycleLines
                (ExpenseItemId ASC, AccrualPeriod ASC);
        CREATE NONCLUSTERED INDEX
            IX_bms_web_ExpenseRequestAccrualClosingCycleLines_CashFlowItem
            ON bms_web.ExpenseRequestAccrualClosingCycleLines (CashFlowItemId ASC);
        CREATE NONCLUSTERED INDEX
            IX_bms_web_ExpenseRequestAccrualClosingCycleLines_CostCenter
            ON bms_web.ExpenseRequestAccrualClosingCycleLines (CostCenterId ASC);
    END;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'ExpenseRequestAccrualClosingCycles', N'ExpenseRequestId', N'uniqueidentifier', 16, 0, 0, 0),
                (N'ExpenseRequestAccrualClosingCycles', N'CycleId', N'uniqueidentifier', 16, 0, 0, 0),
                (N'ExpenseRequestAccrualClosingCycles', N'CycleNumber', N'int', 4, 0, 10, 0),
                (N'ExpenseRequestAccrualClosingCycles', N'OrganizationId', N'uniqueidentifier', 16, 0, 0, 0),
                (N'ExpenseRequestAccrualClosingCycles', N'CostCenterId', N'uniqueidentifier', 16, 0, 0, 0),
                (N'ExpenseRequestAccrualClosingCycles', N'RequestVersion', N'uniqueidentifier', 16, 0, 0, 0),
                (N'ExpenseRequestAccrualClosingCycles', N'SourceUpdatedAtUtc', N'datetimeoffset', 9, 0, 30, 3),
                (N'ExpenseRequestAccrualClosingCycles', N'CapturedAtUtc', N'datetimeoffset', 9, 0, 30, 3),
                (N'ExpenseRequestAccrualClosingCycles', N'GrossAmountKzt', N'decimal', 9, 0, 18, 2),
                (N'ExpenseRequestAccrualClosingCycles', N'VatAmountKzt', N'decimal', 9, 0, 18, 2),
                (N'ExpenseRequestAccrualClosingCycles', N'AmountExcludingVatKzt', N'decimal', 9, 0, 18, 2),
                (N'ExpenseRequestAccrualClosingCycleLines', N'ExpenseRequestId', N'uniqueidentifier', 16, 0, 0, 0),
                (N'ExpenseRequestAccrualClosingCycleLines', N'CycleId', N'uniqueidentifier', 16, 0, 0, 0),
                (N'ExpenseRequestAccrualClosingCycleLines', N'LineId', N'uniqueidentifier', 16, 0, 0, 0),
                (N'ExpenseRequestAccrualClosingCycleLines', N'SortOrder', N'int', 4, 0, 10, 0),
                (N'ExpenseRequestAccrualClosingCycleLines', N'CashFlowItemId', N'uniqueidentifier', 16, 0, 0, 0),
                (N'ExpenseRequestAccrualClosingCycleLines', N'CostCenterId', N'uniqueidentifier', 16, 0, 0, 0),
                (N'ExpenseRequestAccrualClosingCycleLines', N'ExpenseItemId', N'uniqueidentifier', 16, 0, 0, 0),
                (N'ExpenseRequestAccrualClosingCycleLines', N'AccrualPeriod', N'date', 3, 0, 10, 0),
                (N'ExpenseRequestAccrualClosingCycleLines', N'GrossAmountKzt', N'decimal', 9, 0, 18, 2),
                (N'ExpenseRequestAccrualClosingCycleLines', N'VatAmountKzt', N'decimal', 9, 0, 18, 2),
                (N'ExpenseRequestAccrualClosingCycleLines', N'AmountExcludingVatKzt', N'decimal', 9, 0, 18, 2)
        ) AS expected(TableName, ColumnName, TypeName, MaxLength, IsNullable, [Precision], Scale)
        LEFT JOIN sys.columns AS actual
          ON actual.object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
         AND actual.name = expected.ColumnName
        WHERE actual.object_id IS NULL
           OR TYPE_NAME(actual.user_type_id) <> expected.TypeName
           OR actual.max_length <> expected.MaxLength
           OR actual.is_nullable <> expected.IsNullable
           OR actual.[precision] <> expected.[Precision]
           OR actual.scale <> expected.Scale
           OR actual.is_identity <> 0 OR actual.is_computed <> 0
           OR actual.default_object_id <> 0 OR actual.rule_object_id <> 0
           OR actual.generated_always_type <> 0 OR actual.is_rowguidcol <> 0
           OR actual.is_filestream <> 0 OR actual.is_sparse <> 0
           OR actual.is_column_set <> 0 OR actual.encryption_type IS NOT NULL
           OR actual.is_hidden <> 0 OR actual.is_masked <> 0
           OR actual.collation_name IS NOT NULL
    )
       OR (SELECT COUNT(*) FROM sys.columns WHERE object_id =
           OBJECT_ID(N'bms_web.ExpenseRequestAccrualClosingCycles')) <> 11
       OR (SELECT COUNT(*) FROM sys.columns WHERE object_id =
           OBJECT_ID(N'bms_web.ExpenseRequestAccrualClosingCycleLines')) <> 11
        THROW 62107, N'BMS W487 refused: accrual closing cycle column contract is drifted.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.indexes
        WHERE object_id IN
        (
            OBJECT_ID(N'bms_web.ExpenseRequestAccrualClosingCycles'),
            OBJECT_ID(N'bms_web.ExpenseRequestAccrualClosingCycleLines')
        )
          AND index_id > 0
    ) <> 11
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (N'ExpenseRequestAccrualClosingCycles', N'PK_bms_web_ExpenseRequestAccrualClosingCycles', 1, 1, 1, 2),
                   (N'ExpenseRequestAccrualClosingCycles', N'UQ_bms_web_ExpenseRequestAccrualClosingCycles_Request_Number', 2, 1, 0, 2),
                   (N'ExpenseRequestAccrualClosingCycles', N'UQ_bms_web_ExpenseRequestAccrualClosingCycles_CycleId', 2, 1, 0, 1),
                   (N'ExpenseRequestAccrualClosingCycles', N'IX_bms_web_ExpenseRequestAccrualClosingCycles_CapturedAtUtc', 2, 0, 0, 1),
                   (N'ExpenseRequestAccrualClosingCycles', N'IX_bms_web_ExpenseRequestAccrualClosingCycles_Organization', 2, 0, 0, 1),
                   (N'ExpenseRequestAccrualClosingCycles', N'IX_bms_web_ExpenseRequestAccrualClosingCycles_CostCenter', 2, 0, 0, 1),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'PK_bms_web_ExpenseRequestAccrualClosingCycleLines', 1, 1, 1, 3),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'UQ_bms_web_ExpenseRequestAccrualClosingCycleLines_Cycle_SortOrder', 2, 1, 0, 3),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'IX_bms_web_ExpenseRequestAccrualClosingCycleLines_ExpenseItem_Period', 2, 0, 0, 2),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'IX_bms_web_ExpenseRequestAccrualClosingCycleLines_CashFlowItem', 2, 0, 0, 1),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'IX_bms_web_ExpenseRequestAccrualClosingCycleLines_CostCenter', 2, 0, 0, 1)
           ) AS expected(TableName, IndexName, IndexType, IsUnique, IsPrimaryKey, KeyCount)
           LEFT JOIN sys.indexes AS actual
             ON actual.object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
            AND actual.name = expected.IndexName
           WHERE actual.index_id IS NULL OR actual.type <> expected.IndexType
              OR actual.is_unique <> expected.IsUnique
              OR actual.is_primary_key <> expected.IsPrimaryKey
              OR actual.is_disabled <> 0 OR actual.is_hypothetical <> 0
              OR actual.has_filter <> 0 OR actual.filter_definition IS NOT NULL
              OR actual.ignore_dup_key <> 0
              OR (SELECT COUNT(*) FROM sys.index_columns AS key_column
                  WHERE key_column.object_id = actual.object_id
                    AND key_column.index_id = actual.index_id
                    AND key_column.key_ordinal > 0) <> expected.KeyCount
       )
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (N'ExpenseRequestAccrualClosingCycles', N'PK_bms_web_ExpenseRequestAccrualClosingCycles', 1, N'ExpenseRequestId'),
                   (N'ExpenseRequestAccrualClosingCycles', N'PK_bms_web_ExpenseRequestAccrualClosingCycles', 2, N'CycleId'),
                   (N'ExpenseRequestAccrualClosingCycles', N'UQ_bms_web_ExpenseRequestAccrualClosingCycles_Request_Number', 1, N'ExpenseRequestId'),
                   (N'ExpenseRequestAccrualClosingCycles', N'UQ_bms_web_ExpenseRequestAccrualClosingCycles_Request_Number', 2, N'CycleNumber'),
                   (N'ExpenseRequestAccrualClosingCycles', N'UQ_bms_web_ExpenseRequestAccrualClosingCycles_CycleId', 1, N'CycleId'),
                   (N'ExpenseRequestAccrualClosingCycles', N'IX_bms_web_ExpenseRequestAccrualClosingCycles_CapturedAtUtc', 1, N'CapturedAtUtc'),
                   (N'ExpenseRequestAccrualClosingCycles', N'IX_bms_web_ExpenseRequestAccrualClosingCycles_Organization', 1, N'OrganizationId'),
                   (N'ExpenseRequestAccrualClosingCycles', N'IX_bms_web_ExpenseRequestAccrualClosingCycles_CostCenter', 1, N'CostCenterId'),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'PK_bms_web_ExpenseRequestAccrualClosingCycleLines', 1, N'ExpenseRequestId'),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'PK_bms_web_ExpenseRequestAccrualClosingCycleLines', 2, N'CycleId'),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'PK_bms_web_ExpenseRequestAccrualClosingCycleLines', 3, N'LineId'),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'UQ_bms_web_ExpenseRequestAccrualClosingCycleLines_Cycle_SortOrder', 1, N'ExpenseRequestId'),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'UQ_bms_web_ExpenseRequestAccrualClosingCycleLines_Cycle_SortOrder', 2, N'CycleId'),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'UQ_bms_web_ExpenseRequestAccrualClosingCycleLines_Cycle_SortOrder', 3, N'SortOrder'),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'IX_bms_web_ExpenseRequestAccrualClosingCycleLines_ExpenseItem_Period', 1, N'ExpenseItemId'),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'IX_bms_web_ExpenseRequestAccrualClosingCycleLines_ExpenseItem_Period', 2, N'AccrualPeriod'),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'IX_bms_web_ExpenseRequestAccrualClosingCycleLines_CashFlowItem', 1, N'CashFlowItemId'),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'IX_bms_web_ExpenseRequestAccrualClosingCycleLines_CostCenter', 1, N'CostCenterId')
           ) AS expected(TableName, IndexName, KeyOrdinal, ColumnName)
           LEFT JOIN sys.indexes AS actual_index
             ON actual_index.object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
            AND actual_index.name = expected.IndexName
           LEFT JOIN sys.index_columns AS actual_key
             ON actual_key.object_id = actual_index.object_id
            AND actual_key.index_id = actual_index.index_id
            AND actual_key.key_ordinal = expected.KeyOrdinal
           LEFT JOIN sys.columns AS actual_column
             ON actual_column.object_id = actual_key.object_id
            AND actual_column.column_id = actual_key.column_id
           WHERE actual_column.name IS NULL OR actual_column.name <> expected.ColumnName
              OR actual_key.is_descending_key <> 0 OR actual_key.is_included_column <> 0
       )
        THROW 62108, N'BMS W487 refused: accrual closing cycle index contract is drifted.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.foreign_keys
        WHERE parent_object_id IN
        (
            OBJECT_ID(N'bms_web.ExpenseRequestAccrualClosingCycles'),
            OBJECT_ID(N'bms_web.ExpenseRequestAccrualClosingCycleLines')
        )
    ) <> 6
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (N'ExpenseRequestAccrualClosingCycles', N'FK_bms_web_ExpenseRequestAccrualClosingCycles_ExpenseRequest', 1, N'ExpenseRequestId', N'ExpenseRequests', N'Id'),
                   (N'ExpenseRequestAccrualClosingCycles', N'FK_bms_web_ExpenseRequestAccrualClosingCycles_Organization', 1, N'OrganizationId', N'Organizations', N'Id'),
                   (N'ExpenseRequestAccrualClosingCycles', N'FK_bms_web_ExpenseRequestAccrualClosingCycles_CostCenter', 1, N'CostCenterId', N'CostCenters', N'Id'),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'FK_bms_web_ExpenseRequestAccrualClosingCycleLines_Cycle', 1, N'ExpenseRequestId', N'ExpenseRequestAccrualClosingCycles', N'ExpenseRequestId'),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'FK_bms_web_ExpenseRequestAccrualClosingCycleLines_Cycle', 2, N'CycleId', N'ExpenseRequestAccrualClosingCycles', N'CycleId'),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'FK_bms_web_ExpenseRequestAccrualClosingCycleLines_CashFlowItem', 1, N'CashFlowItemId', N'CashFlowItems', N'Id'),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'FK_bms_web_ExpenseRequestAccrualClosingCycleLines_CostCenter', 1, N'CostCenterId', N'CostCenters', N'Id')
           ) AS expected(TableName, ForeignKeyName, Ordinal, ParentColumnName, ReferencedTableName, ReferencedColumnName)
           LEFT JOIN sys.foreign_keys AS actual_fk
             ON actual_fk.parent_object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
            AND actual_fk.name = expected.ForeignKeyName
           LEFT JOIN sys.foreign_key_columns AS actual_fkc
             ON actual_fkc.constraint_object_id = actual_fk.object_id
            AND actual_fkc.constraint_column_id = expected.Ordinal
           LEFT JOIN sys.columns AS actual_parent_column
             ON actual_parent_column.object_id = actual_fkc.parent_object_id
            AND actual_parent_column.column_id = actual_fkc.parent_column_id
           LEFT JOIN sys.tables AS actual_referenced_table
             ON actual_referenced_table.object_id = actual_fkc.referenced_object_id
           LEFT JOIN sys.columns AS actual_referenced_column
             ON actual_referenced_column.object_id = actual_fkc.referenced_object_id
            AND actual_referenced_column.column_id = actual_fkc.referenced_column_id
           WHERE actual_fk.object_id IS NULL OR actual_fk.is_disabled <> 0
              OR actual_fk.is_not_trusted <> 0 OR actual_fk.is_not_for_replication <> 0
              OR actual_fk.is_system_named <> 0 OR actual_fk.delete_referential_action <> 0
              OR actual_fk.update_referential_action <> 0
              OR actual_parent_column.name <> expected.ParentColumnName
              OR actual_referenced_table.name <> expected.ReferencedTableName
              OR actual_referenced_column.name <> expected.ReferencedColumnName
       )
        THROW 62109, N'BMS W487 refused: accrual closing cycle foreign-key contract is drifted.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.check_constraints
        WHERE parent_object_id IN
        (
            OBJECT_ID(N'bms_web.ExpenseRequestAccrualClosingCycles'),
            OBJECT_ID(N'bms_web.ExpenseRequestAccrualClosingCycleLines')
        )
    ) <> 6
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (N'ExpenseRequestAccrualClosingCycles', N'CK_bms_web_ExpenseRequestAccrualClosingCycles_Identity'),
                   (N'ExpenseRequestAccrualClosingCycles', N'CK_bms_web_ExpenseRequestAccrualClosingCycles_Amounts'),
                   (N'ExpenseRequestAccrualClosingCycles', N'CK_bms_web_ExpenseRequestAccrualClosingCycles_Time'),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'CK_bms_web_ExpenseRequestAccrualClosingCycleLines_Identity'),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'CK_bms_web_ExpenseRequestAccrualClosingCycleLines_Period'),
                   (N'ExpenseRequestAccrualClosingCycleLines', N'CK_bms_web_ExpenseRequestAccrualClosingCycleLines_Amounts')
           ) AS expected(TableName, ConstraintName)
           LEFT JOIN sys.check_constraints AS actual
             ON actual.parent_object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
            AND actual.name = expected.ConstraintName
           WHERE actual.object_id IS NULL OR actual.is_disabled <> 0
              OR actual.is_not_trusted <> 0 OR actual.is_not_for_replication <> 0
              OR actual.is_system_named <> 0 OR actual.definition IS NULL
       )
       OR EXISTS
       (
           SELECT 1
           FROM sys.triggers
           WHERE parent_id IN
           (
               OBJECT_ID(N'bms_web.ExpenseRequestAccrualClosingCycles'),
               OBJECT_ID(N'bms_web.ExpenseRequestAccrualClosingCycleLines')
           )
       )
        THROW 62110, N'BMS W487 refused: accrual closing cycle CHECK or trigger contract is drifted.', 1;

    IF NOT EXISTS
    (
        SELECT 1 FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 25
    )
        INSERT INTO bms_web.schema_version (version_no, script_name)
        VALUES (25, N'025_expense_request_accrual_closing_cycles.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
