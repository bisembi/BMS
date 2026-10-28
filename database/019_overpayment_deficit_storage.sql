-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web W142 — dormant physical storage for overpayment deficits.

    IMPORTANT
    - Apply 001 through 018 first and run this script standalone.
    - This is a structure-only migration. It creates no business row and does
      not activate a writer, repository, API, UI command or direct user access.
    - A later audited server command must own locks, exact paired-money
      reconciliation, idempotency and all state transitions.
    - It has not been executed against an accounting server.
*/

IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS W142 refused: run 019_overpayment_deficit_storage.sql outside an ambient transaction.', 16, 1);
    RETURN;
END;

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 60001, N'BMS W142 refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 60002, N'BMS W142 refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
    THROW 60003, N'BMS W142 refused: managed schema bms_web is absent.', 1;

IF OBJECT_ID(N'bms_web.Organizations', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.Counterparties', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.Currencies', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.Employees', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.ExpenseRequests', N'U') IS NULL
    THROW 60004, N'BMS W142 refused: required reference or expense-request tables are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no > 19
    )
        THROW 60005, N'BMS W142 refused: a future schema version is already registered.', 1;

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
                (18, N'018_expense_request_line_currency_conversion.sql')
        ) AS expected(version_no, script_name)
        INNER JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK)
          ON actual.version_no = expected.version_no
         AND actual.script_name COLLATE Latin1_General_100_BIN2 =
             expected.script_name COLLATE Latin1_General_100_BIN2
         AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
    ) <> 18
        THROW 60006, N'BMS W142 refused: exact schema versions 1 through 18 are required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 19
          AND
          (
              script_name COLLATE Latin1_General_100_BIN2 <>
                  N'019_overpayment_deficit_storage.sql'
              OR DATALENGTH(script_name) <>
                  DATALENGTH(N'019_overpayment_deficit_storage.sql')
          )
    )
        THROW 60007, N'BMS W142 refused: schema version 19 belongs to another script.', 1;

    DECLARE @ExistingTargetCount int =
    (
        SELECT COUNT(*)
        FROM
        (
            VALUES
                (N'bms_web.OverpaymentDeficits'),
                (N'bms_web.ManualOverpaymentDeficitCorrections'),
                (N'bms_web.OverpaymentDeficitSettlements')
        ) AS target(ObjectName)
        WHERE OBJECT_ID(target.ObjectName, N'U') IS NOT NULL
    );

    IF @ExistingTargetCount NOT IN (0, 3)
        THROW 60008, N'BMS W142 refused: overpayment-deficit storage is partially present.', 1;

    IF @ExistingTargetCount = 0
    BEGIN
        CREATE TABLE bms_web.OverpaymentDeficits
        (
            OrganizationId              uniqueidentifier NOT NULL,
            CounterpartyId              uniqueidentifier NOT NULL,
            CurrencyId                  uniqueidentifier NOT NULL,
            DeficitId                   uniqueidentifier NOT NULL,
            OriginalAmount              decimal(18,2) NOT NULL,
            OriginalAmountKzt           decimal(18,2) NOT NULL,
            CurrentAmount               decimal(18,2) NOT NULL,
            CurrentAmountKzt            decimal(18,2) NOT NULL,
            SourceOverpaymentId         uniqueidentifier NOT NULL,
            ExternalReversalId          nvarchar(150) COLLATE Latin1_General_100_BIN2 NOT NULL,
            OccurredAt                  datetimeoffset(3) NOT NULL,
            SourceExpenseRequestId      uniqueidentifier NULL,
            CreationCommandId           uniqueidentifier NOT NULL,
            ActualCorrectionExecutorId  uniqueidentifier NULL,
            SettledAt                   datetimeoffset(3) NULL,
            CONSTRAINT PK_bms_web_OverpaymentDeficits PRIMARY KEY CLUSTERED
                (OrganizationId ASC, CounterpartyId ASC, CurrencyId ASC, DeficitId ASC),
            CONSTRAINT UQ_bms_web_OverpaymentDeficits_DeficitId UNIQUE NONCLUSTERED
                (DeficitId ASC),
            CONSTRAINT UQ_bms_web_OverpaymentDeficits_CreationCommandId UNIQUE NONCLUSTERED
                (CreationCommandId ASC),
            CONSTRAINT FK_bms_web_OverpaymentDeficits_Organization
                FOREIGN KEY (OrganizationId) REFERENCES bms_web.Organizations (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_OverpaymentDeficits_Counterparty
                FOREIGN KEY (CounterpartyId) REFERENCES bms_web.Counterparties (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_OverpaymentDeficits_Currency
                FOREIGN KEY (CurrencyId) REFERENCES bms_web.Currencies (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_OverpaymentDeficits_SourceExpenseRequest
                FOREIGN KEY (SourceExpenseRequestId) REFERENCES bms_web.ExpenseRequests (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_OverpaymentDeficits_ActualCorrectionExecutor
                FOREIGN KEY (ActualCorrectionExecutorId) REFERENCES bms_web.Employees (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT CK_bms_web_OverpaymentDeficits_Amounts CHECK
            (
                OriginalAmount > 0 AND OriginalAmountKzt > 0
                AND CurrentAmount >= 0 AND CurrentAmount <= OriginalAmount
                AND CurrentAmountKzt >= 0 AND CurrentAmountKzt <= OriginalAmountKzt
            ),
            CONSTRAINT CK_bms_web_OverpaymentDeficits_PairedState CHECK
            (
                (CurrentAmount = 0 AND CurrentAmountKzt = 0 AND SettledAt IS NOT NULL)
                OR (CurrentAmount > 0 AND CurrentAmountKzt > 0 AND SettledAt IS NULL)
            ),
            CONSTRAINT CK_bms_web_OverpaymentDeficits_ExternalReversalId CHECK
            (
                LEN(LTRIM(RTRIM(ExternalReversalId))) > 0
                AND DATALENGTH(ExternalReversalId) =
                    DATALENGTH(LTRIM(RTRIM(ExternalReversalId)))
            )
        );

        CREATE NONCLUSTERED INDEX IX_bms_web_OverpaymentDeficits_OccurredAt
            ON bms_web.OverpaymentDeficits (OccurredAt ASC);
        CREATE NONCLUSTERED INDEX IX_bms_web_OverpaymentDeficits_SourceOverpaymentId
            ON bms_web.OverpaymentDeficits (SourceOverpaymentId ASC);
        CREATE NONCLUSTERED INDEX IX_bms_web_OverpaymentDeficits_ExternalReversalId
            ON bms_web.OverpaymentDeficits (ExternalReversalId ASC);
        CREATE NONCLUSTERED INDEX IX_bms_web_OverpaymentDeficits_Counterparty
            ON bms_web.OverpaymentDeficits (CounterpartyId ASC);
        CREATE NONCLUSTERED INDEX IX_bms_web_OverpaymentDeficits_Currency
            ON bms_web.OverpaymentDeficits (CurrencyId ASC);
        CREATE NONCLUSTERED INDEX IX_bms_web_OverpaymentDeficits_SourceExpenseRequest
            ON bms_web.OverpaymentDeficits (SourceExpenseRequestId ASC);
        CREATE NONCLUSTERED INDEX IX_bms_web_OverpaymentDeficits_ActualCorrectionExecutor
            ON bms_web.OverpaymentDeficits (ActualCorrectionExecutorId ASC);

        CREATE TABLE bms_web.ManualOverpaymentDeficitCorrections
        (
            CorrectionId                uniqueidentifier NOT NULL,
            DeficitId                   uniqueidentifier NOT NULL,
            OrganizationId              uniqueidentifier NOT NULL,
            CounterpartyId              uniqueidentifier NOT NULL,
            CurrencyId                  uniqueidentifier NOT NULL,
            DeficitAmountAtCreation     decimal(18,2) NOT NULL,
            DeficitAmountKztAtCreation  decimal(18,2) NOT NULL,
            CorrectionAmount            decimal(18,2) NOT NULL,
            CorrectionAmountKzt         decimal(18,2) NOT NULL,
            Basis                       nvarchar(1000) NOT NULL,
            ResponsibleAccountantId     uniqueidentifier NOT NULL,
            FinancialDirectorId         uniqueidentifier NULL,
            State                       nvarchar(30) COLLATE Latin1_General_100_BIN2 NOT NULL,
            DecidedAt                   datetimeoffset(3) NULL,
            DecisionComment             nvarchar(1000) NULL,
            RejectionReason             nvarchar(1000) NULL,
            CreationCommandId           uniqueidentifier NOT NULL,
            DecisionCommandId           uniqueidentifier NULL,
            CONSTRAINT PK_bms_web_ManualOverpaymentDeficitCorrections
                PRIMARY KEY CLUSTERED (CorrectionId ASC),
            CONSTRAINT UQ_bms_web_ManualOverpaymentDeficitCorrections_CreationCommandId
                UNIQUE NONCLUSTERED (CreationCommandId ASC),
            CONSTRAINT FK_bms_web_ManualOverpaymentDeficitCorrections_DeficitScope
                FOREIGN KEY (OrganizationId, CounterpartyId, CurrencyId, DeficitId)
                REFERENCES bms_web.OverpaymentDeficits
                    (OrganizationId, CounterpartyId, CurrencyId, DeficitId)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_ManualOverpaymentDeficitCorrections_ResponsibleAccountant
                FOREIGN KEY (ResponsibleAccountantId) REFERENCES bms_web.Employees (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_ManualOverpaymentDeficitCorrections_FinancialDirector
                FOREIGN KEY (FinancialDirectorId) REFERENCES bms_web.Employees (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT CK_bms_web_ManualOverpaymentDeficitCorrections_Amounts CHECK
            (
                DeficitAmountAtCreation > 0 AND DeficitAmountKztAtCreation > 0
                AND CorrectionAmount > 0
                AND CorrectionAmount <= DeficitAmountAtCreation
                AND CorrectionAmountKzt > 0
                AND CorrectionAmountKzt <= DeficitAmountKztAtCreation
                AND
                (
                    (DeficitAmountAtCreation - CorrectionAmount = 0
                     AND DeficitAmountKztAtCreation - CorrectionAmountKzt = 0)
                    OR
                    (DeficitAmountAtCreation - CorrectionAmount > 0
                     AND DeficitAmountKztAtCreation - CorrectionAmountKzt > 0)
                )
            ),
            CONSTRAINT CK_bms_web_ManualOverpaymentDeficitCorrections_Basis CHECK
            (
                LEN(LTRIM(RTRIM(Basis))) > 0
            ),
            CONSTRAINT CK_bms_web_ManualOverpaymentDeficitCorrections_State CHECK
            (
                (State = N'PendingApproval'
                 AND FinancialDirectorId IS NULL AND DecidedAt IS NULL
                 AND DecisionCommandId IS NULL AND DecisionComment IS NULL
                 AND RejectionReason IS NULL)
                OR
                (State = N'ApprovedAndApplied'
                 AND FinancialDirectorId IS NOT NULL AND DecidedAt IS NOT NULL
                 AND DecisionCommandId IS NOT NULL AND RejectionReason IS NULL)
                OR
                (State = N'Rejected'
                 AND FinancialDirectorId IS NOT NULL AND DecidedAt IS NOT NULL
                 AND DecisionCommandId IS NOT NULL
                 AND LEN(LTRIM(RTRIM(RejectionReason))) > 0)
                OR
                (State = N'Annulled'
                 AND FinancialDirectorId IS NOT NULL AND DecidedAt IS NOT NULL
                 AND DecisionCommandId IS NOT NULL AND RejectionReason IS NULL)
            )
        );

        CREATE UNIQUE NONCLUSTERED INDEX
            UX_bms_web_ManualOverpaymentDeficitCorrections_PendingDeficit
            ON bms_web.ManualOverpaymentDeficitCorrections (DeficitId ASC)
            WHERE State = N'PendingApproval';
        CREATE NONCLUSTERED INDEX
            IX_bms_web_ManualOverpaymentDeficitCorrections_DeficitScope
            ON bms_web.ManualOverpaymentDeficitCorrections
                (OrganizationId ASC, CounterpartyId ASC, CurrencyId ASC, DeficitId ASC);
        CREATE NONCLUSTERED INDEX
            IX_bms_web_ManualOverpaymentDeficitCorrections_ResponsibleAccountant
            ON bms_web.ManualOverpaymentDeficitCorrections (ResponsibleAccountantId ASC);
        CREATE NONCLUSTERED INDEX
            IX_bms_web_ManualOverpaymentDeficitCorrections_FinancialDirector
            ON bms_web.ManualOverpaymentDeficitCorrections (FinancialDirectorId ASC);

        CREATE TABLE bms_web.OverpaymentDeficitSettlements
        (
            DeficitId              uniqueidentifier NOT NULL,
            CommandId              uniqueidentifier NOT NULL,
            Kind                   nvarchar(40) COLLATE Latin1_General_100_BIN2 NOT NULL,
            SourceReference        nvarchar(150) COLLATE Latin1_General_100_BIN2 NULL,
            SourceExpenseRequestId uniqueidentifier NULL,
            CorrectionId           uniqueidentifier NULL,
            Amount                 decimal(18,2) NOT NULL,
            AmountKzt              decimal(18,2) NOT NULL,
            AppliedAt              datetimeoffset(3) NOT NULL,
            UserId                 uniqueidentifier NOT NULL,
            CONSTRAINT PK_bms_web_OverpaymentDeficitSettlements PRIMARY KEY CLUSTERED
                (DeficitId ASC, CommandId ASC),
            CONSTRAINT FK_bms_web_OverpaymentDeficitSettlements_Deficit
                FOREIGN KEY (DeficitId) REFERENCES bms_web.OverpaymentDeficits (DeficitId)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_OverpaymentDeficitSettlements_SourceExpenseRequest
                FOREIGN KEY (SourceExpenseRequestId) REFERENCES bms_web.ExpenseRequests (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_OverpaymentDeficitSettlements_Correction
                FOREIGN KEY (CorrectionId)
                REFERENCES bms_web.ManualOverpaymentDeficitCorrections (CorrectionId)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_OverpaymentDeficitSettlements_User
                FOREIGN KEY (UserId) REFERENCES bms_web.Employees (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT CK_bms_web_OverpaymentDeficitSettlements_Amounts CHECK
                (Amount > 0 AND AmountKzt > 0),
            CONSTRAINT CK_bms_web_OverpaymentDeficitSettlements_Source CHECK
            (
                (Kind IN
                    (N'NewOverpayment', N'AdditionalPayment', N'RestoredReversedPayment')
                 AND SourceReference IS NOT NULL
                 AND LEN(LTRIM(RTRIM(SourceReference))) > 0
                 AND DATALENGTH(SourceReference) =
                     DATALENGTH(LTRIM(RTRIM(SourceReference)))
                 AND CorrectionId IS NULL)
                OR
                (Kind = N'ManualCorrection' AND SourceReference IS NULL
                 AND SourceExpenseRequestId IS NULL AND CorrectionId IS NOT NULL)
                OR
                (Kind = N'ReconsideredPriorRequest' AND SourceReference IS NULL
                 AND SourceExpenseRequestId IS NOT NULL AND CorrectionId IS NULL)
            )
        );

        CREATE NONCLUSTERED INDEX
            IX_bms_web_OverpaymentDeficitSettlements_SourceExpenseRequest
            ON bms_web.OverpaymentDeficitSettlements (SourceExpenseRequestId ASC);
        CREATE UNIQUE NONCLUSTERED INDEX
            UX_bms_web_OverpaymentDeficitSettlements_Correction
            ON bms_web.OverpaymentDeficitSettlements (CorrectionId ASC)
            WHERE CorrectionId IS NOT NULL;
        CREATE NONCLUSTERED INDEX IX_bms_web_OverpaymentDeficitSettlements_AppliedAt
            ON bms_web.OverpaymentDeficitSettlements (AppliedAt ASC);
        CREATE NONCLUSTERED INDEX IX_bms_web_OverpaymentDeficitSettlements_User
            ON bms_web.OverpaymentDeficitSettlements (UserId ASC);
    END;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'OverpaymentDeficits', 15),
                (N'ManualOverpaymentDeficitCorrections', 18),
                (N'OverpaymentDeficitSettlements', 10)
        ) AS expected(TableName, ColumnCount)
        WHERE OBJECT_ID(N'bms_web.' + expected.TableName, N'U') IS NULL
           OR
           (
               SELECT COUNT(*)
               FROM sys.columns
               WHERE object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
           ) <> expected.ColumnCount
    )
        THROW 60009, N'BMS W142 refused: table or exact column-count contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'OverpaymentDeficits', N'OrganizationId', N'uniqueidentifier', 0, 0, 0, CAST(NULL AS int), CAST(NULL AS nvarchar(128))),
                (N'OverpaymentDeficits', N'CounterpartyId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'OverpaymentDeficits', N'CurrencyId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'OverpaymentDeficits', N'DeficitId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'OverpaymentDeficits', N'OriginalAmount', N'decimal', 0, 18, 2, NULL, NULL),
                (N'OverpaymentDeficits', N'OriginalAmountKzt', N'decimal', 0, 18, 2, NULL, NULL),
                (N'OverpaymentDeficits', N'CurrentAmount', N'decimal', 0, 18, 2, NULL, NULL),
                (N'OverpaymentDeficits', N'CurrentAmountKzt', N'decimal', 0, 18, 2, NULL, NULL),
                (N'OverpaymentDeficits', N'SourceOverpaymentId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'OverpaymentDeficits', N'ExternalReversalId', N'nvarchar', 0, 0, 0, 300, N'Latin1_General_100_BIN2'),
                (N'OverpaymentDeficits', N'OccurredAt', N'datetimeoffset', 0, 0, 3, NULL, NULL),
                (N'OverpaymentDeficits', N'SourceExpenseRequestId', N'uniqueidentifier', 1, 0, 0, NULL, NULL),
                (N'OverpaymentDeficits', N'CreationCommandId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'OverpaymentDeficits', N'ActualCorrectionExecutorId', N'uniqueidentifier', 1, 0, 0, NULL, NULL),
                (N'OverpaymentDeficits', N'SettledAt', N'datetimeoffset', 1, 0, 3, NULL, NULL),
                (N'ManualOverpaymentDeficitCorrections', N'CorrectionId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'ManualOverpaymentDeficitCorrections', N'DeficitId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'ManualOverpaymentDeficitCorrections', N'OrganizationId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'ManualOverpaymentDeficitCorrections', N'CounterpartyId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'ManualOverpaymentDeficitCorrections', N'CurrencyId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'ManualOverpaymentDeficitCorrections', N'DeficitAmountAtCreation', N'decimal', 0, 18, 2, NULL, NULL),
                (N'ManualOverpaymentDeficitCorrections', N'DeficitAmountKztAtCreation', N'decimal', 0, 18, 2, NULL, NULL),
                (N'ManualOverpaymentDeficitCorrections', N'CorrectionAmount', N'decimal', 0, 18, 2, NULL, NULL),
                (N'ManualOverpaymentDeficitCorrections', N'CorrectionAmountKzt', N'decimal', 0, 18, 2, NULL, NULL),
                (N'ManualOverpaymentDeficitCorrections', N'Basis', N'nvarchar', 0, 0, 0, 2000, NULL),
                (N'ManualOverpaymentDeficitCorrections', N'ResponsibleAccountantId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'ManualOverpaymentDeficitCorrections', N'FinancialDirectorId', N'uniqueidentifier', 1, 0, 0, NULL, NULL),
                (N'ManualOverpaymentDeficitCorrections', N'State', N'nvarchar', 0, 0, 0, 60, N'Latin1_General_100_BIN2'),
                (N'ManualOverpaymentDeficitCorrections', N'DecidedAt', N'datetimeoffset', 1, 0, 3, NULL, NULL),
                (N'ManualOverpaymentDeficitCorrections', N'DecisionComment', N'nvarchar', 1, 0, 0, 2000, NULL),
                (N'ManualOverpaymentDeficitCorrections', N'RejectionReason', N'nvarchar', 1, 0, 0, 2000, NULL),
                (N'ManualOverpaymentDeficitCorrections', N'CreationCommandId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'ManualOverpaymentDeficitCorrections', N'DecisionCommandId', N'uniqueidentifier', 1, 0, 0, NULL, NULL),
                (N'OverpaymentDeficitSettlements', N'DeficitId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'OverpaymentDeficitSettlements', N'CommandId', N'uniqueidentifier', 0, 0, 0, NULL, NULL),
                (N'OverpaymentDeficitSettlements', N'Kind', N'nvarchar', 0, 0, 0, 80, N'Latin1_General_100_BIN2'),
                (N'OverpaymentDeficitSettlements', N'SourceReference', N'nvarchar', 1, 0, 0, 300, N'Latin1_General_100_BIN2'),
                (N'OverpaymentDeficitSettlements', N'SourceExpenseRequestId', N'uniqueidentifier', 1, 0, 0, NULL, NULL),
                (N'OverpaymentDeficitSettlements', N'CorrectionId', N'uniqueidentifier', 1, 0, 0, NULL, NULL),
                (N'OverpaymentDeficitSettlements', N'Amount', N'decimal', 0, 18, 2, NULL, NULL),
                (N'OverpaymentDeficitSettlements', N'AmountKzt', N'decimal', 0, 18, 2, NULL, NULL),
                (N'OverpaymentDeficitSettlements', N'AppliedAt', N'datetimeoffset', 0, 0, 3, NULL, NULL),
                (N'OverpaymentDeficitSettlements', N'UserId', N'uniqueidentifier', 0, 0, 0, NULL, NULL)
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
        THROW 60010, N'BMS W142 refused: overpayment-deficit column contract is drifted.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.indexes
        WHERE object_id IN
        (
            OBJECT_ID(N'bms_web.OverpaymentDeficits'),
            OBJECT_ID(N'bms_web.ManualOverpaymentDeficitCorrections'),
            OBJECT_ID(N'bms_web.OverpaymentDeficitSettlements')
        )
          AND index_id > 0 AND is_hypothetical = 0
    ) <> 21
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (N'OverpaymentDeficits', N'PK_bms_web_OverpaymentDeficits', 1, 1, CAST(NULL AS nvarchar(1000))),
                   (N'OverpaymentDeficits', N'UQ_bms_web_OverpaymentDeficits_DeficitId', 1, 0, NULL),
                   (N'OverpaymentDeficits', N'UQ_bms_web_OverpaymentDeficits_CreationCommandId', 1, 0, NULL),
                   (N'OverpaymentDeficits', N'IX_bms_web_OverpaymentDeficits_OccurredAt', 0, 0, NULL),
                   (N'OverpaymentDeficits', N'IX_bms_web_OverpaymentDeficits_SourceOverpaymentId', 0, 0, NULL),
                   (N'OverpaymentDeficits', N'IX_bms_web_OverpaymentDeficits_ExternalReversalId', 0, 0, NULL),
                   (N'OverpaymentDeficits', N'IX_bms_web_OverpaymentDeficits_Counterparty', 0, 0, NULL),
                   (N'OverpaymentDeficits', N'IX_bms_web_OverpaymentDeficits_Currency', 0, 0, NULL),
                   (N'OverpaymentDeficits', N'IX_bms_web_OverpaymentDeficits_SourceExpenseRequest', 0, 0, NULL),
                   (N'OverpaymentDeficits', N'IX_bms_web_OverpaymentDeficits_ActualCorrectionExecutor', 0, 0, NULL),
                   (N'ManualOverpaymentDeficitCorrections', N'PK_bms_web_ManualOverpaymentDeficitCorrections', 1, 1, NULL),
                   (N'ManualOverpaymentDeficitCorrections', N'UQ_bms_web_ManualOverpaymentDeficitCorrections_CreationCommandId', 1, 0, NULL),
                   (N'ManualOverpaymentDeficitCorrections', N'UX_bms_web_ManualOverpaymentDeficitCorrections_PendingDeficit', 1, 0, N'([State]=N''PendingApproval'')'),
                   (N'ManualOverpaymentDeficitCorrections', N'IX_bms_web_ManualOverpaymentDeficitCorrections_DeficitScope', 0, 0, NULL),
                   (N'ManualOverpaymentDeficitCorrections', N'IX_bms_web_ManualOverpaymentDeficitCorrections_ResponsibleAccountant', 0, 0, NULL),
                   (N'ManualOverpaymentDeficitCorrections', N'IX_bms_web_ManualOverpaymentDeficitCorrections_FinancialDirector', 0, 0, NULL),
                   (N'OverpaymentDeficitSettlements', N'PK_bms_web_OverpaymentDeficitSettlements', 1, 1, NULL),
                   (N'OverpaymentDeficitSettlements', N'IX_bms_web_OverpaymentDeficitSettlements_SourceExpenseRequest', 0, 0, NULL),
                   (N'OverpaymentDeficitSettlements', N'UX_bms_web_OverpaymentDeficitSettlements_Correction', 1, 0, N'([CorrectionId]ISNOTNULL)'),
                   (N'OverpaymentDeficitSettlements', N'IX_bms_web_OverpaymentDeficitSettlements_AppliedAt', 0, 0, NULL),
                   (N'OverpaymentDeficitSettlements', N'IX_bms_web_OverpaymentDeficitSettlements_User', 0, 0, NULL)
           ) AS expected(TableName, IndexName, IsUnique, IsPrimaryKey, FilterDefinition)
           LEFT JOIN sys.indexes AS i
             ON i.object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
            AND i.name = expected.IndexName
           WHERE i.object_id IS NULL
              OR i.is_unique <> expected.IsUnique
              OR i.is_primary_key <> expected.IsPrimaryKey
              OR i.is_disabled <> 0 OR i.is_hypothetical <> 0
              OR
              (
                  expected.FilterDefinition IS NULL AND i.filter_definition IS NOT NULL
                  OR expected.FilterDefinition IS NOT NULL AND
                     REPLACE(REPLACE(REPLACE(REPLACE(i.filter_definition,
                         N' ', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'')
                         COLLATE Latin1_General_100_BIN2 <>
                     expected.FilterDefinition COLLATE Latin1_General_100_BIN2
              )
              )
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (N'OverpaymentDeficits', N'PK_bms_web_OverpaymentDeficits', 1, 4, N'OrganizationId'),
                   (N'OverpaymentDeficits', N'PK_bms_web_OverpaymentDeficits', 2, 4, N'CounterpartyId'),
                   (N'OverpaymentDeficits', N'PK_bms_web_OverpaymentDeficits', 3, 4, N'CurrencyId'),
                   (N'OverpaymentDeficits', N'PK_bms_web_OverpaymentDeficits', 4, 4, N'DeficitId'),
                   (N'OverpaymentDeficits', N'UQ_bms_web_OverpaymentDeficits_DeficitId', 1, 1, N'DeficitId'),
                   (N'OverpaymentDeficits', N'UQ_bms_web_OverpaymentDeficits_CreationCommandId', 1, 1, N'CreationCommandId'),
                   (N'OverpaymentDeficits', N'IX_bms_web_OverpaymentDeficits_OccurredAt', 1, 1, N'OccurredAt'),
                   (N'OverpaymentDeficits', N'IX_bms_web_OverpaymentDeficits_SourceOverpaymentId', 1, 1, N'SourceOverpaymentId'),
                   (N'OverpaymentDeficits', N'IX_bms_web_OverpaymentDeficits_ExternalReversalId', 1, 1, N'ExternalReversalId'),
                   (N'OverpaymentDeficits', N'IX_bms_web_OverpaymentDeficits_Counterparty', 1, 1, N'CounterpartyId'),
                   (N'OverpaymentDeficits', N'IX_bms_web_OverpaymentDeficits_Currency', 1, 1, N'CurrencyId'),
                   (N'OverpaymentDeficits', N'IX_bms_web_OverpaymentDeficits_SourceExpenseRequest', 1, 1, N'SourceExpenseRequestId'),
                   (N'OverpaymentDeficits', N'IX_bms_web_OverpaymentDeficits_ActualCorrectionExecutor', 1, 1, N'ActualCorrectionExecutorId'),
                   (N'ManualOverpaymentDeficitCorrections', N'PK_bms_web_ManualOverpaymentDeficitCorrections', 1, 1, N'CorrectionId'),
                   (N'ManualOverpaymentDeficitCorrections', N'UQ_bms_web_ManualOverpaymentDeficitCorrections_CreationCommandId', 1, 1, N'CreationCommandId'),
                   (N'ManualOverpaymentDeficitCorrections', N'UX_bms_web_ManualOverpaymentDeficitCorrections_PendingDeficit', 1, 1, N'DeficitId'),
                   (N'ManualOverpaymentDeficitCorrections', N'IX_bms_web_ManualOverpaymentDeficitCorrections_DeficitScope', 1, 4, N'OrganizationId'),
                   (N'ManualOverpaymentDeficitCorrections', N'IX_bms_web_ManualOverpaymentDeficitCorrections_DeficitScope', 2, 4, N'CounterpartyId'),
                   (N'ManualOverpaymentDeficitCorrections', N'IX_bms_web_ManualOverpaymentDeficitCorrections_DeficitScope', 3, 4, N'CurrencyId'),
                   (N'ManualOverpaymentDeficitCorrections', N'IX_bms_web_ManualOverpaymentDeficitCorrections_DeficitScope', 4, 4, N'DeficitId'),
                   (N'ManualOverpaymentDeficitCorrections', N'IX_bms_web_ManualOverpaymentDeficitCorrections_ResponsibleAccountant', 1, 1, N'ResponsibleAccountantId'),
                   (N'ManualOverpaymentDeficitCorrections', N'IX_bms_web_ManualOverpaymentDeficitCorrections_FinancialDirector', 1, 1, N'FinancialDirectorId'),
                   (N'OverpaymentDeficitSettlements', N'PK_bms_web_OverpaymentDeficitSettlements', 1, 2, N'DeficitId'),
                   (N'OverpaymentDeficitSettlements', N'PK_bms_web_OverpaymentDeficitSettlements', 2, 2, N'CommandId'),
                   (N'OverpaymentDeficitSettlements', N'IX_bms_web_OverpaymentDeficitSettlements_SourceExpenseRequest', 1, 1, N'SourceExpenseRequestId'),
                   (N'OverpaymentDeficitSettlements', N'UX_bms_web_OverpaymentDeficitSettlements_Correction', 1, 1, N'CorrectionId'),
                   (N'OverpaymentDeficitSettlements', N'IX_bms_web_OverpaymentDeficitSettlements_AppliedAt', 1, 1, N'AppliedAt'),
                   (N'OverpaymentDeficitSettlements', N'IX_bms_web_OverpaymentDeficitSettlements_User', 1, 1, N'UserId')
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
               OBJECT_ID(N'bms_web.OverpaymentDeficits'),
               OBJECT_ID(N'bms_web.ManualOverpaymentDeficitCorrections'),
               OBJECT_ID(N'bms_web.OverpaymentDeficitSettlements')
           )
             AND included.is_included_column <> 0
       )
        THROW 60011, N'BMS W142 refused: overpayment-deficit index contract is drifted.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.check_constraints
        WHERE parent_object_id IN
        (
            OBJECT_ID(N'bms_web.OverpaymentDeficits'),
            OBJECT_ID(N'bms_web.ManualOverpaymentDeficitCorrections'),
            OBJECT_ID(N'bms_web.OverpaymentDeficitSettlements')
        )
    ) <> 8
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (N'OverpaymentDeficits', N'CK_bms_web_OverpaymentDeficits_Amounts'),
                   (N'OverpaymentDeficits', N'CK_bms_web_OverpaymentDeficits_PairedState'),
                   (N'OverpaymentDeficits', N'CK_bms_web_OverpaymentDeficits_ExternalReversalId'),
                   (N'ManualOverpaymentDeficitCorrections', N'CK_bms_web_ManualOverpaymentDeficitCorrections_Amounts'),
                   (N'ManualOverpaymentDeficitCorrections', N'CK_bms_web_ManualOverpaymentDeficitCorrections_Basis'),
                   (N'ManualOverpaymentDeficitCorrections', N'CK_bms_web_ManualOverpaymentDeficitCorrections_State'),
                   (N'OverpaymentDeficitSettlements', N'CK_bms_web_OverpaymentDeficitSettlements_Amounts'),
                   (N'OverpaymentDeficitSettlements', N'CK_bms_web_OverpaymentDeficitSettlements_Source')
           ) AS expected(TableName, ConstraintName)
           LEFT JOIN sys.check_constraints AS cc
             ON cc.parent_object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
            AND cc.name = expected.ConstraintName
           WHERE cc.object_id IS NULL OR cc.is_disabled <> 0 OR cc.is_not_trusted <> 0
              OR cc.is_not_for_replication <> 0 OR cc.is_system_named <> 0
       )
        THROW 60012, N'BMS W142 refused: overpayment-deficit CHECK contract is drifted.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.foreign_keys
        WHERE parent_object_id IN
        (
            OBJECT_ID(N'bms_web.OverpaymentDeficits'),
            OBJECT_ID(N'bms_web.ManualOverpaymentDeficitCorrections'),
            OBJECT_ID(N'bms_web.OverpaymentDeficitSettlements')
        )
    ) <> 12
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (N'OverpaymentDeficits', N'FK_bms_web_OverpaymentDeficits_Organization'),
                   (N'OverpaymentDeficits', N'FK_bms_web_OverpaymentDeficits_Counterparty'),
                   (N'OverpaymentDeficits', N'FK_bms_web_OverpaymentDeficits_Currency'),
                   (N'OverpaymentDeficits', N'FK_bms_web_OverpaymentDeficits_SourceExpenseRequest'),
                   (N'OverpaymentDeficits', N'FK_bms_web_OverpaymentDeficits_ActualCorrectionExecutor'),
                   (N'ManualOverpaymentDeficitCorrections', N'FK_bms_web_ManualOverpaymentDeficitCorrections_DeficitScope'),
                   (N'ManualOverpaymentDeficitCorrections', N'FK_bms_web_ManualOverpaymentDeficitCorrections_ResponsibleAccountant'),
                   (N'ManualOverpaymentDeficitCorrections', N'FK_bms_web_ManualOverpaymentDeficitCorrections_FinancialDirector'),
                   (N'OverpaymentDeficitSettlements', N'FK_bms_web_OverpaymentDeficitSettlements_Deficit'),
                   (N'OverpaymentDeficitSettlements', N'FK_bms_web_OverpaymentDeficitSettlements_SourceExpenseRequest'),
                   (N'OverpaymentDeficitSettlements', N'FK_bms_web_OverpaymentDeficitSettlements_Correction'),
                   (N'OverpaymentDeficitSettlements', N'FK_bms_web_OverpaymentDeficitSettlements_User')
           ) AS expected(TableName, ConstraintName)
           LEFT JOIN sys.foreign_keys AS fk
             ON fk.parent_object_id = OBJECT_ID(N'bms_web.' + expected.TableName)
            AND fk.name = expected.ConstraintName
           WHERE fk.object_id IS NULL OR fk.is_disabled <> 0 OR fk.is_not_trusted <> 0
              OR fk.is_not_for_replication <> 0 OR fk.is_system_named <> 0
              OR fk.delete_referential_action <> 0 OR fk.update_referential_action <> 0
       )
       OR
       (
           SELECT COUNT(*)
           FROM sys.foreign_key_columns AS fkc
           WHERE fkc.parent_object_id IN
           (
               OBJECT_ID(N'bms_web.OverpaymentDeficits'),
               OBJECT_ID(N'bms_web.ManualOverpaymentDeficitCorrections'),
               OBJECT_ID(N'bms_web.OverpaymentDeficitSettlements')
           )
       ) <> 15
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (N'OverpaymentDeficits', N'FK_bms_web_OverpaymentDeficits_Organization', 1, N'OrganizationId', N'Organizations', N'Id'),
                   (N'OverpaymentDeficits', N'FK_bms_web_OverpaymentDeficits_Counterparty', 1, N'CounterpartyId', N'Counterparties', N'Id'),
                   (N'OverpaymentDeficits', N'FK_bms_web_OverpaymentDeficits_Currency', 1, N'CurrencyId', N'Currencies', N'Id'),
                   (N'OverpaymentDeficits', N'FK_bms_web_OverpaymentDeficits_SourceExpenseRequest', 1, N'SourceExpenseRequestId', N'ExpenseRequests', N'Id'),
                   (N'OverpaymentDeficits', N'FK_bms_web_OverpaymentDeficits_ActualCorrectionExecutor', 1, N'ActualCorrectionExecutorId', N'Employees', N'Id'),
                   (N'ManualOverpaymentDeficitCorrections', N'FK_bms_web_ManualOverpaymentDeficitCorrections_DeficitScope', 1, N'OrganizationId', N'OverpaymentDeficits', N'OrganizationId'),
                   (N'ManualOverpaymentDeficitCorrections', N'FK_bms_web_ManualOverpaymentDeficitCorrections_DeficitScope', 2, N'CounterpartyId', N'OverpaymentDeficits', N'CounterpartyId'),
                   (N'ManualOverpaymentDeficitCorrections', N'FK_bms_web_ManualOverpaymentDeficitCorrections_DeficitScope', 3, N'CurrencyId', N'OverpaymentDeficits', N'CurrencyId'),
                   (N'ManualOverpaymentDeficitCorrections', N'FK_bms_web_ManualOverpaymentDeficitCorrections_DeficitScope', 4, N'DeficitId', N'OverpaymentDeficits', N'DeficitId'),
                   (N'ManualOverpaymentDeficitCorrections', N'FK_bms_web_ManualOverpaymentDeficitCorrections_ResponsibleAccountant', 1, N'ResponsibleAccountantId', N'Employees', N'Id'),
                   (N'ManualOverpaymentDeficitCorrections', N'FK_bms_web_ManualOverpaymentDeficitCorrections_FinancialDirector', 1, N'FinancialDirectorId', N'Employees', N'Id'),
                   (N'OverpaymentDeficitSettlements', N'FK_bms_web_OverpaymentDeficitSettlements_Deficit', 1, N'DeficitId', N'OverpaymentDeficits', N'DeficitId'),
                   (N'OverpaymentDeficitSettlements', N'FK_bms_web_OverpaymentDeficitSettlements_SourceExpenseRequest', 1, N'SourceExpenseRequestId', N'ExpenseRequests', N'Id'),
                   (N'OverpaymentDeficitSettlements', N'FK_bms_web_OverpaymentDeficitSettlements_Correction', 1, N'CorrectionId', N'ManualOverpaymentDeficitCorrections', N'CorrectionId'),
                   (N'OverpaymentDeficitSettlements', N'FK_bms_web_OverpaymentDeficitSettlements_User', 1, N'UserId', N'Employees', N'Id')
           ) AS expected(ParentTable, ConstraintName, ConstraintColumnId, ParentColumn, ReferencedTable, ReferencedColumn)
           LEFT JOIN sys.foreign_keys AS fk
             ON fk.parent_object_id = OBJECT_ID(N'bms_web.' + expected.ParentTable)
            AND fk.name = expected.ConstraintName
           LEFT JOIN sys.foreign_key_columns AS fkc
             ON fkc.constraint_object_id = fk.object_id
            AND fkc.constraint_column_id = expected.ConstraintColumnId
           LEFT JOIN sys.columns AS parent_column
             ON parent_column.object_id = fkc.parent_object_id
            AND parent_column.column_id = fkc.parent_column_id
           LEFT JOIN sys.columns AS referenced_column
             ON referenced_column.object_id = fkc.referenced_object_id
            AND referenced_column.column_id = fkc.referenced_column_id
           WHERE parent_column.name IS NULL
              OR parent_column.name <> expected.ParentColumn
              OR fkc.referenced_object_id <>
                 OBJECT_ID(N'bms_web.' + expected.ReferencedTable)
              OR referenced_column.name IS NULL
              OR referenced_column.name <> expected.ReferencedColumn
       )
        THROW 60013, N'BMS W142 refused: overpayment-deficit foreign-key contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.triggers
        WHERE parent_id IN
        (
            OBJECT_ID(N'bms_web.OverpaymentDeficits'),
            OBJECT_ID(N'bms_web.ManualOverpaymentDeficitCorrections'),
            OBJECT_ID(N'bms_web.OverpaymentDeficitSettlements')
        )
          AND is_ms_shipped = 0
    )
        THROW 60014, N'BMS W142 refused: overpayment-deficit tables have user DML triggers.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 19
    )
       AND @ExistingTargetCount <> 3
        THROW 60015, N'BMS W142 refused: version 19 is registered without its physical contract.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 19
    )
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (19, N'019_overpayment_deficit_storage.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
