-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web — additive expense-request evidence fields (W-08).

    IMPORTANT
    - Apply managed schema versions 1 through 4 first.
    - This migration adds draft data only. It creates no workflow transition,
      budget reservation, NSI import, attachment storage or file endpoint.
    - The exact conditional counterparty matrix remains OPEN; the database does
      not invent it with a check constraint.
    - This script creates no database, login, user, role or permission.
    - It has not been executed against an accounting server.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 55001, N'BMS evidence migration refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 55002, N'BMS evidence migration refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
    THROW 55003, N'BMS evidence migration refused: managed schema bms_web is absent.', 1;

IF NOT EXISTS
(
    SELECT 1 FROM bms_web.schema_version
    WHERE version_no = 1
      AND script_name COLLATE Latin1_General_100_BIN2 = N'001_bootstrap_bms_web.sql'
)
   OR NOT EXISTS
(
    SELECT 1 FROM bms_web.schema_version
    WHERE version_no = 2
      AND script_name COLLATE Latin1_General_100_BIN2 = N'002_budget_projection.sql'
)
   OR NOT EXISTS
(
    SELECT 1 FROM bms_web.schema_version
    WHERE version_no = 3
      AND script_name COLLATE Latin1_General_100_BIN2 = N'003_access_scope.sql'
)
   OR NOT EXISTS
(
    SELECT 1 FROM bms_web.schema_version
    WHERE version_no = 4
      AND script_name COLLATE Latin1_General_100_BIN2 = N'004_workflow_matrix.sql'
)
    THROW 55004, N'BMS evidence migration refused: exact schema versions 1 through 4 are required.', 1;

IF EXISTS
(
    SELECT 1 FROM bms_web.schema_version
    WHERE version_no = 5
      AND script_name COLLATE Latin1_General_100_BIN2 <> N'005_expense_request_evidence.sql'
)
    THROW 55005, N'BMS evidence migration refused: schema version 5 belongs to another script.', 1;

