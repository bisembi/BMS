-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web W143 — dormant treasury payment/overpayment storage.

    IMPORTANT
    - Apply 001 through 019 first and run this script standalone.
    - This is a structure-only migration. It creates no payment fact, source,
      allocation, business movement or user-facing command.
    - Payment-event Gate G1, managed locks, exact re-read, idempotency,
      business audit and atomic request/budget/accrual writers remain required.
    - It has not been executed against an accounting server.
*/

IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS W143 refused: run 020_treasury_payment_overpayment_storage.sql outside an ambient transaction.', 16, 1);
    RETURN;
END;

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 60101, N'BMS W143 refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 60102, N'BMS W143 refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
    THROW 60103, N'BMS W143 refused: managed schema bms_web is absent.', 1;

IF OBJECT_ID(N'bms_web.Organizations', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.Counterparties', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.Currencies', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.Employees', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.ExpenseRequests', N'U') IS NULL
    THROW 60104, N'BMS W143 refused: required reference or expense-request tables are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no > 20
    )
        THROW 60105, N'BMS W143 refused: a future schema version is already registered.', 1;

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
                (19, N'019_overpayment_deficit_storage.sql')
        ) AS expected(version_no, script_name)
        INNER JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK)
          ON actual.version_no = expected.version_no
         AND actual.script_name COLLATE Latin1_General_100_BIN2 =
             expected.script_name COLLATE Latin1_General_100_BIN2
         AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
    ) <> 19
        THROW 60106, N'BMS W143 refused: exact schema versions 1 through 19 are required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 20
          AND
          (
              script_name COLLATE Latin1_General_100_BIN2 <>
                  N'020_treasury_payment_overpayment_storage.sql'
              OR DATALENGTH(script_name) <>
                  DATALENGTH(N'020_treasury_payment_overpayment_storage.sql')
          )
    )
        THROW 60107, N'BMS W143 refused: schema version 20 belongs to another script.', 1;

    DECLARE @ExistingTargetCount int =
    (
        SELECT COUNT(*)
        FROM
        (
            VALUES
                (N'bms_web.ExpenseRequestPaymentFacts'),
                (N'bms_web.OverpaymentSources'),
                (N'bms_web.OverpaymentAllocations')
        ) AS target(ObjectName)
        WHERE OBJECT_ID(target.ObjectName, N'U') IS NOT NULL
    );

    IF @ExistingTargetCount NOT IN (0, 3)
        THROW 60108, N'BMS W143 refused: treasury overpayment storage is partially present.', 1;

    IF @ExistingTargetCount = 0
    BEGIN
        CREATE TABLE bms_web.ExpenseRequestPaymentFacts
        (
            SourceSystem       nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL,
            ExternalId        nvarchar(150) COLLATE Latin1_General_100_BIN2 NOT NULL,
            EventId           uniqueidentifier NOT NULL,
            ExpenseRequestId  uniqueidentifier NOT NULL,
            CurrencyId        uniqueidentifier NOT NULL,
            Amount            decimal(18,2) NOT NULL,
            AmountKzt         decimal(18,2) NOT NULL,
            PaidAt            datetimeoffset(3) NOT NULL,
            PaymentNumber     nvarchar(100) NULL,
            IsReversal        bit NOT NULL,
            ReversedEventId   uniqueidentifier NULL,
            ProcessedAt       datetimeoffset(3) NOT NULL,
            CONSTRAINT PK_bms_web_ExpenseRequestPaymentFacts PRIMARY KEY CLUSTERED
                (SourceSystem ASC, ExternalId ASC),
            CONSTRAINT UQ_bms_web_ExpenseRequestPaymentFacts_EventId
                UNIQUE NONCLUSTERED (EventId ASC),
            CONSTRAINT FK_bms_web_ExpenseRequestPaymentFacts_ExpenseRequest
                FOREIGN KEY (ExpenseRequestId) REFERENCES bms_web.ExpenseRequests (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_ExpenseRequestPaymentFacts_Currency
                FOREIGN KEY (CurrencyId) REFERENCES bms_web.Currencies (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_ExpenseRequestPaymentFacts_ReversedEvent
                FOREIGN KEY (ReversedEventId)
                REFERENCES bms_web.ExpenseRequestPaymentFacts (EventId)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT CK_bms_web_ExpenseRequestPaymentFacts_Identifiers CHECK
            (
                LEN(LTRIM(RTRIM(SourceSystem))) > 0
                AND DATALENGTH(SourceSystem) = DATALENGTH(LTRIM(RTRIM(SourceSystem)))
                AND LEN(LTRIM(RTRIM(ExternalId))) > 0
                AND DATALENGTH(ExternalId) = DATALENGTH(LTRIM(RTRIM(ExternalId)))
            ),
            CONSTRAINT CK_bms_web_ExpenseRequestPaymentFacts_Amounts CHECK
                (Amount > 0 AND AmountKzt > 0),
            CONSTRAINT CK_bms_web_ExpenseRequestPaymentFacts_Reversal CHECK
            (
                (IsReversal = 0 AND ReversedEventId IS NULL)
                OR
                (IsReversal = 1 AND ReversedEventId IS NOT NULL
                 AND ReversedEventId <> EventId)
            )
        );

        CREATE NONCLUSTERED INDEX IX_bms_web_ExpenseRequestPaymentFacts_ExpenseRequest
            ON bms_web.ExpenseRequestPaymentFacts (ExpenseRequestId ASC);
        CREATE NONCLUSTERED INDEX IX_bms_web_ExpenseRequestPaymentFacts_ReversedEvent
            ON bms_web.ExpenseRequestPaymentFacts (ReversedEventId ASC);

        CREATE TABLE bms_web.OverpaymentSources
        (
            OrganizationId                uniqueidentifier NOT NULL,
            CounterpartyId                uniqueidentifier NOT NULL,
            CurrencyId                    uniqueidentifier NOT NULL,
            SourceId                      uniqueidentifier NOT NULL,
            OriginalAmount                decimal(18,2) NOT NULL,
            OriginalAmountKzt             decimal(18,2) NOT NULL,
            ReversedAmount                decimal(18,2) NOT NULL,
            ReversedAmountKzt             decimal(18,2) NOT NULL,
            DeficitTransferredAmount      decimal(18,2) NOT NULL,
            DeficitTransferredAmountKzt   decimal(18,2) NOT NULL,
            CurrentRemainingAmount        decimal(18,2) NOT NULL,
            CurrentRemainingAmountKzt     decimal(18,2) NOT NULL,
            ReservedAmount                decimal(18,2) NOT NULL,
            ReservedAmountKzt             decimal(18,2) NOT NULL,
            SourceExpenseRequestId        uniqueidentifier NOT NULL,
            SourceEventId                 uniqueidentifier NOT NULL,
            OccurredAt                    datetimeoffset(3) NOT NULL,
            CONSTRAINT PK_bms_web_OverpaymentSources PRIMARY KEY CLUSTERED
                (OrganizationId ASC, CounterpartyId ASC, CurrencyId ASC, SourceId ASC),
            CONSTRAINT UQ_bms_web_OverpaymentSources_SourceId
                UNIQUE NONCLUSTERED (SourceId ASC),
            CONSTRAINT UQ_bms_web_OverpaymentSources_SourceEventId
                UNIQUE NONCLUSTERED (SourceEventId ASC),
            CONSTRAINT FK_bms_web_OverpaymentSources_Organization
                FOREIGN KEY (OrganizationId) REFERENCES bms_web.Organizations (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_OverpaymentSources_Counterparty
                FOREIGN KEY (CounterpartyId) REFERENCES bms_web.Counterparties (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_OverpaymentSources_Currency
                FOREIGN KEY (CurrencyId) REFERENCES bms_web.Currencies (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_OverpaymentSources_SourceExpenseRequest
                FOREIGN KEY (SourceExpenseRequestId) REFERENCES bms_web.ExpenseRequests (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_OverpaymentSources_SourceEvent
                FOREIGN KEY (SourceEventId)
                REFERENCES bms_web.ExpenseRequestPaymentFacts (EventId)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT CK_bms_web_OverpaymentSources_Amounts CHECK
            (
                OriginalAmount > 0 AND OriginalAmountKzt > 0
                AND ReversedAmount >= 0 AND ReversedAmount <= OriginalAmount
                AND ReversedAmountKzt >= 0 AND ReversedAmountKzt <= OriginalAmountKzt
                AND DeficitTransferredAmount >= 0
                AND DeficitTransferredAmount <= ReversedAmount
                AND DeficitTransferredAmountKzt >= 0
                AND DeficitTransferredAmountKzt <= ReversedAmountKzt
                AND CurrentRemainingAmount >= 0
                AND CurrentRemainingAmount <= OriginalAmount
                AND CurrentRemainingAmountKzt >= 0
                AND CurrentRemainingAmountKzt <= OriginalAmountKzt
                AND ReservedAmount >= 0 AND ReservedAmount <= CurrentRemainingAmount
                AND ReservedAmountKzt >= 0
                AND ReservedAmountKzt <= CurrentRemainingAmountKzt
            ),
            CONSTRAINT CK_bms_web_OverpaymentSources_PairedState CHECK
            (
                ((ReversedAmount = 0 AND ReversedAmountKzt = 0)
                 OR (ReversedAmount > 0 AND ReversedAmountKzt > 0))
                AND ((DeficitTransferredAmount = 0 AND DeficitTransferredAmountKzt = 0)
                     OR (DeficitTransferredAmount > 0 AND DeficitTransferredAmountKzt > 0))
                AND ((CurrentRemainingAmount = 0 AND CurrentRemainingAmountKzt = 0)
                     OR (CurrentRemainingAmount > 0 AND CurrentRemainingAmountKzt > 0))
                AND ((ReservedAmount = 0 AND ReservedAmountKzt = 0)
                     OR (ReservedAmount > 0 AND ReservedAmountKzt > 0))
            )
        );

        CREATE NONCLUSTERED INDEX IX_bms_web_OverpaymentSources_OccurredAt
            ON bms_web.OverpaymentSources (OccurredAt ASC);
        CREATE NONCLUSTERED INDEX IX_bms_web_OverpaymentSources_SourceExpenseRequest
            ON bms_web.OverpaymentSources (SourceExpenseRequestId ASC);

        CREATE TABLE bms_web.OverpaymentAllocations
        (
            TargetExpenseRequestId  uniqueidentifier NOT NULL,
            SourceId                uniqueidentifier NOT NULL,
            AllocationId            uniqueidentifier NOT NULL,
            Amount                  decimal(18,2) NOT NULL,
            AmountKzt               decimal(18,2) NOT NULL,
            Status                  nvarchar(30) COLLATE Latin1_General_100_BIN2 NOT NULL,
            CommandId               uniqueidentifier NOT NULL,
            ReservedAt              datetimeoffset(3) NOT NULL,
            AppliedOrReleasedAt     datetimeoffset(3) NULL,
            UserId                  uniqueidentifier NOT NULL,
            CONSTRAINT PK_bms_web_OverpaymentAllocations PRIMARY KEY CLUSTERED
                (TargetExpenseRequestId ASC, SourceId ASC, AllocationId ASC),
            CONSTRAINT UQ_bms_web_OverpaymentAllocations_AllocationId
                UNIQUE NONCLUSTERED (AllocationId ASC),
            CONSTRAINT FK_bms_web_OverpaymentAllocations_TargetExpenseRequest
                FOREIGN KEY (TargetExpenseRequestId) REFERENCES bms_web.ExpenseRequests (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_OverpaymentAllocations_Source
                FOREIGN KEY (SourceId) REFERENCES bms_web.OverpaymentSources (SourceId)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_OverpaymentAllocations_User
                FOREIGN KEY (UserId) REFERENCES bms_web.Employees (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT CK_bms_web_OverpaymentAllocations_Amounts CHECK
                (Amount > 0 AND AmountKzt > 0),
            CONSTRAINT CK_bms_web_OverpaymentAllocations_State CHECK
            (
                (Status = N'Reserved' AND AppliedOrReleasedAt IS NULL)
                OR
                (Status IN (N'Applied', N'Released')
                 AND AppliedOrReleasedAt IS NOT NULL)
            )
        );

        CREATE NONCLUSTERED INDEX IX_bms_web_OverpaymentAllocations_SourceId
            ON bms_web.OverpaymentAllocations (SourceId ASC);
        CREATE NONCLUSTERED INDEX IX_bms_web_OverpaymentAllocations_Status
            ON bms_web.OverpaymentAllocations (Status ASC);
        CREATE NONCLUSTERED INDEX IX_bms_web_OverpaymentAllocations_ReservedAt
            ON bms_web.OverpaymentAllocations (ReservedAt ASC);
    END;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'ExpenseRequestPaymentFacts', 12),
                (N'OverpaymentSources', 17),
                (N'OverpaymentAllocations', 10)
        ) AS expected(TableName, ColumnCount)
        WHERE OBJECT_ID(N'bms_web.' + expected.TableName, N'U') IS NULL
           OR
           (
               SELECT COUNT(*)
               FROM sys.columns
               WHERE object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
           ) <> expected.ColumnCount
    )
        THROW 60109, N'BMS W143 refused: treasury overpayment table contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'ExpenseRequestPaymentFacts', N'SourceSystem', N'nvarchar', 0, 0, 0, 200, N'Latin1_General_100_BIN2'),
                (N'ExpenseRequestPaymentFacts', N'ExternalId', N'nvarchar', 0, 0, 0, 300, N'Latin1_General_100_BIN2'),
                (N'ExpenseRequestPaymentFacts', N'EventId', N'uniqueidentifier', 0, 0, 0, CAST(NULL AS int), CAST(NULL AS nvarchar(128))),
                (N'ExpenseRequestPaymentFacts', N'ExpenseRequestId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'ExpenseRequestPaymentFacts', N'CurrencyId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'ExpenseRequestPaymentFacts', N'Amount', N'decimal', 0, 18, 2, NULL, NULL),
                (N'ExpenseRequestPaymentFacts', N'AmountKzt', N'decimal', 0, 18, 2, NULL, NULL),
                (N'ExpenseRequestPaymentFacts', N'PaidAt', N'datetimeoffset', 0, 0, 3, NULL, NULL),
                (N'ExpenseRequestPaymentFacts', N'PaymentNumber', N'nvarchar', 1, 0, 0, 200, NULL),
                (N'ExpenseRequestPaymentFacts', N'IsReversal', N'bit', 0, 0, 0, NULL, NULL),
                (N'ExpenseRequestPaymentFacts', N'ReversedEventId', N'uniqueidentifier', 1, 0, 0, NULL, NULL),
                (N'ExpenseRequestPaymentFacts', N'ProcessedAt', N'datetimeoffset', 0, 0, 3, NULL, NULL),
                (N'OverpaymentSources', N'OrganizationId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'OverpaymentSources', N'CounterpartyId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'OverpaymentSources', N'CurrencyId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'OverpaymentSources', N'SourceId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'OverpaymentSources', N'OriginalAmount', N'decimal', 0, 18, 2, NULL, NULL),
                (N'OverpaymentSources', N'OriginalAmountKzt', N'decimal', 0, 18, 2, NULL, NULL),
                (N'OverpaymentSources', N'ReversedAmount', N'decimal', 0, 18, 2, NULL, NULL),
                (N'OverpaymentSources', N'ReversedAmountKzt', N'decimal', 0, 18, 2, NULL, NULL),
                (N'OverpaymentSources', N'DeficitTransferredAmount', N'decimal', 0, 18, 2, NULL, NULL),
                (N'OverpaymentSources', N'DeficitTransferredAmountKzt', N'decimal', 0, 18, 2, NULL, NULL),
                (N'OverpaymentSources', N'CurrentRemainingAmount', N'decimal', 0, 18, 2, NULL, NULL),
                (N'OverpaymentSources', N'CurrentRemainingAmountKzt', N'decimal', 0, 18, 2, NULL, NULL),
                (N'OverpaymentSources', N'ReservedAmount', N'decimal', 0, 18, 2, NULL, NULL),
                (N'OverpaymentSources', N'ReservedAmountKzt', N'decimal', 0, 18, 2, NULL, NULL),
                (N'OverpaymentSources', N'SourceExpenseRequestId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'OverpaymentSources', N'SourceEventId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'OverpaymentSources', N'OccurredAt', N'datetimeoffset', 0, 0, 3, NULL, NULL),
                (N'OverpaymentAllocations', N'TargetExpenseRequestId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'OverpaymentAllocations', N'SourceId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'OverpaymentAllocations', N'AllocationId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'OverpaymentAllocations', N'Amount', N'decimal', 0, 18, 2, NULL, NULL),
                (N'OverpaymentAllocations', N'AmountKzt', N'decimal', 0, 18, 2, NULL, NULL),
                (N'OverpaymentAllocations', N'Status', N'nvarchar', 0, 0, 0, 60, N'Latin1_General_100_BIN2'),
                (N'OverpaymentAllocations', N'CommandId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'OverpaymentAllocations', N'ReservedAt', N'datetimeoffset', 0, 0, 3, NULL, NULL),
                (N'OverpaymentAllocations', N'AppliedOrReleasedAt', N'datetimeoffset', 1, 0, 3, NULL, NULL),
                (N'OverpaymentAllocations', N'UserId', N'uniqueidentifier', 0, 0, 0, NULL, NULL)
        ) AS expected(TableName, ColumnName, TypeName, IsNullable, PrecisionValue, ScaleValue, MaxLengthValue, CollationName)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
         AND c.name = expected.ColumnName
        WHERE c.object_id IS NULL
           OR TYPE_NAME(c.user_type_id) <> expected.TypeName
           OR c.is_nullable <> expected.IsNullable
           OR (expected.PrecisionValue <> 0 AND c.precision <> expected.PrecisionValue)
           OR (expected.ScaleValue <> 0 AND c.scale <> expected.ScaleValue)
           OR (expected.MaxLengthValue IS NOT NULL AND c.max_length <> expected.MaxLengthValue)
           OR (expected.CollationName IS NOT NULL AND
               c.collation_name COLLATE Latin1_General_100_BIN2 <>
               expected.CollationName COLLATE Latin1_General_100_BIN2)
           OR c.is_computed <> 0 OR c.is_identity <> 0
           OR c.is_rowguidcol <> 0 OR c.is_filestream <> 0
           OR c.default_object_id <> 0 OR c.rule_object_id <> 0
           OR c.generated_always_type <> 0
           OR c.is_sparse <> 0 OR c.is_column_set <> 0
           OR c.encryption_type IS NOT NULL OR c.is_hidden <> 0 OR c.is_masked <> 0
    )
        THROW 60110, N'BMS W143 refused: treasury overpayment column contract is drifted.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.indexes
        WHERE object_id IN
        (
            OBJECT_ID(N'bms_web.ExpenseRequestPaymentFacts'),
            OBJECT_ID(N'bms_web.OverpaymentSources'),
            OBJECT_ID(N'bms_web.OverpaymentAllocations')
        )
          AND index_id > 0 AND is_hypothetical = 0
    ) <> 14
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (N'ExpenseRequestPaymentFacts', N'PK_bms_web_ExpenseRequestPaymentFacts', 1, 1),
                   (N'ExpenseRequestPaymentFacts', N'UQ_bms_web_ExpenseRequestPaymentFacts_EventId', 1, 0),
                   (N'ExpenseRequestPaymentFacts', N'IX_bms_web_ExpenseRequestPaymentFacts_ExpenseRequest', 0, 0),
                   (N'ExpenseRequestPaymentFacts', N'IX_bms_web_ExpenseRequestPaymentFacts_ReversedEvent', 0, 0),
                   (N'OverpaymentSources', N'PK_bms_web_OverpaymentSources', 1, 1),
                   (N'OverpaymentSources', N'UQ_bms_web_OverpaymentSources_SourceId', 1, 0),
                   (N'OverpaymentSources', N'UQ_bms_web_OverpaymentSources_SourceEventId', 1, 0),
                   (N'OverpaymentSources', N'IX_bms_web_OverpaymentSources_OccurredAt', 0, 0),
                   (N'OverpaymentSources', N'IX_bms_web_OverpaymentSources_SourceExpenseRequest', 0, 0),
                   (N'OverpaymentAllocations', N'PK_bms_web_OverpaymentAllocations', 1, 1),
                   (N'OverpaymentAllocations', N'UQ_bms_web_OverpaymentAllocations_AllocationId', 1, 0),
                   (N'OverpaymentAllocations', N'IX_bms_web_OverpaymentAllocations_SourceId', 0, 0),
                   (N'OverpaymentAllocations', N'IX_bms_web_OverpaymentAllocations_Status', 0, 0),
                   (N'OverpaymentAllocations', N'IX_bms_web_OverpaymentAllocations_ReservedAt', 0, 0)
           ) AS expected(TableName, IndexName, IsUnique, IsPrimaryKey)
           LEFT JOIN sys.indexes AS i
             ON i.object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
            AND i.name = expected.IndexName
           WHERE i.object_id IS NULL OR i.is_unique <> expected.IsUnique
              OR i.is_primary_key <> expected.IsPrimaryKey
              OR i.is_disabled <> 0 OR i.is_hypothetical <> 0
              OR i.filter_definition IS NOT NULL
       )
        THROW 60111, N'BMS W143 refused: treasury overpayment index contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'ExpenseRequestPaymentFacts', N'PK_bms_web_ExpenseRequestPaymentFacts', 1, 2, N'SourceSystem'),
                (N'ExpenseRequestPaymentFacts', N'PK_bms_web_ExpenseRequestPaymentFacts', 2, 2, N'ExternalId'),
                (N'ExpenseRequestPaymentFacts', N'UQ_bms_web_ExpenseRequestPaymentFacts_EventId', 1, 1, N'EventId'),
                (N'ExpenseRequestPaymentFacts', N'IX_bms_web_ExpenseRequestPaymentFacts_ExpenseRequest', 1, 1, N'ExpenseRequestId'),
                (N'ExpenseRequestPaymentFacts', N'IX_bms_web_ExpenseRequestPaymentFacts_ReversedEvent', 1, 1, N'ReversedEventId'),
                (N'OverpaymentSources', N'PK_bms_web_OverpaymentSources', 1, 4, N'OrganizationId'),
                (N'OverpaymentSources', N'PK_bms_web_OverpaymentSources', 2, 4, N'CounterpartyId'),
                (N'OverpaymentSources', N'PK_bms_web_OverpaymentSources', 3, 4, N'CurrencyId'),
                (N'OverpaymentSources', N'PK_bms_web_OverpaymentSources', 4, 4, N'SourceId'),
                (N'OverpaymentSources', N'UQ_bms_web_OverpaymentSources_SourceId', 1, 1, N'SourceId'),
                (N'OverpaymentSources', N'UQ_bms_web_OverpaymentSources_SourceEventId', 1, 1, N'SourceEventId'),
                (N'OverpaymentSources', N'IX_bms_web_OverpaymentSources_OccurredAt', 1, 1, N'OccurredAt'),
                (N'OverpaymentSources', N'IX_bms_web_OverpaymentSources_SourceExpenseRequest', 1, 1, N'SourceExpenseRequestId'),
                (N'OverpaymentAllocations', N'PK_bms_web_OverpaymentAllocations', 1, 3, N'TargetExpenseRequestId'),
                (N'OverpaymentAllocations', N'PK_bms_web_OverpaymentAllocations', 2, 3, N'SourceId'),
                (N'OverpaymentAllocations', N'PK_bms_web_OverpaymentAllocations', 3, 3, N'AllocationId'),
                (N'OverpaymentAllocations', N'UQ_bms_web_OverpaymentAllocations_AllocationId', 1, 1, N'AllocationId'),
                (N'OverpaymentAllocations', N'IX_bms_web_OverpaymentAllocations_SourceId', 1, 1, N'SourceId'),
                (N'OverpaymentAllocations', N'IX_bms_web_OverpaymentAllocations_Status', 1, 1, N'Status'),
                (N'OverpaymentAllocations', N'IX_bms_web_OverpaymentAllocations_ReservedAt', 1, 1, N'ReservedAt')
        ) AS expected(TableName, IndexName, KeyOrdinal, KeyCount, ColumnName)
        LEFT JOIN sys.indexes AS i
          ON i.object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
         AND i.name = expected.IndexName
        LEFT JOIN sys.index_columns AS ic
          ON ic.object_id = i.object_id AND ic.index_id = i.index_id
         AND ic.key_ordinal = expected.KeyOrdinal
        LEFT JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE c.name IS NULL OR c.name <> expected.ColumnName
           OR ic.is_descending_key <> 0 OR ic.is_included_column <> 0
           OR
           (
               SELECT COUNT(*)
               FROM sys.index_columns AS exact_ic
               WHERE exact_ic.object_id = i.object_id
                 AND exact_ic.index_id = i.index_id
                 AND exact_ic.key_ordinal > 0
           ) <> expected.KeyCount
    )
       OR EXISTS
       (
           SELECT 1
           FROM sys.index_columns AS included
           WHERE included.object_id IN
           (
               OBJECT_ID(N'bms_web.ExpenseRequestPaymentFacts'),
               OBJECT_ID(N'bms_web.OverpaymentSources'),
               OBJECT_ID(N'bms_web.OverpaymentAllocations')
           )
             AND included.is_included_column <> 0
       )
        THROW 60112, N'BMS W143 refused: treasury overpayment index keys are drifted.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.check_constraints
        WHERE parent_object_id IN
        (
            OBJECT_ID(N'bms_web.ExpenseRequestPaymentFacts'),
            OBJECT_ID(N'bms_web.OverpaymentSources'),
            OBJECT_ID(N'bms_web.OverpaymentAllocations')
        )
    ) <> 7
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (N'ExpenseRequestPaymentFacts', N'CK_bms_web_ExpenseRequestPaymentFacts_Identifiers'),
                   (N'ExpenseRequestPaymentFacts', N'CK_bms_web_ExpenseRequestPaymentFacts_Amounts'),
                   (N'ExpenseRequestPaymentFacts', N'CK_bms_web_ExpenseRequestPaymentFacts_Reversal'),
                   (N'OverpaymentSources', N'CK_bms_web_OverpaymentSources_Amounts'),
                   (N'OverpaymentSources', N'CK_bms_web_OverpaymentSources_PairedState'),
                   (N'OverpaymentAllocations', N'CK_bms_web_OverpaymentAllocations_Amounts'),
                   (N'OverpaymentAllocations', N'CK_bms_web_OverpaymentAllocations_State')
           ) AS expected(TableName, ConstraintName)
           LEFT JOIN sys.check_constraints AS cc
             ON cc.parent_object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
            AND cc.name = expected.ConstraintName
           WHERE cc.object_id IS NULL OR cc.is_disabled <> 0 OR cc.is_not_trusted <> 0
              OR cc.is_not_for_replication <> 0 OR cc.is_system_named <> 0
       )
        THROW 60113, N'BMS W143 refused: treasury overpayment CHECK contract is drifted.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.foreign_keys
        WHERE parent_object_id IN
        (
            OBJECT_ID(N'bms_web.ExpenseRequestPaymentFacts'),
            OBJECT_ID(N'bms_web.OverpaymentSources'),
            OBJECT_ID(N'bms_web.OverpaymentAllocations')
        )
    ) <> 11
       OR
       (
           SELECT COUNT(*)
           FROM sys.foreign_key_columns AS fkc
           WHERE fkc.parent_object_id IN
           (
               OBJECT_ID(N'bms_web.ExpenseRequestPaymentFacts'),
               OBJECT_ID(N'bms_web.OverpaymentSources'),
               OBJECT_ID(N'bms_web.OverpaymentAllocations')
           )
       ) <> 11
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (N'ExpenseRequestPaymentFacts', N'FK_bms_web_ExpenseRequestPaymentFacts_ExpenseRequest', N'ExpenseRequestId', N'ExpenseRequests', N'Id'),
                   (N'ExpenseRequestPaymentFacts', N'FK_bms_web_ExpenseRequestPaymentFacts_Currency', N'CurrencyId', N'Currencies', N'Id'),
                   (N'ExpenseRequestPaymentFacts', N'FK_bms_web_ExpenseRequestPaymentFacts_ReversedEvent', N'ReversedEventId', N'ExpenseRequestPaymentFacts', N'EventId'),
                   (N'OverpaymentSources', N'FK_bms_web_OverpaymentSources_Organization', N'OrganizationId', N'Organizations', N'Id'),
                   (N'OverpaymentSources', N'FK_bms_web_OverpaymentSources_Counterparty', N'CounterpartyId', N'Counterparties', N'Id'),
                   (N'OverpaymentSources', N'FK_bms_web_OverpaymentSources_Currency', N'CurrencyId', N'Currencies', N'Id'),
                   (N'OverpaymentSources', N'FK_bms_web_OverpaymentSources_SourceExpenseRequest', N'SourceExpenseRequestId', N'ExpenseRequests', N'Id'),
                   (N'OverpaymentSources', N'FK_bms_web_OverpaymentSources_SourceEvent', N'SourceEventId', N'ExpenseRequestPaymentFacts', N'EventId'),
                   (N'OverpaymentAllocations', N'FK_bms_web_OverpaymentAllocations_TargetExpenseRequest', N'TargetExpenseRequestId', N'ExpenseRequests', N'Id'),
                   (N'OverpaymentAllocations', N'FK_bms_web_OverpaymentAllocations_Source', N'SourceId', N'OverpaymentSources', N'SourceId'),
                   (N'OverpaymentAllocations', N'FK_bms_web_OverpaymentAllocations_User', N'UserId', N'Employees', N'Id')
           ) AS expected(ParentTable, ConstraintName, ParentColumn, ReferencedTable, ReferencedColumn)
           LEFT JOIN sys.foreign_keys AS fk
             ON fk.parent_object_id = OBJECT_ID(N'bms_web.' + expected.ParentTable)
            AND fk.name = expected.ConstraintName
           LEFT JOIN sys.foreign_key_columns AS fkc
             ON fkc.constraint_object_id = fk.object_id
            AND fkc.constraint_column_id = 1
           LEFT JOIN sys.columns AS parent_column
             ON parent_column.object_id = fkc.parent_object_id
            AND parent_column.column_id = fkc.parent_column_id
           LEFT JOIN sys.columns AS referenced_column
             ON referenced_column.object_id = fkc.referenced_object_id
            AND referenced_column.column_id = fkc.referenced_column_id
           WHERE fk.object_id IS NULL OR fk.is_disabled <> 0 OR fk.is_not_trusted <> 0
              OR fk.is_not_for_replication <> 0 OR fk.is_system_named <> 0
              OR fk.delete_referential_action <> 0 OR fk.update_referential_action <> 0
              OR parent_column.name IS NULL OR parent_column.name <> expected.ParentColumn
              OR fkc.referenced_object_id <> OBJECT_ID(N'bms_web.' + expected.ReferencedTable)
              OR referenced_column.name IS NULL
              OR referenced_column.name <> expected.ReferencedColumn
       )
        THROW 60114, N'BMS W143 refused: treasury overpayment foreign-key contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.triggers
        WHERE parent_id IN
        (
            OBJECT_ID(N'bms_web.ExpenseRequestPaymentFacts'),
            OBJECT_ID(N'bms_web.OverpaymentSources'),
            OBJECT_ID(N'bms_web.OverpaymentAllocations')
        )
          AND is_ms_shipped = 0
    )
        THROW 60115, N'BMS W143 refused: treasury overpayment tables have user DML triggers.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 20
    )
       AND @ExistingTargetCount <> 3
        THROW 60116, N'BMS W143 refused: version 20 is registered without its physical contract.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 20
    )
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (20, N'020_treasury_payment_overpayment_storage.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
