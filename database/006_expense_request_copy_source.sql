-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web — hidden expense-request copy provenance (W-10).

    IMPORTANT
    - Apply managed schema versions 1 through 5 first.
    - CopiedFromExpenseRequestId is technical provenance. It must not be exposed
      by ordinary DTOs, audit snapshots, Outbox payloads, history or exports.
    - This script creates no business data, database, login, user, role or permission.
    - It has not been executed against an accounting server.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 56001, N'BMS copy-source migration refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 56002, N'BMS copy-source migration refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
    THROW 56003, N'BMS copy-source migration refused: managed schema bms_web is absent.', 1;

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
   OR NOT EXISTS
(
    SELECT 1 FROM bms_web.schema_version
    WHERE version_no = 5
      AND script_name COLLATE Latin1_General_100_BIN2 = N'005_expense_request_evidence.sql'
)
    THROW 56004, N'BMS copy-source migration refused: exact schema versions 1 through 5 are required.', 1;

IF EXISTS
(
    SELECT 1 FROM bms_web.schema_version
    WHERE version_no = 6
      AND script_name COLLATE Latin1_General_100_BIN2 <> N'006_expense_request_copy_source.sql'
)
    THROW 56005, N'BMS copy-source migration refused: schema version 6 belongs to another script.', 1;

IF OBJECT_ID(N'bms_web.ExpenseRequests', N'U') IS NULL
    THROW 56006, N'BMS copy-source migration refused: bms_web.ExpenseRequests is absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'CopiedFromExpenseRequestId') IS NULL
        ALTER TABLE bms_web.ExpenseRequests
            ADD CopiedFromExpenseRequestId uniqueidentifier NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
          AND name = N'FK_bms_web_ExpenseRequests_CopySource'
    )
    BEGIN
        /* Compile after the additive column exists in this batch. */
        EXEC(N'ALTER TABLE bms_web.ExpenseRequests WITH CHECK ADD
            CONSTRAINT FK_bms_web_ExpenseRequests_CopySource
            FOREIGN KEY (CopiedFromExpenseRequestId)
            REFERENCES bms_web.ExpenseRequests (Id)
            ON DELETE NO ACTION
            ON UPDATE NO ACTION;
            ALTER TABLE bms_web.ExpenseRequests
                CHECK CONSTRAINT FK_bms_web_ExpenseRequests_CopySource;');
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
          AND name = N'CK_bms_web_ExpenseRequests_CopySourceNotSelf'
    )
        EXEC(N'ALTER TABLE bms_web.ExpenseRequests WITH CHECK ADD
            CONSTRAINT CK_bms_web_ExpenseRequests_CopySourceNotSelf CHECK
            (
                CopiedFromExpenseRequestId IS NULL
                OR CopiedFromExpenseRequestId <> Id
            );');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
          AND name = N'IX_bms_web_ExpenseRequests_CopiedFromExpenseRequestId'
    )
        EXEC(N'CREATE INDEX IX_bms_web_ExpenseRequests_CopiedFromExpenseRequestId
            ON bms_web.ExpenseRequests (CopiedFromExpenseRequestId)
            WHERE CopiedFromExpenseRequestId IS NOT NULL;');

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.columns AS c
        WHERE c.object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
          AND c.name = N'CopiedFromExpenseRequestId'
          AND TYPE_NAME(c.user_type_id) = N'uniqueidentifier'
          AND c.max_length = 16
          AND c.is_nullable = 1
    )
        THROW 56007, N'BMS copy-source migration refused: copy-source column is missing or drifted.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys AS fk
        WHERE fk.parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
          AND fk.referenced_object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
          AND fk.name = N'FK_bms_web_ExpenseRequests_CopySource'
          AND fk.delete_referential_action = 0
          AND fk.update_referential_action = 0
          AND fk.is_disabled = 0
          AND fk.is_not_trusted = 0
          AND (SELECT COUNT(*)
               FROM sys.foreign_key_columns AS exact_fkc
               WHERE exact_fkc.constraint_object_id = fk.object_id) = 1
          AND EXISTS
          (
              SELECT 1
              FROM sys.foreign_key_columns AS fkc
              INNER JOIN sys.columns AS parent_column
                ON parent_column.object_id = fkc.parent_object_id
               AND parent_column.column_id = fkc.parent_column_id
              INNER JOIN sys.columns AS referenced_column
                ON referenced_column.object_id = fkc.referenced_object_id
               AND referenced_column.column_id = fkc.referenced_column_id
              WHERE fkc.constraint_object_id = fk.object_id
                AND parent_column.name = N'CopiedFromExpenseRequestId'
                AND referenced_column.name = N'Id'
          )
    )
        THROW 56008, N'BMS copy-source migration refused: self-reference FK is missing or drifted.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints AS cc
        WHERE cc.parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
          AND cc.name = N'CK_bms_web_ExpenseRequests_CopySourceNotSelf'
          AND cc.is_disabled = 0
          AND cc.is_not_trusted = 0
          AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                cc.definition,
                N'[', N''), N']', N''), N' ', N''), N'(', N''), N')', N''),
                CHAR(9), N''), CHAR(10), N''), CHAR(13), N'')
                COLLATE Latin1_General_100_BIN2 =
              N'CopiedFromExpenseRequestIdISNULLORCopiedFromExpenseRequestId<>Id'
                COLLATE Latin1_General_100_BIN2
    )
        THROW 56009, N'BMS copy-source migration refused: anti-self CHECK is missing or drifted.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes AS i
        WHERE i.object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
          AND i.name = N'IX_bms_web_ExpenseRequests_CopiedFromExpenseRequestId'
          AND i.type = 2
          AND i.is_unique = 0
          AND i.is_disabled = 0
          AND i.is_hypothetical = 0
          AND i.has_filter = 1
          AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                i.filter_definition,
                N'[', N''), N']', N''), N' ', N''), N'(', N''), N')', N''),
                CHAR(9), N''), CHAR(10), N''), CHAR(13), N'')
                COLLATE Latin1_General_100_BIN2 =
              N'CopiedFromExpenseRequestIdISNOTNULL' COLLATE Latin1_General_100_BIN2
          AND (SELECT COUNT(*)
               FROM sys.index_columns AS exact_ic
               WHERE exact_ic.object_id = i.object_id
                 AND exact_ic.index_id = i.index_id) = 1
          AND EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS ic
              INNER JOIN sys.columns AS c
                ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id
                AND ic.index_id = i.index_id
                AND ic.key_ordinal = 1
                AND ic.is_included_column = 0
                AND c.name = N'CopiedFromExpenseRequestId'
          )
    )
        THROW 56010, N'BMS copy-source migration refused: filtered source index is missing or drifted.', 1;

    IF NOT EXISTS (SELECT 1 FROM bms_web.schema_version WHERE version_no = 6)
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (6, N'006_expense_request_copy_source.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
