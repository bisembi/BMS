-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web — stable hidden expense-request line order (W-11).

    IMPORTANT
    - Apply managed schema versions 1 through 6 first.
    - SortOrder is server-owned technical state. It must not be exposed by
      ordinary DTOs, audit snapshots, Outbox payloads, history or exports.
    - Existing rows are never assigned a guessed order. If the column is absent
      and legacy lines exist, this migration refuses to proceed.
    - This script creates no business data, database, login, user, role or permission.
    - It has not been executed against an accounting server.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 57001, N'BMS line-order migration refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 57002, N'BMS line-order migration refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
    THROW 57003, N'BMS line-order migration refused: managed schema bms_web is absent.', 1;

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
   OR NOT EXISTS
(
    SELECT 1 FROM bms_web.schema_version
    WHERE version_no = 6
      AND script_name COLLATE Latin1_General_100_BIN2 = N'006_expense_request_copy_source.sql'
)
    THROW 57004, N'BMS line-order migration refused: exact schema versions 1 through 6 are required.', 1;

IF EXISTS
(
    SELECT 1 FROM bms_web.schema_version
    WHERE version_no = 7
      AND script_name COLLATE Latin1_General_100_BIN2 <> N'007_expense_request_line_order.sql'
)
    THROW 57005, N'BMS line-order migration refused: schema version 7 belongs to another script.', 1;

IF OBJECT_ID(N'bms_web.ExpenseRequestLines', N'U') IS NULL
    THROW 57006, N'BMS line-order migration refused: bms_web.ExpenseRequestLines is absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'bms_web.ExpenseRequestLines', N'SortOrder') IS NULL
       AND EXISTS (SELECT 1 FROM bms_web.ExpenseRequestLines)
        THROW 57007, N'BMS W-11 refused: existing request lines require a reviewed line-order migration.', 1;

    IF COL_LENGTH(N'bms_web.ExpenseRequestLines', N'SortOrder') IS NULL
        ALTER TABLE bms_web.ExpenseRequestLines
            ADD SortOrder int NOT NULL;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
          AND name = N'CK_bms_web_ExpenseRequestLines_SortOrder_Positive'
    )
        /* Compile after the additive SortOrder column exists in this batch. */
        EXEC(N'ALTER TABLE bms_web.ExpenseRequestLines WITH CHECK ADD
            CONSTRAINT CK_bms_web_ExpenseRequestLines_SortOrder_Positive
            CHECK (SortOrder > 0);');

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
          AND name = N'UX_bms_web_ExpenseRequestLines_Request_SortOrder'
    )
        EXEC(N'CREATE UNIQUE INDEX UX_bms_web_ExpenseRequestLines_Request_SortOrder
            ON bms_web.ExpenseRequestLines (ExpenseRequestId, SortOrder);');

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.columns AS c
        WHERE c.object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
          AND c.name = N'SortOrder'
          AND TYPE_NAME(c.user_type_id) = N'int'
          AND c.max_length = 4
          AND c.[precision] = 10
          AND c.scale = 0
          AND c.is_nullable = 0
          AND c.is_identity = 0
          AND c.is_computed = 0
          AND c.default_object_id = 0
    )
        THROW 57008, N'BMS line-order migration refused: SortOrder column is missing or drifted.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints AS cc
        WHERE cc.parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
          AND cc.name = N'CK_bms_web_ExpenseRequestLines_SortOrder_Positive'
          AND cc.is_disabled = 0
          AND cc.is_not_trusted = 0
          AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                cc.definition,
                N'[', N''), N']', N''), N' ', N''), N'(', N''), N')', N''),
                CHAR(9), N''), CHAR(10), N''), CHAR(13), N'')
                COLLATE Latin1_General_100_BIN2 =
              N'SortOrder>0' COLLATE Latin1_General_100_BIN2
    )
        THROW 57009, N'BMS line-order migration refused: positive-order CHECK is missing or drifted.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes AS i
        WHERE i.object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines')
          AND i.name = N'UX_bms_web_ExpenseRequestLines_Request_SortOrder'
          AND i.type = 2
          AND i.is_unique = 1
          AND i.is_disabled = 0
          AND i.is_hypothetical = 0
          AND i.has_filter = 0
          AND i.filter_definition IS NULL
          AND (SELECT COUNT(*)
               FROM sys.index_columns AS exact_ic
               WHERE exact_ic.object_id = i.object_id
                 AND exact_ic.index_id = i.index_id) = 2
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
                AND c.name = N'ExpenseRequestId'
          )
          AND EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS ic
              INNER JOIN sys.columns AS c
                ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id
                AND ic.index_id = i.index_id
                AND ic.key_ordinal = 2
                AND ic.is_included_column = 0
                AND c.name = N'SortOrder'
          )
    )
        THROW 57010, N'BMS line-order migration refused: unique request/order index is missing or drifted.', 1;

    IF NOT EXISTS (SELECT 1 FROM bms_web.schema_version WHERE version_no = 7)
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (7, N'007_expense_request_line_order.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
