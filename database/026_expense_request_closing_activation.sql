-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web W513 — physical activation shape for accrual closing.

    IMPORTANT
    - Apply SQL001 through SQL025 first and run this script standalone.
    - The migration adds only durable closing state. It does not grant a role,
      invent a payment, activate a payment intake, or contact a remote system.
    - SQL025 cycle rows cannot be upgraded safely: their original-currency
      gross/VAT/net amounts were not stored. If either dormant cycle table is
      non-empty while those columns are absent, this migration refuses rather
      than deriving historical amounts from current data or KZT.
    - It has not been executed against an accounting server.
*/

IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS W513 refused: run 026_expense_request_closing_activation.sql outside an ambient transaction.', 16, 1);
    RETURN;
END;

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 62201, N'BMS W513 refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 62202, N'BMS W513 refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.ExpenseRequests', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.ExpenseRequestLines', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.ExpenseRequestAccrualClosingCycles', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.ExpenseRequestAccrualClosingCycleLines', N'U') IS NULL
    THROW 62203, N'BMS W513 refused: required managed closing tables are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no > 26
    )
        THROW 62204, N'BMS W513 refused: a future schema version is already registered.', 1;

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
                (25, N'025_expense_request_accrual_closing_cycles.sql')
        ) AS expected(version_no, script_name)
        INNER JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK)
          ON actual.version_no = expected.version_no
         AND actual.script_name COLLATE Latin1_General_100_BIN2 =
             expected.script_name COLLATE Latin1_General_100_BIN2
         AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
    ) <> 25
        THROW 62205, N'BMS W513 refused: exact schema versions 1 through 25 are required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 26
          AND
          (
              script_name COLLATE Latin1_General_100_BIN2 <>
                  N'026_expense_request_closing_activation.sql'
              OR DATALENGTH(script_name) <>
                  DATALENGTH(N'026_expense_request_closing_activation.sql')
          )
    )
        THROW 62206, N'BMS W513 refused: schema version 26 belongs to another script.', 1;

    IF
    (
        COL_LENGTH(N'bms_web.ExpenseRequestAccrualClosingCycles', N'GrossAmount') IS NULL
        OR COL_LENGTH(N'bms_web.ExpenseRequestAccrualClosingCycles', N'VatAmount') IS NULL
        OR COL_LENGTH(N'bms_web.ExpenseRequestAccrualClosingCycles', N'AmountExcludingVat') IS NULL
        OR COL_LENGTH(N'bms_web.ExpenseRequestAccrualClosingCycleLines', N'GrossAmount') IS NULL
        OR COL_LENGTH(N'bms_web.ExpenseRequestAccrualClosingCycleLines', N'VatAmount') IS NULL
        OR COL_LENGTH(N'bms_web.ExpenseRequestAccrualClosingCycleLines', N'AmountExcludingVat') IS NULL
    )
    AND
    (
        EXISTS (SELECT 1 FROM bms_web.ExpenseRequestAccrualClosingCycles WITH (UPDLOCK, HOLDLOCK))
        OR EXISTS (SELECT 1 FROM bms_web.ExpenseRequestAccrualClosingCycleLines WITH (UPDLOCK, HOLDLOCK))
    )
        THROW 62207, N'BMS W513 refused: legacy closing cycles lack unreconstructable original-currency amounts.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.ExpenseRequests WITH (UPDLOCK, HOLDLOCK)
        WHERE Status = N'CLOSED'
          AND COL_LENGTH(N'bms_web.ExpenseRequests', N'FullyPaidAtUtc') IS NULL
    )
        THROW 62208, N'BMS W513 refused: legacy CLOSED requests need an explicit evidence migration.', 1;

    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'ClosedPartially') IS NULL
    BEGIN
        ALTER TABLE bms_web.ExpenseRequests ADD
            ClosedPartially bit NOT NULL
                CONSTRAINT DF_bms_web_ExpenseRequests_ClosedPartially DEFAULT (0) WITH VALUES;
        ALTER TABLE bms_web.ExpenseRequests
            DROP CONSTRAINT DF_bms_web_ExpenseRequests_ClosedPartially;
    END;

    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'AccrualCycleId') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD AccrualCycleId uniqueidentifier NULL;

    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'LatestClosingDocumentsReceivedDate') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD LatestClosingDocumentsReceivedDate date NULL;

    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'ConfirmedClosingControlAmount') IS NULL
    BEGIN
        ALTER TABLE bms_web.ExpenseRequests ADD
            ConfirmedClosingControlAmount decimal(18, 2) NOT NULL
                CONSTRAINT DF_bms_web_ExpenseRequests_ConfirmedClosingControlAmount DEFAULT (0) WITH VALUES;
        ALTER TABLE bms_web.ExpenseRequests
            DROP CONSTRAINT DF_bms_web_ExpenseRequests_ConfirmedClosingControlAmount;
    END;

    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'FullyPaidAtUtc') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD FullyPaidAtUtc datetimeoffset(3) NULL;

    IF COL_LENGTH(N'bms_web.ExpenseRequestLines', N'ConfirmedClosedAmount') IS NULL
    BEGIN
        ALTER TABLE bms_web.ExpenseRequestLines ADD
            ConfirmedClosedAmount decimal(18, 2) NOT NULL
                CONSTRAINT DF_bms_web_ExpenseRequestLines_ConfirmedClosedAmount DEFAULT (0) WITH VALUES;
        ALTER TABLE bms_web.ExpenseRequestLines
            DROP CONSTRAINT DF_bms_web_ExpenseRequestLines_ConfirmedClosedAmount;
    END;

    IF COL_LENGTH(N'bms_web.ExpenseRequestLines', N'ConfirmedClosedAmountKzt') IS NULL
    BEGIN
        ALTER TABLE bms_web.ExpenseRequestLines ADD
            ConfirmedClosedAmountKzt decimal(18, 2) NOT NULL
                CONSTRAINT DF_bms_web_ExpenseRequestLines_ConfirmedClosedAmountKzt DEFAULT (0) WITH VALUES;
        ALTER TABLE bms_web.ExpenseRequestLines
            DROP CONSTRAINT DF_bms_web_ExpenseRequestLines_ConfirmedClosedAmountKzt;
    END;

    IF COL_LENGTH(N'bms_web.ExpenseRequestAccrualClosingCycles', N'GrossAmount') IS NULL
        ALTER TABLE bms_web.ExpenseRequestAccrualClosingCycles ADD GrossAmount decimal(18, 2) NOT NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequestAccrualClosingCycles', N'VatAmount') IS NULL
        ALTER TABLE bms_web.ExpenseRequestAccrualClosingCycles ADD VatAmount decimal(18, 2) NOT NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequestAccrualClosingCycles', N'AmountExcludingVat') IS NULL
        ALTER TABLE bms_web.ExpenseRequestAccrualClosingCycles ADD AmountExcludingVat decimal(18, 2) NOT NULL;

    IF COL_LENGTH(N'bms_web.ExpenseRequestAccrualClosingCycleLines', N'GrossAmount') IS NULL
        ALTER TABLE bms_web.ExpenseRequestAccrualClosingCycleLines ADD GrossAmount decimal(18, 2) NOT NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequestAccrualClosingCycleLines', N'VatAmount') IS NULL
        ALTER TABLE bms_web.ExpenseRequestAccrualClosingCycleLines ADD VatAmount decimal(18, 2) NOT NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequestAccrualClosingCycleLines', N'AmountExcludingVat') IS NULL
        ALTER TABLE bms_web.ExpenseRequestAccrualClosingCycleLines ADD AmountExcludingVat decimal(18, 2) NOT NULL;

    IF OBJECT_ID(N'bms_web.CK_bms_web_ExpenseRequests_AccrualClosingState', N'C') IS NOT NULL
        ALTER TABLE bms_web.ExpenseRequests
            DROP CONSTRAINT CK_bms_web_ExpenseRequests_AccrualClosingState;
    EXEC sys.sp_executesql N'
        ALTER TABLE bms_web.ExpenseRequests WITH CHECK ADD CONSTRAINT
            CK_bms_web_ExpenseRequests_AccrualClosingState CHECK
            (
                (AccrualCycleId IS NULL OR AccrualCycleId <>
                    CONVERT(uniqueidentifier, ''00000000-0000-0000-0000-000000000000''))
                AND ConfirmedClosingControlAmount >= 0
                AND (FullyPaidAtUtc IS NULL OR DATEPART(TZOFFSET, FullyPaidAtUtc) = 0)
                AND (LatestClosingDocumentsReceivedDate IS NULL OR FullyPaidAtUtc IS NOT NULL)
                AND (ConfirmedClosingControlAmount = 0 OR LatestClosingDocumentsReceivedDate IS NOT NULL)
                AND
                (
                    ClosedPartially = 0
                    OR
                    (
                        Status = N''PAID''
                        AND FullyPaidAtUtc IS NOT NULL
                        AND LatestClosingDocumentsReceivedDate IS NOT NULL
                        AND ConfirmedClosingControlAmount > 0
                    )
                )
                AND
                (
                    Status <> N''CLOSED''
                    OR
                    (
                        ClosedPartially = 0
                        AND FullyPaidAtUtc IS NOT NULL
                        AND LatestClosingDocumentsReceivedDate IS NOT NULL
                    )
                )
            );
        ALTER TABLE bms_web.ExpenseRequests
            CHECK CONSTRAINT CK_bms_web_ExpenseRequests_AccrualClosingState;';

    IF OBJECT_ID(N'bms_web.CK_bms_web_ExpenseRequestLines_ConfirmedClosing', N'C') IS NOT NULL
        ALTER TABLE bms_web.ExpenseRequestLines
            DROP CONSTRAINT CK_bms_web_ExpenseRequestLines_ConfirmedClosing;
    EXEC sys.sp_executesql N'
        ALTER TABLE bms_web.ExpenseRequestLines WITH CHECK ADD CONSTRAINT
            CK_bms_web_ExpenseRequestLines_ConfirmedClosing CHECK
            (
                CASE
                WHEN ConfirmedClosedAmount = 0 AND ConfirmedClosedAmountKzt = 0 THEN 1
                WHEN AccrualSnapshotFormsManagerialAccrual = 1
                 AND AccrualSnapshotExpenseItemId IS NOT NULL
                 AND AmountKzt IS NOT NULL
                 AND ConfirmedClosedAmount > 0
                 AND ConfirmedClosedAmount <= Amount
                 AND ConfirmedClosedAmountKzt >= 0
                 AND ConfirmedClosedAmountKzt <= AmountKzt
                 AND (ConfirmedClosedAmount <> Amount OR ConfirmedClosedAmountKzt = AmountKzt)
                THEN 1 ELSE 0 END = 1
            );
        ALTER TABLE bms_web.ExpenseRequestLines
            CHECK CONSTRAINT CK_bms_web_ExpenseRequestLines_ConfirmedClosing;';

    ALTER TABLE bms_web.ExpenseRequestAccrualClosingCycles
        DROP CONSTRAINT CK_bms_web_ExpenseRequestAccrualClosingCycles_Amounts;
    EXEC sys.sp_executesql N'
        ALTER TABLE bms_web.ExpenseRequestAccrualClosingCycles WITH CHECK ADD CONSTRAINT
            CK_bms_web_ExpenseRequestAccrualClosingCycles_Amounts CHECK
            (
                GrossAmount >= 0 AND VatAmount >= 0 AND AmountExcludingVat >= 0
                AND VatAmount + AmountExcludingVat = GrossAmount
                AND GrossAmountKzt >= 0 AND VatAmountKzt >= 0
                AND AmountExcludingVatKzt >= 0
                AND VatAmountKzt + AmountExcludingVatKzt = GrossAmountKzt
            );
        ALTER TABLE bms_web.ExpenseRequestAccrualClosingCycles
            CHECK CONSTRAINT CK_bms_web_ExpenseRequestAccrualClosingCycles_Amounts;';

    ALTER TABLE bms_web.ExpenseRequestAccrualClosingCycleLines
        DROP CONSTRAINT CK_bms_web_ExpenseRequestAccrualClosingCycleLines_Amounts;
    EXEC sys.sp_executesql N'
        ALTER TABLE bms_web.ExpenseRequestAccrualClosingCycleLines WITH CHECK ADD CONSTRAINT
            CK_bms_web_ExpenseRequestAccrualClosingCycleLines_Amounts CHECK
            (
                GrossAmount >= 0 AND VatAmount >= 0 AND AmountExcludingVat >= 0
                AND VatAmount + AmountExcludingVat = GrossAmount
                AND GrossAmountKzt >= 0 AND VatAmountKzt >= 0
                AND AmountExcludingVatKzt >= 0
                AND VatAmountKzt + AmountExcludingVatKzt = GrossAmountKzt
            );
        ALTER TABLE bms_web.ExpenseRequestAccrualClosingCycleLines
            CHECK CONSTRAINT CK_bms_web_ExpenseRequestAccrualClosingCycleLines_Amounts;';

    IF OBJECT_ID(N'bms_web.FK_bms_web_ExpenseRequests_CurrentAccrualClosingCycle', N'F') IS NOT NULL
        ALTER TABLE bms_web.ExpenseRequests
            DROP CONSTRAINT FK_bms_web_ExpenseRequests_CurrentAccrualClosingCycle;
    EXEC sys.sp_executesql N'
        ALTER TABLE bms_web.ExpenseRequests WITH CHECK ADD CONSTRAINT
            FK_bms_web_ExpenseRequests_CurrentAccrualClosingCycle
            FOREIGN KEY (Id, AccrualCycleId)
            REFERENCES bms_web.ExpenseRequestAccrualClosingCycles (ExpenseRequestId, CycleId)
            ON DELETE NO ACTION ON UPDATE NO ACTION;
        ALTER TABLE bms_web.ExpenseRequests
            CHECK CONSTRAINT FK_bms_web_ExpenseRequests_CurrentAccrualClosingCycle;';

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'ExpenseRequests', N'ClosedPartially', N'bit', 1, 0, 1, 0),
                (N'ExpenseRequests', N'AccrualCycleId', N'uniqueidentifier', 16, 1, 0, 0),
                (N'ExpenseRequests', N'LatestClosingDocumentsReceivedDate', N'date', 3, 1, 10, 0),
                (N'ExpenseRequests', N'ConfirmedClosingControlAmount', N'decimal', 9, 0, 18, 2),
                (N'ExpenseRequests', N'FullyPaidAtUtc', N'datetimeoffset', 9, 1, 30, 3),
                (N'ExpenseRequestLines', N'ConfirmedClosedAmount', N'decimal', 9, 0, 18, 2),
                (N'ExpenseRequestLines', N'ConfirmedClosedAmountKzt', N'decimal', 9, 0, 18, 2),
                (N'ExpenseRequestAccrualClosingCycles', N'GrossAmount', N'decimal', 9, 0, 18, 2),
                (N'ExpenseRequestAccrualClosingCycles', N'VatAmount', N'decimal', 9, 0, 18, 2),
                (N'ExpenseRequestAccrualClosingCycles', N'AmountExcludingVat', N'decimal', 9, 0, 18, 2),
                (N'ExpenseRequestAccrualClosingCycleLines', N'GrossAmount', N'decimal', 9, 0, 18, 2),
                (N'ExpenseRequestAccrualClosingCycleLines', N'VatAmount', N'decimal', 9, 0, 18, 2),
                (N'ExpenseRequestAccrualClosingCycleLines', N'AmountExcludingVat', N'decimal', 9, 0, 18, 2)
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
           OBJECT_ID(N'bms_web.ExpenseRequestAccrualClosingCycles')) <> 14
       OR (SELECT COUNT(*) FROM sys.columns WHERE object_id =
           OBJECT_ID(N'bms_web.ExpenseRequestAccrualClosingCycleLines')) <> 14
        THROW 62209, N'BMS W513 refused: closing activation column contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'ExpenseRequests', N'CK_bms_web_ExpenseRequests_AccrualClosingState'),
                (N'ExpenseRequestLines', N'CK_bms_web_ExpenseRequestLines_ConfirmedClosing'),
                (N'ExpenseRequestAccrualClosingCycles', N'CK_bms_web_ExpenseRequestAccrualClosingCycles_Amounts'),
                (N'ExpenseRequestAccrualClosingCycleLines', N'CK_bms_web_ExpenseRequestAccrualClosingCycleLines_Amounts')
        ) AS expected(TableName, ConstraintName)
        LEFT JOIN sys.check_constraints AS actual
          ON actual.parent_object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
         AND actual.name = expected.ConstraintName
        WHERE actual.object_id IS NULL OR actual.is_disabled <> 0
           OR actual.is_not_trusted <> 0 OR actual.is_not_for_replication <> 0
           OR actual.is_system_named <> 0 OR actual.definition IS NULL
    )
        THROW 62210, N'BMS W513 refused: closing activation CHECK contract is drifted.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys AS actual
        WHERE actual.parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
          AND actual.name = N'FK_bms_web_ExpenseRequests_CurrentAccrualClosingCycle'
          AND actual.is_disabled = 0 AND actual.is_not_trusted = 0
          AND actual.is_not_for_replication = 0 AND actual.is_system_named = 0
          AND actual.delete_referential_action = 0
          AND actual.update_referential_action = 0
          AND (SELECT COUNT(*) FROM sys.foreign_key_columns AS columns
               WHERE columns.constraint_object_id = actual.object_id) = 2
    )
        THROW 62211, N'BMS W513 refused: current closing-cycle foreign key is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.triggers
        WHERE parent_id IN
        (
            OBJECT_ID(N'bms_web.ExpenseRequests'),
            OBJECT_ID(N'bms_web.ExpenseRequestLines'),
            OBJECT_ID(N'bms_web.ExpenseRequestAccrualClosingCycles'),
            OBJECT_ID(N'bms_web.ExpenseRequestAccrualClosingCycleLines')
        )
          AND is_ms_shipped = 0
    )
        THROW 62212, N'BMS W513 refused: closing activation tables must not have user triggers.', 1;

    IF NOT EXISTS
    (
        SELECT 1 FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 26
    )
        INSERT INTO bms_web.schema_version (version_no, script_name)
        VALUES (26, N'026_expense_request_closing_activation.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