IF OBJECT_ID(N'bms_web.ExpenseRequests', N'U') IS NULL
    THROW 55006, N'BMS evidence migration refused: bms_web.ExpenseRequests is absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'PaymentBasisCode') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD PaymentBasisCode nvarchar(40) NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'InvoiceNumber') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD InvoiceNumber nvarchar(100) NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'InvoiceDate') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD InvoiceDate date NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'PublicOfferName') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD PublicOfferName nvarchar(300) NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'PublicOfferIdentifier') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD PublicOfferIdentifier nvarchar(1000) NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'OtherBasisExplanation') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD OtherBasisExplanation nvarchar(1000) NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'SupportingDocumentMissing') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD SupportingDocumentMissing bit NOT NULL
            CONSTRAINT DF_bms_web_ExpenseRequests_SupportingDocumentMissing DEFAULT (0) WITH VALUES;
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'SupportingDocumentMissingExplanation') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD SupportingDocumentMissingExplanation nvarchar(1000) NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'CandidateCounterpartyName') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD CandidateCounterpartyName nvarchar(300) NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'CandidateResidencyCode') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD CandidateResidencyCode nvarchar(40) NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'CandidateBinIin') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD CandidateBinIin nvarchar(12) NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'CandidateCountry') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD CandidateCountry nvarchar(100) NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'CandidateForeignRegistrationNumber') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD CandidateForeignRegistrationNumber nvarchar(100) NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'CandidateRegistrationNumberMissing') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD CandidateRegistrationNumberMissing bit NOT NULL
            CONSTRAINT DF_bms_web_ExpenseRequests_CandidateRegistrationNumberMissing DEFAULT (0) WITH VALUES;
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'CandidateRegistrationNumberMissingExplanation') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD CandidateRegistrationNumberMissingExplanation nvarchar(1000) NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'CandidateContractNumber') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD CandidateContractNumber nvarchar(100) NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'CandidateContractNumberMissing') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD CandidateContractNumberMissing bit NOT NULL
            CONSTRAINT DF_bms_web_ExpenseRequests_CandidateContractNumberMissing DEFAULT (0) WITH VALUES;
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'CandidateContractDate') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD CandidateContractDate date NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'CandidateContractSubject') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD CandidateContractSubject nvarchar(300) NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'CandidateContractTerms') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD CandidateContractTerms nvarchar(1000) NULL;
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'NewContractDocumentMissing') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD NewContractDocumentMissing bit NOT NULL
            CONSTRAINT DF_bms_web_ExpenseRequests_NewContractDocumentMissing DEFAULT (0) WITH VALUES;
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'NewContractDocumentMissingExplanation') IS NULL
        ALTER TABLE bms_web.ExpenseRequests ADD NewContractDocumentMissingExplanation nvarchar(1000) NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
          AND name = N'CK_bms_web_ExpenseRequests_PaymentBasisCode'
    )
        /*
            SQL Server binds a static CHECK before the preceding additive
            PaymentBasisCode column is visible in this batch. Keep the exact
            checked definition, but compile it after the column has been added.
        */
        EXEC(N'ALTER TABLE bms_web.ExpenseRequests WITH CHECK ADD
            CONSTRAINT CK_bms_web_ExpenseRequests_PaymentBasisCode CHECK
            (
                PaymentBasisCode IS NULL
                OR PaymentBasisCode COLLATE Latin1_General_100_BIN2 = N''Contract''
                OR PaymentBasisCode COLLATE Latin1_General_100_BIN2 = N''InvoiceWithoutContract''
                OR PaymentBasisCode COLLATE Latin1_General_100_BIN2 = N''PublicOffer''
                OR PaymentBasisCode COLLATE Latin1_General_100_BIN2 = N''OtherBasis''
            );');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
          AND name = N'CK_bms_web_ExpenseRequests_CandidateResidencyCode'
    )
        EXEC(N'ALTER TABLE bms_web.ExpenseRequests WITH CHECK ADD
            CONSTRAINT CK_bms_web_ExpenseRequests_CandidateResidencyCode CHECK
            (
                CandidateResidencyCode IS NULL
                OR CandidateResidencyCode COLLATE Latin1_General_100_BIN2 = N''ResidentKz''
                OR CandidateResidencyCode COLLATE Latin1_General_100_BIN2 = N''NonResident''
            );');

    /* Exact W-08 column type, length and nullability drift contract. */
    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'PaymentBasisCode', N'nvarchar', 80, 1),
                (N'InvoiceNumber', N'nvarchar', 200, 1),
                (N'InvoiceDate', N'date', 3, 1),
                (N'PublicOfferName', N'nvarchar', 600, 1),
                (N'PublicOfferIdentifier', N'nvarchar', 2000, 1),
                (N'OtherBasisExplanation', N'nvarchar', 2000, 1),
                (N'SupportingDocumentMissing', N'bit', 1, 0),
                (N'SupportingDocumentMissingExplanation', N'nvarchar', 2000, 1),
                (N'CandidateCounterpartyName', N'nvarchar', 600, 1),
                (N'CandidateResidencyCode', N'nvarchar', 80, 1),
                (N'CandidateBinIin', N'nvarchar', 24, 1),
                (N'CandidateCountry', N'nvarchar', 200, 1),
                (N'CandidateForeignRegistrationNumber', N'nvarchar', 200, 1),
                (N'CandidateRegistrationNumberMissing', N'bit', 1, 0),
                (N'CandidateRegistrationNumberMissingExplanation', N'nvarchar', 2000, 1),
                (N'CandidateContractNumber', N'nvarchar', 200, 1),
                (N'CandidateContractNumberMissing', N'bit', 1, 0),
                (N'CandidateContractDate', N'date', 3, 1),
                (N'CandidateContractSubject', N'nvarchar', 600, 1),
                (N'CandidateContractTerms', N'nvarchar', 2000, 1),
                (N'NewContractDocumentMissing', N'bit', 1, 0),
                (N'NewContractDocumentMissingExplanation', N'nvarchar', 2000, 1)
        ) AS expected(ColumnName, TypeName, MaxLength, IsNullable)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
         AND c.name = expected.ColumnName
        WHERE c.object_id IS NULL
           OR TYPE_NAME(c.user_type_id) <> expected.TypeName
           OR c.max_length <> expected.MaxLength
           OR c.is_nullable <> expected.IsNullable
    )
        THROW 55007, N'BMS evidence migration refused: W-08 columns are missing or drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (
                    N'CK_bms_web_ExpenseRequests_PaymentBasisCode',
                    N'PaymentBasisCodeISNULLORPaymentBasisCodecollateLatin1_General_100_BIN2=N''Contract''ORPaymentBasisCodecollateLatin1_General_100_BIN2=N''InvoiceWithoutContract''ORPaymentBasisCodecollateLatin1_General_100_BIN2=N''PublicOffer''ORPaymentBasisCodecollateLatin1_General_100_BIN2=N''OtherBasis'''
                ),
                (
                    N'CK_bms_web_ExpenseRequests_CandidateResidencyCode',
                    N'CandidateResidencyCodeISNULLORCandidateResidencyCodecollateLatin1_General_100_BIN2=N''ResidentKz''ORCandidateResidencyCodecollateLatin1_General_100_BIN2=N''NonResident'''
                )
        ) AS expected(ConstraintName, NormalizedDefinition)
        WHERE NOT EXISTS
        (
            SELECT 1 FROM sys.check_constraints AS cc
            WHERE cc.parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
              AND cc.name = expected.ConstraintName
              AND cc.is_disabled = 0
              AND cc.is_not_trusted = 0
              AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                    cc.definition,
                    N'[', N''), N']', N''), N' ', N''), N'(', N''), N')', N''),
                    CHAR(9), N''), CHAR(10), N''), CHAR(13), N'')
                    COLLATE Latin1_General_100_BIN2 =
                  expected.NormalizedDefinition COLLATE Latin1_General_100_BIN2
        )
    )
        THROW 55008, N'BMS evidence migration refused: W-08 enum checks are missing or untrusted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'DF_bms_web_ExpenseRequests_SupportingDocumentMissing', N'SupportingDocumentMissing'),
                (N'DF_bms_web_ExpenseRequests_CandidateRegistrationNumberMissing', N'CandidateRegistrationNumberMissing'),
                (N'DF_bms_web_ExpenseRequests_CandidateContractNumberMissing', N'CandidateContractNumberMissing'),
                (N'DF_bms_web_ExpenseRequests_NewContractDocumentMissing', N'NewContractDocumentMissing')
        ) AS expected(ConstraintName, ColumnName)
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM sys.default_constraints AS dc
            INNER JOIN sys.columns AS c
              ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
            WHERE dc.parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
              AND dc.name = expected.ConstraintName
              AND c.name = expected.ColumnName
              AND REPLACE(REPLACE(REPLACE(dc.definition, N' ', N''), N'(', N''), N')', N'') = N'0'
        )
    )
        THROW 55009, N'BMS evidence migration refused: W-08 flag defaults are missing or drifted.', 1;

    IF NOT EXISTS (SELECT 1 FROM bms_web.schema_version WHERE version_no = 5)
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (5, N'005_expense_request_evidence.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
