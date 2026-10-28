-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    W641: immutable expense-request attachment storage.

    The table owns the exact bytes accepted by the application scanner. It
    does not grant access by itself: every read and mutation is authorized
    through the parent expense request. Pre-submission attachments may be
    deleted only while unsealed; submission seals them. Post-submission
    evidence is born sealed and requires a comment.
*/
IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS W641 refused: run SQL031 outside an ambient transaction.', 16, 1);
    RETURN;
END;
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 62701, N'BMS W641 refused: current database must be the explicitly owned BMS database.', 1;
IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 62702, N'BMS W641 refused: current database is not READ_WRITE.', 1;
IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.ExpenseRequests', N'U') IS NULL
    THROW 62703, N'BMS W641 refused: required managed tables are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no > 31
    )
        THROW 62704, N'BMS W641 refused: a future schema version is registered.', 1;

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
                (25, N'025_expense_request_accrual_closing_cycles.sql'),
                (26, N'026_expense_request_closing_activation.sql'),
                (27, N'027_accrual_red_zone_notifications.sql'),
                (28, N'028_cash_flow_accrual_settings_version.sql'),
                (29, N'029_expense_request_substitutions.sql'),
                (30, N'030_expense_request_task_scope_index.sql')
        ) AS expected(version_no, script_name)
        INNER JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK)
          ON actual.version_no = expected.version_no
         AND actual.script_name COLLATE Latin1_General_100_BIN2 =
             expected.script_name COLLATE Latin1_General_100_BIN2
         AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
    ) <> 30
        THROW 62705, N'BMS W641 refused: exact SQL001-SQL030 predecessors are required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 31
          AND
          (
              script_name COLLATE Latin1_General_100_BIN2 <>
                  N'031_expense_request_attachments.sql'
              OR DATALENGTH(script_name) <>
                  DATALENGTH(N'031_expense_request_attachments.sql')
          )
    )
        THROW 62706, N'BMS W641 refused: SQL031 belongs to another script.', 1;

    DECLARE @VersionRegistered bit = CASE WHEN EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 31
    ) THEN 1 ELSE 0 END;
    DECLARE @TableId int = OBJECT_ID(N'bms_web.ExpenseRequestAttachments');

    IF (@VersionRegistered = 0 AND @TableId IS NOT NULL)
       OR (@VersionRegistered = 1 AND @TableId IS NULL)
        THROW 62707, N'BMS W641 refused: version registration and table ownership disagree.', 1;

    DECLARE @RequestLock uniqueidentifier;
    SELECT TOP (1) @RequestLock = Id
    FROM bms_web.ExpenseRequests WITH (TABLOCKX, HOLDLOCK)
    ORDER BY Id;

    IF @VersionRegistered = 0
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM sys.objects
            WHERE name IN
            (
                N'PK_bms_web_ExpenseRequestAttachments',
                N'FK_bms_web_ExpenseRequestAttachments_Request',
                N'CK_bms_web_ExpenseRequestAttachments_Identity',
                N'CK_bms_web_ExpenseRequestAttachments_File',
                N'CK_bms_web_ExpenseRequestAttachments_Lifecycle',
                N'CK_bms_web_ExpenseRequestAttachments_Uploader',
                N'IX_bms_web_ExpenseRequestAttachments_Request_Time_Id'
            )
        )
            THROW 62708, N'BMS W641 refused: a required object name is already owned.', 1;

        CREATE TABLE bms_web.ExpenseRequestAttachments
        (
            Id                   uniqueidentifier NOT NULL,
            ExpenseRequestId     uniqueidentifier NOT NULL,
            Kind                 nvarchar(30) COLLATE Latin1_General_100_BIN2 NOT NULL,
            FileName             nvarchar(255) COLLATE Latin1_General_100_BIN2 NOT NULL,
            FileSizeBytes        bigint NOT NULL,
            ContentSha256        binary(32) NOT NULL,
            Content              varbinary(max) NOT NULL,
            UploadedAtUtc        datetimeoffset(3) NOT NULL,
            UploadedByExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
            Comment              nvarchar(1000) COLLATE Latin1_General_100_BIN2 NULL,
            IsPostSubmission     bit NOT NULL,
            IsSealed             bit NOT NULL,
            CONSTRAINT PK_bms_web_ExpenseRequestAttachments
                PRIMARY KEY CLUSTERED (Id ASC),
            CONSTRAINT FK_bms_web_ExpenseRequestAttachments_Request
                FOREIGN KEY (ExpenseRequestId)
                REFERENCES bms_web.ExpenseRequests (Id)
                ON DELETE CASCADE ON UPDATE NO ACTION,
            CONSTRAINT CK_bms_web_ExpenseRequestAttachments_Identity CHECK
            (
                Id <> CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000')
                AND ExpenseRequestId <>
                    CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000')
            ),
            CONSTRAINT CK_bms_web_ExpenseRequestAttachments_File CHECK
            (
                FileSizeBytes BETWEEN 1 AND 26214400
                AND DATALENGTH(Content) = FileSizeBytes
                AND ContentSha256 = HASHBYTES('SHA2_256', Content)
                AND DATALENGTH(FileName) BETWEEN 2 AND 510
                AND FileName = LTRIM(RTRIM(FileName))
                AND RIGHT(FileName, 1) NOT IN (N'.', N' ')
                AND FileName NOT LIKE N'%[/\\:*?""<>|]%'
                AND PATINDEX(
                    N'%[' + NCHAR(1) + N'-' + NCHAR(31)
                        + NCHAR(127) + N'-' + NCHAR(159) + N']%',
                    FileName COLLATE Latin1_General_100_BIN2) = 0
            ),
            CONSTRAINT CK_bms_web_ExpenseRequestAttachments_Lifecycle CHECK
            (
                (IsPostSubmission = 0
                    AND Kind IN (N'SUPPORTING', N'NEW_CONTRACT')
                    AND Comment IS NULL)
                OR
                (IsPostSubmission = 1
                    AND Kind = N'ADDITIONAL'
                    AND IsSealed = 1
                    AND Comment IS NOT NULL
                    AND DATALENGTH(Comment) BETWEEN 2 AND 2000
                    AND Comment = LTRIM(RTRIM(Comment))
                    AND PATINDEX(
                        N'%[' + NCHAR(1) + N'-' + NCHAR(31)
                            + NCHAR(127) + N'-' + NCHAR(159) + N']%',
                        Comment COLLATE Latin1_General_100_BIN2) = 0)
            ),
            CONSTRAINT CK_bms_web_ExpenseRequestAttachments_Uploader CHECK
            (
                DATEPART(TZOFFSET, UploadedAtUtc) = 0
                AND DATALENGTH(UploadedByExternalId) BETWEEN 2 AND 512
                AND UploadedByExternalId = LTRIM(RTRIM(UploadedByExternalId))
                AND PATINDEX(
                    N'%[' + NCHAR(1) + N'-' + NCHAR(31)
                        + NCHAR(127) + N'-' + NCHAR(159) + N']%',
                    UploadedByExternalId COLLATE Latin1_General_100_BIN2) = 0
            )
        );

        CREATE NONCLUSTERED INDEX
            IX_bms_web_ExpenseRequestAttachments_Request_Time_Id
            ON bms_web.ExpenseRequestAttachments
                (ExpenseRequestId ASC, UploadedAtUtc ASC, Id ASC)
            INCLUDE
                (Kind, FileName, FileSizeBytes, ContentSha256,
                 UploadedByExternalId, Comment, IsPostSubmission, IsSealed);
        SET @TableId = OBJECT_ID(N'bms_web.ExpenseRequestAttachments');
    END;

    IF @TableId IS NULL
        THROW 62709, N'BMS W641 refused: attachment table was not created.', 1;
    IF EXISTS
    (
        SELECT 1 FROM sys.triggers
        WHERE parent_id = @TableId AND is_ms_shipped = 0
    )
        THROW 62710, N'BMS W641 refused: attachment table has an unmanaged trigger.', 1;
    IF EXISTS
    (
        SELECT 1 FROM sys.tables
        WHERE object_id = @TableId
          AND (temporal_type <> 0 OR is_memory_optimized <> 0)
    )
        THROW 62710, N'BMS W641 refused: attachment table has unsupported behavior.', 1;
    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = @TableId) <> 12
        THROW 62711, N'BMS W641 refused: attachment column count differs.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'Id', N'uniqueidentifier', 16, 0, 0, 0, NULL),
                (N'ExpenseRequestId', N'uniqueidentifier', 16, 0, 0, 0, NULL),
                (N'Kind', N'nvarchar', 60, 0, 0, 0, N'Latin1_General_100_BIN2'),
                (N'FileName', N'nvarchar', 510, 0, 0, 0, N'Latin1_General_100_BIN2'),
                (N'FileSizeBytes', N'bigint', 8, 0, 19, 0, NULL),
                (N'ContentSha256', N'binary', 32, 0, 0, 0, NULL),
                (N'Content', N'varbinary', -1, 0, 0, 0, NULL),
                (N'UploadedAtUtc', N'datetimeoffset', 9, 0, 30, 3, NULL),
                (N'UploadedByExternalId', N'nvarchar', 512, 0, 0, 0, N'Latin1_General_100_BIN2'),
                (N'Comment', N'nvarchar', 2000, 1, 0, 0, N'Latin1_General_100_BIN2'),
                (N'IsPostSubmission', N'bit', 1, 0, 0, 0, NULL),
                (N'IsSealed', N'bit', 1, 0, 0, 0, NULL)
        ) AS expected(Name, TypeName, MaxLength, IsNullable, [Precision], Scale, CollationName)
        LEFT JOIN sys.columns AS actual
          ON actual.object_id = @TableId AND actual.name = expected.Name
        WHERE actual.column_id IS NULL
           OR TYPE_NAME(actual.user_type_id) <> expected.TypeName
           OR actual.max_length <> expected.MaxLength
           OR actual.is_nullable <> expected.IsNullable
           OR (expected.[Precision] <> 0 AND actual.[precision] <> expected.[Precision])
           OR (expected.Scale <> 0 AND actual.scale <> expected.Scale)
           OR ISNULL(actual.collation_name, N'') <> ISNULL(expected.CollationName, N'')
           OR actual.is_identity <> 0 OR actual.is_computed <> 0
           OR actual.default_object_id <> 0 OR actual.rule_object_id <> 0
           OR actual.generated_always_type <> 0 OR actual.is_rowguidcol <> 0
           OR actual.is_filestream <> 0 OR actual.is_sparse <> 0
           OR actual.is_column_set <> 0 OR actual.encryption_type IS NOT NULL
           OR actual.is_hidden <> 0 OR actual.is_masked <> 0
    )
        THROW 62712, N'BMS W641 refused: attachment column metadata differs.', 1;

    IF
    (
        SELECT COUNT(*) FROM sys.indexes
        WHERE object_id = @TableId AND index_id > 0
    ) <> 2
        THROW 62713, N'BMS W641 refused: attachment index count differs.', 1;
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = @TableId
          AND name = N'PK_bms_web_ExpenseRequestAttachments'
          AND type = 1 AND is_primary_key = 1 AND is_unique = 1
          AND is_unique_constraint = 0
          AND is_disabled = 0 AND is_hypothetical = 0
          AND has_filter = 0 AND filter_definition IS NULL
          AND ignore_dup_key = 0
    )
        THROW 62714, N'BMS W641 refused: attachment primary key differs.', 1;
    IF
    (
        SELECT STRING_AGG(c.name, N',') WITHIN GROUP (ORDER BY ic.key_ordinal)
        FROM sys.indexes AS i
        INNER JOIN sys.index_columns AS ic
          ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        INNER JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = @TableId
          AND i.name = N'PK_bms_web_ExpenseRequestAttachments'
          AND ic.key_ordinal > 0
    ) COLLATE Latin1_General_100_BIN2 <> N'Id'
       OR EXISTS
       (
           SELECT 1 FROM sys.index_columns AS ic
           INNER JOIN sys.indexes AS i
             ON i.object_id = ic.object_id AND i.index_id = ic.index_id
           WHERE i.object_id = @TableId
             AND i.name = N'PK_bms_web_ExpenseRequestAttachments'
             AND (ic.is_included_column <> 0 OR ic.is_descending_key <> 0)
       )
        THROW 62714, N'BMS W641 refused: attachment primary key shape differs.', 1;

    DECLARE @ListIndexId int = INDEXPROPERTY(
        @TableId,
        N'IX_bms_web_ExpenseRequestAttachments_Request_Time_Id',
        'IndexId');
    IF @ListIndexId IS NULL
        THROW 62715, N'BMS W641 refused: attachment list index is absent.', 1;
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = @TableId
          AND index_id = @ListIndexId
          AND type = 2 AND is_primary_key = 0 AND is_unique = 0
          AND is_unique_constraint = 0
          AND is_disabled = 0 AND is_hypothetical = 0
          AND has_filter = 0 AND filter_definition IS NULL
          AND ignore_dup_key = 0
    )
        THROW 62715, N'BMS W641 refused: attachment list index metadata differs.', 1;
    IF
    (
        SELECT STRING_AGG(
            CONCAT(c.name, N':', ic.is_descending_key), N',')
            WITHIN GROUP (ORDER BY ic.key_ordinal)
        FROM sys.index_columns AS ic
        INNER JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE ic.object_id = @TableId
          AND ic.index_id = @ListIndexId
          AND ic.key_ordinal > 0
    ) COLLATE Latin1_General_100_BIN2 <>
        N'ExpenseRequestId:0,UploadedAtUtc:0,Id:0'
       OR
       (
           SELECT STRING_AGG(c.name, N',') WITHIN GROUP (ORDER BY ic.index_column_id)
           FROM sys.index_columns AS ic
           INNER JOIN sys.columns AS c
             ON c.object_id = ic.object_id AND c.column_id = ic.column_id
           WHERE ic.object_id = @TableId
             AND ic.index_id = @ListIndexId
             AND ic.is_included_column = 1
       ) COLLATE Latin1_General_100_BIN2 <>
        N'Kind,FileName,FileSizeBytes,ContentSha256,UploadedByExternalId,Comment,IsPostSubmission,IsSealed'
        THROW 62716, N'BMS W641 refused: attachment list index shape differs.', 1;

    IF (SELECT COUNT(*) FROM sys.check_constraints
        WHERE parent_object_id = @TableId) <> 4
        THROW 62717, N'BMS W641 refused: attachment check count differs.', 1;

    CREATE TABLE #W641Identity
    (
        Id uniqueidentifier, ExpenseRequestId uniqueidentifier,
        CHECK
        (
            Id <> CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000')
            AND ExpenseRequestId <>
                CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000')
        )
    );
    CREATE TABLE #W641File
    (
        FileName nvarchar(255) COLLATE Latin1_General_100_BIN2,
        FileSizeBytes bigint, ContentSha256 binary(32), Content varbinary(max),
        CHECK
        (
            FileSizeBytes BETWEEN 1 AND 26214400
            AND DATALENGTH(Content) = FileSizeBytes
            AND ContentSha256 = HASHBYTES('SHA2_256', Content)
            AND DATALENGTH(FileName) BETWEEN 2 AND 510
            AND FileName = LTRIM(RTRIM(FileName))
            AND RIGHT(FileName, 1) NOT IN (N'.', N' ')
            AND FileName NOT LIKE N'%[/\\:*?""<>|]%'
            AND PATINDEX(
                N'%[' + NCHAR(1) + N'-' + NCHAR(31)
                    + NCHAR(127) + N'-' + NCHAR(159) + N']%',
                FileName COLLATE Latin1_General_100_BIN2) = 0
        )
    );
    CREATE TABLE #W641Lifecycle
    (
        Kind nvarchar(30) COLLATE Latin1_General_100_BIN2,
        Comment nvarchar(1000) COLLATE Latin1_General_100_BIN2,
        IsPostSubmission bit, IsSealed bit,
        CHECK
        (
            (IsPostSubmission = 0
                AND Kind IN (N'SUPPORTING', N'NEW_CONTRACT')
                AND Comment IS NULL)
            OR
            (IsPostSubmission = 1
                AND Kind = N'ADDITIONAL'
                AND IsSealed = 1
                AND Comment IS NOT NULL
                AND DATALENGTH(Comment) BETWEEN 2 AND 2000
                AND Comment = LTRIM(RTRIM(Comment))
                AND PATINDEX(
                    N'%[' + NCHAR(1) + N'-' + NCHAR(31)
                        + NCHAR(127) + N'-' + NCHAR(159) + N']%',
                    Comment COLLATE Latin1_General_100_BIN2) = 0)
        )
    );
    CREATE TABLE #W641Uploader
    (
        UploadedAtUtc datetimeoffset(3),
        UploadedByExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2,
        CHECK
        (
            DATEPART(TZOFFSET, UploadedAtUtc) = 0
            AND DATALENGTH(UploadedByExternalId) BETWEEN 2 AND 512
            AND UploadedByExternalId = LTRIM(RTRIM(UploadedByExternalId))
            AND PATINDEX(
                N'%[' + NCHAR(1) + N'-' + NCHAR(31)
                    + NCHAR(127) + N'-' + NCHAR(159) + N']%',
                UploadedByExternalId COLLATE Latin1_General_100_BIN2) = 0
        )
    );
    DECLARE @ExpectedChecks TABLE
        (ConstraintName sysname NOT NULL, CanonicalDefinition nvarchar(max) NULL);
    INSERT INTO @ExpectedChecks
    SELECT expected.ConstraintName, actual.definition
    FROM (VALUES
        (N'CK_bms_web_ExpenseRequestAttachments_Identity', N'tempdb..#W641Identity'),
        (N'CK_bms_web_ExpenseRequestAttachments_File', N'tempdb..#W641File'),
        (N'CK_bms_web_ExpenseRequestAttachments_Lifecycle', N'tempdb..#W641Lifecycle'),
        (N'CK_bms_web_ExpenseRequestAttachments_Uploader', N'tempdb..#W641Uploader')
    ) AS expected(ConstraintName, TemporaryTable)
    LEFT JOIN tempdb.sys.check_constraints AS actual
      ON actual.parent_object_id = OBJECT_ID(expected.TemporaryTable);
    IF EXISTS
    (
        SELECT 1 FROM @ExpectedChecks AS expected
        LEFT JOIN sys.check_constraints AS actual
          ON actual.parent_object_id = @TableId
         AND actual.name = expected.ConstraintName
        WHERE actual.object_id IS NULL OR actual.definition IS NULL
           OR expected.CanonicalDefinition IS NULL
           OR actual.is_disabled <> 0 OR actual.is_not_trusted <> 0
           OR actual.is_not_for_replication <> 0 OR actual.is_system_named <> 0
           OR actual.parent_column_id <> 0
           OR
           (SELECT COUNT(DISTINCT normalized_definition COLLATE Latin1_General_100_BIN2)
            FROM (VALUES
              (REPLACE(REPLACE(REPLACE(REPLACE(actual.definition,
                N' ', N''), NCHAR(13), N''), NCHAR(10), N''), NCHAR(9), N'')),
              (REPLACE(REPLACE(REPLACE(REPLACE(expected.CanonicalDefinition,
                N' ', N''), NCHAR(13), N''), NCHAR(10), N''), NCHAR(9), N''))
            ) AS definitions(normalized_definition)) <> 1
    )
        THROW 62717, N'BMS W641 refused: attachment check shape differs.', 1;

    IF (SELECT COUNT(*) FROM sys.foreign_keys
        WHERE parent_object_id = @TableId) <> 1
        THROW 62718, N'BMS W641 refused: attachment foreign key count differs.', 1;
    IF EXISTS
    (
        SELECT 1
        FROM (VALUES
            (N'FK_bms_web_ExpenseRequestAttachments_Request',
             N'ExpenseRequestId', N'Id')
        ) AS expected(ConstraintName, ParentColumnName, ReferencedColumnName)
        LEFT JOIN sys.foreign_keys AS actual_fk
          ON actual_fk.parent_object_id = @TableId
         AND actual_fk.name = expected.ConstraintName
        LEFT JOIN sys.foreign_key_columns AS actual_fkc
          ON actual_fkc.constraint_object_id = actual_fk.object_id
         AND actual_fkc.constraint_column_id = 1
        LEFT JOIN sys.columns AS parent_column
          ON parent_column.object_id = actual_fkc.parent_object_id
         AND parent_column.column_id = actual_fkc.parent_column_id
        LEFT JOIN sys.columns AS referenced_column
          ON referenced_column.object_id = actual_fkc.referenced_object_id
         AND referenced_column.column_id = actual_fkc.referenced_column_id
        WHERE actual_fk.object_id IS NULL
           OR actual_fk.referenced_object_id <> OBJECT_ID(N'bms_web.ExpenseRequests')
           OR actual_fk.is_disabled <> 0 OR actual_fk.is_not_trusted <> 0
           OR actual_fk.is_not_for_replication <> 0 OR actual_fk.is_system_named <> 0
           OR actual_fk.delete_referential_action <> 1
           OR actual_fk.update_referential_action <> 0
           OR parent_column.name IS NULL
           OR parent_column.name <> expected.ParentColumnName
           OR referenced_column.name IS NULL
           OR referenced_column.name <> expected.ReferencedColumnName
           OR (SELECT COUNT(*) FROM sys.foreign_key_columns
               WHERE constraint_object_id = actual_fk.object_id) <> 1
    )
        THROW 62718, N'BMS W641 refused: attachment request ownership differs.', 1;

    IF EXISTS
    (
        SELECT 1 FROM bms_web.ExpenseRequestAttachments WITH (HOLDLOCK)
        WHERE Id = CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000')
           OR ExpenseRequestId = CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000')
           OR FileSizeBytes NOT BETWEEN 1 AND 26214400
           OR DATALENGTH(Content) <> FileSizeBytes
           OR ContentSha256 <> HASHBYTES('SHA2_256', Content)
           OR DATALENGTH(FileName) NOT BETWEEN 2 AND 510
           OR FileName <> LTRIM(RTRIM(FileName))
           OR RIGHT(FileName, 1) IN (N'.', N' ')
           OR FileName LIKE N'%[/\\:*?""<>|]%'
           OR PATINDEX(
                N'%[' + NCHAR(1) + N'-' + NCHAR(31)
                    + NCHAR(127) + N'-' + NCHAR(159) + N']%',
                FileName COLLATE Latin1_General_100_BIN2) <> 0
           OR (IsPostSubmission = 0
               AND (Kind NOT IN (N'SUPPORTING', N'NEW_CONTRACT') OR Comment IS NOT NULL))
           OR (IsPostSubmission = 1
               AND (Kind <> N'ADDITIONAL' OR IsSealed <> 1
                    OR Comment IS NULL
                    OR DATALENGTH(Comment) NOT BETWEEN 2 AND 2000
                    OR Comment <> LTRIM(RTRIM(Comment))
                    OR PATINDEX(
                        N'%[' + NCHAR(1) + N'-' + NCHAR(31)
                            + NCHAR(127) + N'-' + NCHAR(159) + N']%',
                        Comment COLLATE Latin1_General_100_BIN2) <> 0))
           OR DATEPART(TZOFFSET, UploadedAtUtc) <> 0
           OR DATALENGTH(UploadedByExternalId) NOT BETWEEN 2 AND 512
           OR UploadedByExternalId <> LTRIM(RTRIM(UploadedByExternalId))
           OR PATINDEX(
                N'%[' + NCHAR(1) + N'-' + NCHAR(31)
                    + NCHAR(127) + N'-' + NCHAR(159) + N']%',
                UploadedByExternalId COLLATE Latin1_General_100_BIN2) <> 0
    )
        THROW 62719, N'BMS W641 refused: stored attachment data violates its contract.', 1;

    IF @VersionRegistered = 0
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (31, N'031_expense_request_attachments.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
