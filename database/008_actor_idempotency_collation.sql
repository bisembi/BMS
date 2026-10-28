-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web W13 — fail-closed SQL Server identifier semantics release candidate.

    IMPORTANT
    - This script has not been executed against an accounting server.
    - It never changes identifier values. Non-canonical legacy data is reported
      and refused instead of trimmed, folded, merged or deleted.
    - Only the eight dependencies created by 001/003 are temporarily removed.
    - The IdempotencyRecords clustered PK remains exactly 900 declared bytes:
      Scope nvarchar(323) + Key nvarchar(127).
    - U+0000 is rejected by the application boundary. SQL Server Windows-collation
      CHARINDEX/PATINDEX cannot prove U+0000 absence. Database write credentials
      must be limited to the backend service identity and a controlled deployer;
      raw/compromised credential use remains a documented residual.
*/

IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS W13 refused: run this script standalone, outside an ambient transaction.', 16, 1);
    RETURN;
END;

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 58001, N'BMS W13 refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 58002, N'BMS W13 refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
    THROW 58003, N'BMS W13 refused: managed schema bms_web is absent.', 1;

DECLARE @SqlMajorVersion int = TRY_CONVERT(int, SERVERPROPERTY('ProductMajorVersion'));
IF @SqlMajorVersion IS NULL OR @SqlMajorVersion < 13
    THROW 58035, N'BMS W13 refused: SQL Server 2016 or newer is required.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'bms_web.ExpenseRequests', N'U') IS NULL
       OR OBJECT_ID(N'bms_web.BusinessAudit', N'U') IS NULL
       OR OBJECT_ID(N'bms_web.ActorOrganizationGrants', N'U') IS NULL
       OR OBJECT_ID(N'bms_web.ActorCostCenterAccess', N'U') IS NULL
       OR OBJECT_ID(N'bms_web.IdempotencyRecords', N'U') IS NULL
        THROW 58004, N'BMS W13 refused: one or more managed identifier tables are absent.', 1;

    /* Hold every affected table and the version ledger through all preflight and DDL. */
    DECLARE @LockWitness bigint;
    SELECT @LockWitness = COUNT_BIG(*) FROM bms_web.schema_version WITH (TABLOCKX, HOLDLOCK);
    SELECT @LockWitness = COUNT_BIG(*) FROM bms_web.IdempotencyRecords WITH (TABLOCKX, HOLDLOCK);
    SELECT @LockWitness = COUNT_BIG(*) FROM bms_web.ExpenseRequests WITH (TABLOCKX, HOLDLOCK);
    SELECT @LockWitness = COUNT_BIG(*) FROM bms_web.ActorOrganizationGrants WITH (TABLOCKX, HOLDLOCK);
    SELECT @LockWitness = COUNT_BIG(*) FROM bms_web.ActorCostCenterAccess WITH (TABLOCKX, HOLDLOCK);
    SELECT @LockWitness = COUNT_BIG(*) FROM bms_web.BusinessAudit WITH (TABLOCKX, HOLDLOCK);

    /* Exact, ordered migration ownership. */
    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (1, N'001_bootstrap_bms_web.sql'),
                (2, N'002_budget_projection.sql'),
                (3, N'003_access_scope.sql'),
                (4, N'004_workflow_matrix.sql'),
                (5, N'005_expense_request_evidence.sql'),
                (6, N'006_expense_request_copy_source.sql'),
                (7, N'007_expense_request_line_order.sql')
        ) AS expected(version_no, script_name)
        LEFT JOIN bms_web.schema_version AS actual
         ON actual.version_no = expected.version_no
         AND actual.script_name COLLATE Latin1_General_100_BIN2 =
             expected.script_name COLLATE Latin1_General_100_BIN2
         AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
        WHERE actual.version_no IS NULL
    )
        THROW 58005, N'BMS W13 refused: exact schema versions 1 through 7 are required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version
        WHERE version_no = 8
          AND
          (
              script_name COLLATE Latin1_General_100_BIN2 <>
                  N'008_actor_idempotency_collation.sql'
              OR DATALENGTH(script_name) <>
                  DATALENGTH(N'008_actor_idempotency_collation.sql')
          )
    )
        THROW 58006, N'BMS W13 refused: schema version 8 belongs to another script.', 1;

    IF EXISTS (SELECT 1 FROM bms_web.schema_version WHERE version_no > 8)
        THROW 58007, N'BMS W13 refused: a later schema version is already registered.', 1;

    DECLARE @Version8Applied bit = CASE WHEN EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version
        WHERE version_no = 8
          AND script_name COLLATE Latin1_General_100_BIN2 =
              N'008_actor_idempotency_collation.sql'
          AND DATALENGTH(script_name) =
              DATALENGTH(N'008_actor_idempotency_collation.sql')
    ) THEN 1 ELSE 0 END;

    /* Seven columns only; aliases/defaults/computed columns are never adopted. */
    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.ExpenseRequests', N'InitiatorExternalId', N'nvarchar', 512),
                (N'bms_web.BusinessAudit', N'ActorExternalId', N'nvarchar', 512),
                (N'bms_web.ActorOrganizationGrants', N'ActorExternalId', N'nvarchar', 512),
                (N'bms_web.ActorCostCenterAccess', N'ActorExternalId', N'nvarchar', 512),
                (N'bms_web.IdempotencyRecords', N'Scope', N'nvarchar', CASE WHEN @Version8Applied = 1 THEN 646 ELSE 700 END),
                (N'bms_web.IdempotencyRecords', N'Key', N'nvarchar', CASE WHEN @Version8Applied = 1 THEN 254 ELSE 256 END),
                (N'bms_web.IdempotencyRecords', N'RequestHash', N'char', 64)
        ) AS expected(ObjectName, ColumnName, TypeName, MaxLength)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(expected.ObjectName)
         AND c.name = expected.ColumnName
        WHERE c.object_id IS NULL
           OR TYPE_NAME(c.user_type_id) <> expected.TypeName
           OR c.max_length <> expected.MaxLength
           OR c.is_nullable <> 0
           OR c.is_computed <> 0
           OR c.default_object_id <> 0
           OR c.rule_object_id <> 0
           OR c.generated_always_type <> 0
           OR c.is_sparse <> 0
           OR c.is_column_set <> 0
           OR c.encryption_type IS NOT NULL
           OR c.is_hidden <> 0
           OR c.is_masked <> 0
    )
        THROW 58008, N'BMS W13 refused: identifier column type/length/nullability contract is drifted.', 1;

    DECLARE @TargetCollationCount int =
    (
        SELECT COUNT(*)
        FROM
        (
            VALUES
                (N'bms_web.ExpenseRequests', N'InitiatorExternalId'),
                (N'bms_web.BusinessAudit', N'ActorExternalId'),
                (N'bms_web.ActorOrganizationGrants', N'ActorExternalId'),
                (N'bms_web.ActorCostCenterAccess', N'ActorExternalId'),
                (N'bms_web.IdempotencyRecords', N'Scope'),
                (N'bms_web.IdempotencyRecords', N'Key'),
                (N'bms_web.IdempotencyRecords', N'RequestHash')
        ) AS expected(ObjectName, ColumnName)
        JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(expected.ObjectName)
         AND c.name = expected.ColumnName
        WHERE c.collation_name COLLATE Latin1_General_100_BIN2 =
              N'Latin1_General_100_BIN2'
    );

    IF @TargetCollationCount NOT IN (0, 7)
        THROW 58009, N'BMS W13 refused: mixed legacy/BIN2 identifier state requires DBA review.', 1;

    IF @Version8Applied = 1 AND @TargetCollationCount = 0
        THROW 58010, N'BMS W13 refused: identifier metadata and schema version 8 disagree.', 1;

    IF @Version8Applied = 0 AND @TargetCollationCount = 0 AND EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.ExpenseRequests', N'InitiatorExternalId'),
                (N'bms_web.BusinessAudit', N'ActorExternalId'),
                (N'bms_web.ActorOrganizationGrants', N'ActorExternalId'),
                (N'bms_web.ActorCostCenterAccess', N'ActorExternalId'),
                (N'bms_web.IdempotencyRecords', N'Scope'),
                (N'bms_web.IdempotencyRecords', N'Key'),
                (N'bms_web.IdempotencyRecords', N'RequestHash')
        ) AS expected(ObjectName, ColumnName)
        JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(expected.ObjectName)
         AND c.name = expected.ColumnName
        WHERE c.collation_name COLLATE Latin1_General_100_BIN2 <>
              CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation'))
              COLLATE Latin1_General_100_BIN2
    )
        THROW 58011, N'BMS W13 refused: legacy columns do not inherit the current database collation.', 1;

    /* Exact known pre-migration CHECK definitions. */
    DECLARE @ExpenseActorCheck nvarchar(max);
    DECLARE @AuditHashCheck nvarchar(max);
    DECLARE @OrganizationActorCheck nvarchar(max);
    DECLARE @CostCenterActorCheck nvarchar(max);

    SELECT @ExpenseActorCheck = definition
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
      AND name = N'CK_bms_web_ExpenseRequests_actor'
      AND is_disabled = 0 AND is_not_trusted = 0
      AND is_not_for_replication = 0 AND is_system_named = 0
      AND parent_column_id = COLUMNPROPERTY(
          OBJECT_ID(N'bms_web.ExpenseRequests'), N'InitiatorExternalId', N'ColumnId');
    SELECT @AuditHashCheck = definition
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'bms_web.IdempotencyRecords')
      AND name = N'CK_bms_web_IdempotencyRecords_hash'
      AND is_disabled = 0 AND is_not_trusted = 0
      AND is_not_for_replication = 0 AND is_system_named = 0
      AND parent_column_id = COLUMNPROPERTY(
          OBJECT_ID(N'bms_web.IdempotencyRecords'), N'RequestHash', N'ColumnId');
    SELECT @OrganizationActorCheck = definition
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'bms_web.ActorOrganizationGrants')
      AND name = N'CK_bms_web_ActorOrganizationGrants_Actor'
      AND is_disabled = 0 AND is_not_trusted = 0
      AND is_not_for_replication = 0 AND is_system_named = 0
      AND parent_column_id = COLUMNPROPERTY(
          OBJECT_ID(N'bms_web.ActorOrganizationGrants'), N'ActorExternalId', N'ColumnId');
    SELECT @CostCenterActorCheck = definition
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'bms_web.ActorCostCenterAccess')
      AND name = N'CK_bms_web_ActorCostCenterAccess_Actor'
      AND is_disabled = 0 AND is_not_trusted = 0
      AND is_not_for_replication = 0 AND is_system_named = 0
      AND parent_column_id = COLUMNPROPERTY(
          OBJECT_ID(N'bms_web.ActorCostCenterAccess'), N'ActorExternalId', N'ColumnId');

    SELECT @ExpenseActorCheck = REPLACE(@ExpenseActorCheck, N'[InitiatorExternalId]', N'InitiatorExternalId');
    SELECT @AuditHashCheck = REPLACE(@AuditHashCheck, N'[RequestHash]', N'RequestHash');
    SELECT @OrganizationActorCheck = REPLACE(@OrganizationActorCheck, N'[ActorExternalId]', N'ActorExternalId');
    SELECT @CostCenterActorCheck = REPLACE(@CostCenterActorCheck, N'[ActorExternalId]', N'ActorExternalId');
    SELECT @ExpenseActorCheck = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        @ExpenseActorCheck, N'(', N''), N')', N''), N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N'');
    SELECT @AuditHashCheck = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        @AuditHashCheck, N'(', N''), N')', N''), N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N'');
    SELECT @OrganizationActorCheck = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        @OrganizationActorCheck, N'(', N''), N')', N''), N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N'');
    SELECT @CostCenterActorCheck = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        @CostCenterActorCheck, N'(', N''), N')', N''), N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N'');

    IF @Version8Applied = 0 AND
    (
        @ExpenseActorCheck COLLATE Latin1_General_100_BIN2 <>
            N'lenltrimrtrimInitiatorExternalId>0'
        OR @OrganizationActorCheck COLLATE Latin1_General_100_BIN2 <>
            N'lenltrimrtrimActorExternalId>0'
        OR @CostCenterActorCheck COLLATE Latin1_General_100_BIN2 <>
            N'lenltrimrtrimActorExternalId>0'
        OR @AuditHashCheck COLLATE Latin1_General_100_BIN2 <>
            N'NOTRequestHashlike''%[^0-9A-F]%''ANDlenRequestHash=64'
        OR @ExpenseActorCheck IS NULL
        OR @OrganizationActorCheck IS NULL
        OR @CostCenterActorCheck IS NULL
        OR @AuditHashCheck IS NULL
    )
        THROW 58012, N'BMS W13 refused: one of the four managed CHECK dependencies is missing or drifted.', 1;

    /* Exact four managed indexes before DDL: one IX and three clustered PKs. */
    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.ExpenseRequests', N'IX_ExpenseRequests_InitiatorExternalId_CreatedAtUtc', 2, 0, 0, 2),
                (N'bms_web.ActorOrganizationGrants', N'PK_bms_web_ActorOrganizationGrants', 1, 1, 1, 2),
                (N'bms_web.ActorCostCenterAccess', N'PK_bms_web_ActorCostCenterAccess', 1, 1, 1, 3),
                (N'bms_web.IdempotencyRecords', N'PK_bms_web_IdempotencyRecords', 1, 1, 1, 2)
        ) AS expected(ObjectName, IndexName, IndexType, IsUnique, IsPrimaryKey, KeyCount)
        LEFT JOIN sys.indexes AS i
          ON i.object_id = OBJECT_ID(expected.ObjectName)
         AND i.name = expected.IndexName
        WHERE i.object_id IS NULL
           OR i.type <> expected.IndexType
           OR i.is_unique <> expected.IsUnique
           OR i.is_primary_key <> expected.IsPrimaryKey
           OR i.is_disabled <> 0
           OR i.is_hypothetical <> 0
           OR i.has_filter <> 0
           OR i.filter_definition IS NOT NULL
           OR i.ignore_dup_key <> 0
           OR i.fill_factor <> 0
           OR i.is_padded <> 0
           OR i.allow_row_locks <> 1
           OR i.allow_page_locks <> 1
           OR (@SqlMajorVersion >= 15
               AND ISNULL(INDEXPROPERTY(i.object_id, i.name, 'IsOptimizedForSequentialKey'), -1) <> 0)
           OR NOT EXISTS
              (
                  SELECT 1
                  FROM sys.filegroups AS fg
                  JOIN sys.data_spaces AS ds ON ds.data_space_id = fg.data_space_id
                  WHERE fg.data_space_id = i.data_space_id
                    AND fg.is_default = 1
                    AND ds.[type] = N'FG'
              )
           OR (SELECT COUNT(*) FROM sys.partitions AS p
               WHERE p.object_id = i.object_id AND p.index_id = i.index_id) <> 1
           OR EXISTS
              (
                  SELECT 1 FROM sys.partitions AS p
                  WHERE p.object_id = i.object_id AND p.index_id = i.index_id
                    AND p.data_compression <> 0
              )
           OR NOT EXISTS
              (
                  SELECT 1 FROM sys.stats AS s
                  WHERE s.object_id = i.object_id AND s.stats_id = i.index_id
                    AND s.no_recompute = 0 AND s.is_incremental = 0
                    AND s.auto_created = 0 AND s.user_created = 0
              )
           OR (SELECT COUNT(*) FROM sys.index_columns AS ic
               WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id) <> expected.KeyCount
           OR (SELECT COUNT(*) FROM sys.index_columns AS ic
               WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                 AND ic.key_ordinal > 0 AND ic.is_included_column = 0) <> expected.KeyCount
    )
        THROW 58013, N'BMS W13 refused: one of the four managed indexes is missing or drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.ExpenseRequests', N'IX_ExpenseRequests_InitiatorExternalId_CreatedAtUtc', 1, N'InitiatorExternalId'),
                (N'bms_web.ExpenseRequests', N'IX_ExpenseRequests_InitiatorExternalId_CreatedAtUtc', 2, N'CreatedAtUtc'),
                (N'bms_web.ActorOrganizationGrants', N'PK_bms_web_ActorOrganizationGrants', 1, N'ActorExternalId'),
                (N'bms_web.ActorOrganizationGrants', N'PK_bms_web_ActorOrganizationGrants', 2, N'OrganizationId'),
                (N'bms_web.ActorCostCenterAccess', N'PK_bms_web_ActorCostCenterAccess', 1, N'ActorExternalId'),
                (N'bms_web.ActorCostCenterAccess', N'PK_bms_web_ActorCostCenterAccess', 2, N'CostCenterId'),
                (N'bms_web.ActorCostCenterAccess', N'PK_bms_web_ActorCostCenterAccess', 3, N'EffectiveFrom'),
                (N'bms_web.IdempotencyRecords', N'PK_bms_web_IdempotencyRecords', 1, N'Scope'),
                (N'bms_web.IdempotencyRecords', N'PK_bms_web_IdempotencyRecords', 2, N'Key')
        ) AS expected(ObjectName, IndexName, KeyOrdinal, ColumnName)
        LEFT JOIN sys.indexes AS i
          ON i.object_id = OBJECT_ID(expected.ObjectName) AND i.name = expected.IndexName
        LEFT JOIN sys.index_columns AS ic
          ON ic.object_id = i.object_id AND ic.index_id = i.index_id
         AND ic.key_ordinal = expected.KeyOrdinal
        LEFT JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE c.name <> expected.ColumnName OR c.name IS NULL
           OR ic.is_descending_key <> 0 OR ic.is_included_column <> 0
    )
        THROW 58014, N'BMS W13 refused: a managed index key order is drifted.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.key_constraints
        WHERE (parent_object_id = OBJECT_ID(N'bms_web.ActorOrganizationGrants')
               AND name = N'PK_bms_web_ActorOrganizationGrants' AND [type] = N'PK')
           OR (parent_object_id = OBJECT_ID(N'bms_web.ActorCostCenterAccess')
               AND name = N'PK_bms_web_ActorCostCenterAccess' AND [type] = N'PK')
           OR (parent_object_id = OBJECT_ID(N'bms_web.IdempotencyRecords')
               AND name = N'PK_bms_web_IdempotencyRecords' AND [type] = N'PK')
    ) <> 3
        THROW 58015, N'BMS W13 refused: exact managed primary-key constraints are absent.', 1;

    /* Any dependency outside the eight reviewed objects blocks the migration. */
    IF EXISTS
    (
        SELECT 1
        FROM sys.index_columns AS ic
        JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        JOIN sys.indexes AS i
          ON i.object_id = ic.object_id AND i.index_id = ic.index_id
        JOIN
        (
            VALUES
                (OBJECT_ID(N'bms_web.ExpenseRequests'), N'InitiatorExternalId'),
                (OBJECT_ID(N'bms_web.BusinessAudit'), N'ActorExternalId'),
                (OBJECT_ID(N'bms_web.ActorOrganizationGrants'), N'ActorExternalId'),
                (OBJECT_ID(N'bms_web.ActorCostCenterAccess'), N'ActorExternalId'),
                (OBJECT_ID(N'bms_web.IdempotencyRecords'), N'Scope'),
                (OBJECT_ID(N'bms_web.IdempotencyRecords'), N'Key'),
                (OBJECT_ID(N'bms_web.IdempotencyRecords'), N'RequestHash')
        ) AS target(object_id, column_name)
          ON target.object_id = c.object_id AND target.column_name = c.name
        WHERE NOT
        (
            (i.object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
             AND i.name = N'IX_ExpenseRequests_InitiatorExternalId_CreatedAtUtc')
            OR (i.object_id = OBJECT_ID(N'bms_web.ActorOrganizationGrants')
                AND i.name = N'PK_bms_web_ActorOrganizationGrants')
            OR (i.object_id = OBJECT_ID(N'bms_web.ActorCostCenterAccess')
                AND i.name = N'PK_bms_web_ActorCostCenterAccess')
            OR (i.object_id = OBJECT_ID(N'bms_web.IdempotencyRecords')
                AND i.name = N'PK_bms_web_IdempotencyRecords')
        )
    )
        THROW 58016, N'BMS W13 refused: an unknown index depends on an identifier column.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.stats_columns AS sc
        JOIN sys.columns AS c
          ON c.object_id = sc.object_id AND c.column_id = sc.column_id
        JOIN sys.stats AS s
          ON s.object_id = sc.object_id AND s.stats_id = sc.stats_id
        LEFT JOIN sys.indexes AS i
          ON i.object_id = s.object_id AND i.index_id = s.stats_id
        JOIN
        (
            VALUES
                (OBJECT_ID(N'bms_web.ExpenseRequests'), N'InitiatorExternalId'),
                (OBJECT_ID(N'bms_web.BusinessAudit'), N'ActorExternalId'),
                (OBJECT_ID(N'bms_web.ActorOrganizationGrants'), N'ActorExternalId'),
                (OBJECT_ID(N'bms_web.ActorCostCenterAccess'), N'ActorExternalId'),
                (OBJECT_ID(N'bms_web.IdempotencyRecords'), N'Scope'),
                (OBJECT_ID(N'bms_web.IdempotencyRecords'), N'Key'),
                (OBJECT_ID(N'bms_web.IdempotencyRecords'), N'RequestHash')
        ) AS target(object_id, column_name)
          ON target.object_id = c.object_id AND target.column_name = c.name
        WHERE i.object_id IS NULL
           OR NOT
           (
               (i.object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
                AND i.name = N'IX_ExpenseRequests_InitiatorExternalId_CreatedAtUtc')
               OR (i.object_id = OBJECT_ID(N'bms_web.ActorOrganizationGrants')
                   AND i.name = N'PK_bms_web_ActorOrganizationGrants')
               OR (i.object_id = OBJECT_ID(N'bms_web.ActorCostCenterAccess')
                   AND i.name = N'PK_bms_web_ActorCostCenterAccess')
               OR (i.object_id = OBJECT_ID(N'bms_web.IdempotencyRecords')
                   AND i.name = N'PK_bms_web_IdempotencyRecords')
           )
    )
        THROW 58017, N'BMS W13 refused: unknown auto/user statistics depend on an identifier column.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.foreign_key_columns AS fkc
        JOIN sys.columns AS parent_column
          ON parent_column.object_id = fkc.parent_object_id
         AND parent_column.column_id = fkc.parent_column_id
        JOIN sys.columns AS referenced_column
          ON referenced_column.object_id = fkc.referenced_object_id
         AND referenced_column.column_id = fkc.referenced_column_id
        WHERE EXISTS
        (
            SELECT 1
            FROM
            (
                VALUES
                    (OBJECT_ID(N'bms_web.ExpenseRequests'), N'InitiatorExternalId'),
                    (OBJECT_ID(N'bms_web.BusinessAudit'), N'ActorExternalId'),
                    (OBJECT_ID(N'bms_web.ActorOrganizationGrants'), N'ActorExternalId'),
                    (OBJECT_ID(N'bms_web.ActorCostCenterAccess'), N'ActorExternalId'),
                    (OBJECT_ID(N'bms_web.IdempotencyRecords'), N'Scope'),
                    (OBJECT_ID(N'bms_web.IdempotencyRecords'), N'Key'),
                    (OBJECT_ID(N'bms_web.IdempotencyRecords'), N'RequestHash')
            ) AS target(object_id, column_name)
            WHERE (target.object_id = parent_column.object_id
                   AND target.column_name = parent_column.name)
               OR (target.object_id = referenced_column.object_id
                   AND target.column_name = referenced_column.name)
        )
    )
        THROW 58018, N'BMS W13 refused: a foreign key depends on an identifier column.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.sql_expression_dependencies AS d
        JOIN sys.sql_modules AS m ON m.object_id = d.referencing_id
        JOIN sys.columns AS c
          ON c.object_id = d.referenced_id AND c.column_id = d.referenced_minor_id
        WHERE m.is_schema_bound = 1
          AND EXISTS
          (
              SELECT 1
              FROM
              (
                  VALUES
                      (OBJECT_ID(N'bms_web.ExpenseRequests'), N'InitiatorExternalId'),
                      (OBJECT_ID(N'bms_web.BusinessAudit'), N'ActorExternalId'),
                      (OBJECT_ID(N'bms_web.ActorOrganizationGrants'), N'ActorExternalId'),
                      (OBJECT_ID(N'bms_web.ActorCostCenterAccess'), N'ActorExternalId'),
                      (OBJECT_ID(N'bms_web.IdempotencyRecords'), N'Scope'),
                      (OBJECT_ID(N'bms_web.IdempotencyRecords'), N'Key'),
                      (OBJECT_ID(N'bms_web.IdempotencyRecords'), N'RequestHash')
              ) AS target(object_id, column_name)
              WHERE target.object_id = c.object_id AND target.column_name = c.name
          )
    )
        THROW 58019, N'BMS W13 refused: a schema-bound object depends on an identifier column.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.sql_expression_dependencies AS d
        JOIN sys.computed_columns AS computed
          ON computed.object_id = d.referencing_id
         AND computed.column_id = d.referencing_minor_id
        JOIN sys.columns AS c
          ON c.object_id = d.referenced_id AND c.column_id = d.referenced_minor_id
        WHERE EXISTS
        (
            SELECT 1
            FROM
            (
                VALUES
                    (OBJECT_ID(N'bms_web.ExpenseRequests'), N'InitiatorExternalId'),
                    (OBJECT_ID(N'bms_web.BusinessAudit'), N'ActorExternalId'),
                    (OBJECT_ID(N'bms_web.ActorOrganizationGrants'), N'ActorExternalId'),
                    (OBJECT_ID(N'bms_web.ActorCostCenterAccess'), N'ActorExternalId'),
                    (OBJECT_ID(N'bms_web.IdempotencyRecords'), N'Scope'),
                    (OBJECT_ID(N'bms_web.IdempotencyRecords'), N'Key'),
                    (OBJECT_ID(N'bms_web.IdempotencyRecords'), N'RequestHash')
            ) AS target(object_id, column_name)
            WHERE target.object_id = c.object_id AND target.column_name = c.name
        )
    )
        THROW 58020, N'BMS W13 refused: a computed column depends on an identifier column.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints AS cc
        LEFT JOIN sys.sql_expression_dependencies AS d
          ON d.referencing_id = cc.object_id
        WHERE EXISTS
        (
            SELECT 1
            FROM
            (
                VALUES
                    (OBJECT_ID(N'bms_web.ExpenseRequests'), N'InitiatorExternalId'),
                    (OBJECT_ID(N'bms_web.BusinessAudit'), N'ActorExternalId'),
                    (OBJECT_ID(N'bms_web.ActorOrganizationGrants'), N'ActorExternalId'),
                    (OBJECT_ID(N'bms_web.ActorCostCenterAccess'), N'ActorExternalId'),
                    (OBJECT_ID(N'bms_web.IdempotencyRecords'), N'Scope'),
                    (OBJECT_ID(N'bms_web.IdempotencyRecords'), N'Key'),
                    (OBJECT_ID(N'bms_web.IdempotencyRecords'), N'RequestHash')
            ) AS target(object_id, column_name)
            JOIN sys.columns AS c
              ON c.object_id = target.object_id AND c.name = target.column_name
            WHERE cc.parent_object_id = target.object_id
              AND
              (
                  cc.parent_column_id = c.column_id
                  OR (d.referenced_id = target.object_id
                      AND d.referenced_minor_id = c.column_id)
              )
        )
          AND NOT
          (
              (cc.parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
               AND cc.name = N'CK_bms_web_ExpenseRequests_actor')
              OR (cc.parent_object_id = OBJECT_ID(N'bms_web.ActorOrganizationGrants')
                  AND cc.name = N'CK_bms_web_ActorOrganizationGrants_Actor')
              OR (cc.parent_object_id = OBJECT_ID(N'bms_web.ActorCostCenterAccess')
                  AND cc.name = N'CK_bms_web_ActorCostCenterAccess_Actor')
              OR (cc.parent_object_id = OBJECT_ID(N'bms_web.IdempotencyRecords')
                  AND cc.name = N'CK_bms_web_IdempotencyRecords_hash')
              OR (@Version8Applied = 1
                  AND cc.parent_object_id = OBJECT_ID(N'bms_web.BusinessAudit')
                  AND cc.name = N'CK_bms_web_BusinessAudit_Actor')
              OR (@Version8Applied = 1
                  AND cc.parent_object_id = OBJECT_ID(N'bms_web.IdempotencyRecords')
                  AND cc.name IN
                      (N'CK_bms_web_IdempotencyRecords_ScopeKeyCanonical',
                       N'CK_bms_web_IdempotencyRecords_KeyBytes'))
          )
    )
        THROW 58021, N'BMS W13 refused: an unknown CHECK depends on an identifier column.', 1;

    /* Data is inspected only for a not-yet-applied migration. */
    IF @Version8Applied = 0
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM
            (
                SELECT N'ExpenseRequests' AS SourceObject,
                       InitiatorExternalId COLLATE Latin1_General_100_BIN2 AS ActorExternalId
                FROM bms_web.ExpenseRequests
                UNION ALL
                SELECT N'BusinessAudit', ActorExternalId COLLATE Latin1_General_100_BIN2
                FROM bms_web.BusinessAudit
                UNION ALL
                SELECT N'ActorOrganizationGrants', ActorExternalId COLLATE Latin1_General_100_BIN2
                FROM bms_web.ActorOrganizationGrants
                UNION ALL
                SELECT N'ActorCostCenterAccess', ActorExternalId COLLATE Latin1_General_100_BIN2
                FROM bms_web.ActorCostCenterAccess
            ) AS actors
            WHERE DATALENGTH(actors.ActorExternalId) = 0
               OR DATALENGTH(actors.ActorExternalId) <>
                  DATALENGTH(LTRIM(RTRIM(actors.ActorExternalId)))
               OR UNICODE(LEFT(actors.ActorExternalId, 1)) IN
                  (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                   8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                   8232, 8233, 8239, 8287, 12288)
               OR UNICODE(RIGHT(actors.ActorExternalId, 1)) IN
                  (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                   8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                   8232, 8233, 8239, 8287, 12288)
               OR PATINDEX(
                    N'%[' + NCHAR(1) + N'-' + NCHAR(31) + NCHAR(127) + N'-' + NCHAR(159) + N']%',
                    actors.ActorExternalId COLLATE Latin1_General_100_BIN2) > 0
        )
        BEGIN
            SELECT actors.SourceObject, actors.ActorExternalId
            FROM
            (
                SELECT N'ExpenseRequests' AS SourceObject,
                       InitiatorExternalId COLLATE Latin1_General_100_BIN2 AS ActorExternalId
                FROM bms_web.ExpenseRequests
                UNION ALL SELECT N'BusinessAudit', ActorExternalId COLLATE Latin1_General_100_BIN2 FROM bms_web.BusinessAudit
                UNION ALL SELECT N'ActorOrganizationGrants', ActorExternalId COLLATE Latin1_General_100_BIN2 FROM bms_web.ActorOrganizationGrants
                UNION ALL SELECT N'ActorCostCenterAccess', ActorExternalId COLLATE Latin1_General_100_BIN2 FROM bms_web.ActorCostCenterAccess
            ) AS actors
            WHERE DATALENGTH(actors.ActorExternalId) = 0
               OR DATALENGTH(actors.ActorExternalId) <>
                  DATALENGTH(LTRIM(RTRIM(actors.ActorExternalId)))
               OR UNICODE(LEFT(actors.ActorExternalId, 1)) IN
                  (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                   8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                   8232, 8233, 8239, 8287, 12288)
               OR UNICODE(RIGHT(actors.ActorExternalId, 1)) IN
                  (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                   8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                   8232, 8233, 8239, 8287, 12288)
               OR PATINDEX(
                    N'%[' + NCHAR(1) + N'-' + NCHAR(31) + NCHAR(127) + N'-' + NCHAR(159) + N']%',
                    actors.ActorExternalId COLLATE Latin1_General_100_BIN2) > 0
            ORDER BY actors.SourceObject, actors.ActorExternalId;
            THROW 58022, N'BMS W13 refused: blank actor or actor boundary whitespace/control characters require source correction.', 1;
        END;

        IF EXISTS
        (
            SELECT 1
            FROM
            (
                SELECT InitiatorExternalId COLLATE DATABASE_DEFAULT AS LegacyActor,
                       InitiatorExternalId COLLATE Latin1_General_100_BIN2 AS ExactActor
                FROM bms_web.ExpenseRequests
                UNION ALL
                SELECT ActorExternalId COLLATE DATABASE_DEFAULT,
                       ActorExternalId COLLATE Latin1_General_100_BIN2
                FROM bms_web.BusinessAudit
                UNION ALL
                SELECT ActorExternalId COLLATE DATABASE_DEFAULT,
                       ActorExternalId COLLATE Latin1_General_100_BIN2
                FROM bms_web.ActorOrganizationGrants
                UNION ALL
                SELECT ActorExternalId COLLATE DATABASE_DEFAULT,
                       ActorExternalId COLLATE Latin1_General_100_BIN2
                FROM bms_web.ActorCostCenterAccess
            ) AS actors
            GROUP BY actors.LegacyActor
            HAVING COUNT(DISTINCT actors.ExactActor) > 1
        )
        BEGIN
            ;WITH actors AS
            (
                SELECT N'ExpenseRequests' AS SourceObject,
                       InitiatorExternalId COLLATE DATABASE_DEFAULT AS LegacyActor,
                       InitiatorExternalId COLLATE Latin1_General_100_BIN2 AS ExactActor
                FROM bms_web.ExpenseRequests
                UNION ALL SELECT N'BusinessAudit', ActorExternalId COLLATE DATABASE_DEFAULT, ActorExternalId COLLATE Latin1_General_100_BIN2 FROM bms_web.BusinessAudit
                UNION ALL SELECT N'ActorOrganizationGrants', ActorExternalId COLLATE DATABASE_DEFAULT, ActorExternalId COLLATE Latin1_General_100_BIN2 FROM bms_web.ActorOrganizationGrants
                UNION ALL SELECT N'ActorCostCenterAccess', ActorExternalId COLLATE DATABASE_DEFAULT, ActorExternalId COLLATE Latin1_General_100_BIN2 FROM bms_web.ActorCostCenterAccess
            ), collisions AS
            (
                SELECT LegacyActor
                FROM actors
                GROUP BY LegacyActor
                HAVING COUNT(DISTINCT ExactActor) > 1
            )
            SELECT actors.SourceObject, actors.ExactActor AS ActorExternalId
            FROM actors
            JOIN collisions ON collisions.LegacyActor = actors.LegacyActor
            GROUP BY actors.SourceObject, actors.ExactActor
            ORDER BY actors.ExactActor, actors.SourceObject;
            THROW 58023, N'BMS W13 refused: legacy-equivalent actor spellings become distinct under BIN2.', 1;
        END;

        IF EXISTS
        (
            SELECT 1
            FROM bms_web.IdempotencyRecords
            WHERE DATALENGTH(Scope) = 0 OR DATALENGTH([Key]) = 0
               OR DATALENGTH(Scope) <> DATALENGTH(LTRIM(RTRIM(Scope)))
               OR DATALENGTH([Key]) <> DATALENGTH(LTRIM(RTRIM([Key])))
               OR DATALENGTH(Scope) > 646
               OR DATALENGTH([Key]) > 254
               OR DATALENGTH(Scope) + DATALENGTH([Key]) > 900
               OR UNICODE(LEFT(Scope, 1)) IN
                  (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                   8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                   8232, 8233, 8239, 8287, 12288)
               OR UNICODE(RIGHT(Scope, 1)) IN
                  (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                   8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                   8232, 8233, 8239, 8287, 12288)
               OR UNICODE(LEFT([Key], 1)) IN
                  (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                   8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                   8232, 8233, 8239, 8287, 12288)
               OR UNICODE(RIGHT([Key], 1)) IN
                  (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                   8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                   8232, 8233, 8239, 8287, 12288)
               OR PATINDEX(
                    N'%[' + NCHAR(1) + N'-' + NCHAR(31) + NCHAR(127) + N'-' + NCHAR(159) + N']%',
                    Scope COLLATE Latin1_General_100_BIN2) > 0
               OR PATINDEX(
                    N'%[' + NCHAR(1) + N'-' + NCHAR(31) + NCHAR(127) + N'-' + NCHAR(159) + N']%',
                    [Key] COLLATE Latin1_General_100_BIN2) > 0
        )
        BEGIN
            SELECT Scope, [Key], DATALENGTH(Scope) AS ScopeBytes,
                   DATALENGTH([Key]) AS KeyBytes,
                   DATALENGTH(Scope) + DATALENGTH([Key]) AS CompositeKeyBytes
            FROM bms_web.IdempotencyRecords
            WHERE DATALENGTH(Scope) = 0 OR DATALENGTH([Key]) = 0
               OR DATALENGTH(Scope) <> DATALENGTH(LTRIM(RTRIM(Scope)))
               OR DATALENGTH([Key]) <> DATALENGTH(LTRIM(RTRIM([Key])))
               OR DATALENGTH(Scope) > 646 OR DATALENGTH([Key]) > 254
               OR DATALENGTH(Scope) + DATALENGTH([Key]) > 900
               OR UNICODE(LEFT(Scope, 1)) IN
                  (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                   8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                   8232, 8233, 8239, 8287, 12288)
               OR UNICODE(RIGHT(Scope, 1)) IN
                  (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                   8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                   8232, 8233, 8239, 8287, 12288)
               OR UNICODE(LEFT([Key], 1)) IN
                  (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                   8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                   8232, 8233, 8239, 8287, 12288)
               OR UNICODE(RIGHT([Key], 1)) IN
                  (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                   8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                   8232, 8233, 8239, 8287, 12288)
               OR PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + NCHAR(127) + N'-' + NCHAR(159) + N']%', Scope COLLATE Latin1_General_100_BIN2) > 0
               OR PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + NCHAR(127) + N'-' + NCHAR(159) + N']%', [Key] COLLATE Latin1_General_100_BIN2) > 0;
            THROW 58024, N'BMS W13 refused: idempotency scope/key cannot be represented canonically inside the 900-byte PK.', 1;
        END;

        IF EXISTS
        (
            SELECT 1
            FROM bms_web.IdempotencyRecords
            WHERE DATALENGTH(RequestHash) <> 64
               OR RequestHash COLLATE Latin1_General_100_BIN2 LIKE '%[^0-9A-F]%'
        )
        BEGIN
            SELECT Scope, [Key], RequestHash
            FROM bms_web.IdempotencyRecords
            WHERE DATALENGTH(RequestHash) <> 64
               OR RequestHash COLLATE Latin1_General_100_BIN2 LIKE '%[^0-9A-F]%';
            THROW 58025, N'BMS W13 refused: RequestHash must be exactly 64 uppercase hexadecimal bytes.', 1;
        END;

        IF EXISTS
        (
            SELECT 1
            FROM bms_web.ActorOrganizationGrants
            GROUP BY ActorExternalId COLLATE Latin1_General_100_BIN2, OrganizationId
            HAVING COUNT(*) > 1
        )
           OR EXISTS
        (
            SELECT 1
            FROM bms_web.ActorCostCenterAccess
            GROUP BY ActorExternalId COLLATE Latin1_General_100_BIN2, CostCenterId, EffectiveFrom
            HAVING COUNT(*) > 1
        )
           OR EXISTS
        (
            SELECT 1
            FROM bms_web.IdempotencyRecords
            GROUP BY Scope COLLATE Latin1_General_100_BIN2, [Key] COLLATE Latin1_General_100_BIN2
            HAVING COUNT(*) > 1
        )
            THROW 58026, N'BMS W13 refused: a target BIN2 primary key would not be unique.', 1;

        /* Exact allowlist: four CHECKs, three PKs and one IX. */
        DROP INDEX [IX_ExpenseRequests_InitiatorExternalId_CreatedAtUtc]
            ON bms_web.ExpenseRequests;
        ALTER TABLE bms_web.ActorCostCenterAccess
            DROP CONSTRAINT [PK_bms_web_ActorCostCenterAccess];
        ALTER TABLE bms_web.ActorOrganizationGrants
            DROP CONSTRAINT [PK_bms_web_ActorOrganizationGrants];
        ALTER TABLE bms_web.IdempotencyRecords
            DROP CONSTRAINT [PK_bms_web_IdempotencyRecords];
        ALTER TABLE bms_web.ExpenseRequests
            DROP CONSTRAINT [CK_bms_web_ExpenseRequests_actor];
        ALTER TABLE bms_web.ActorOrganizationGrants
            DROP CONSTRAINT [CK_bms_web_ActorOrganizationGrants_Actor];
        ALTER TABLE bms_web.ActorCostCenterAccess
            DROP CONSTRAINT [CK_bms_web_ActorCostCenterAccess_Actor];
        ALTER TABLE bms_web.IdempotencyRecords
            DROP CONSTRAINT [CK_bms_web_IdempotencyRecords_hash];

        ALTER TABLE bms_web.ExpenseRequests ALTER COLUMN
            InitiatorExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL;
        ALTER TABLE bms_web.BusinessAudit ALTER COLUMN
            ActorExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL;
        ALTER TABLE bms_web.ActorOrganizationGrants ALTER COLUMN
            ActorExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL;
        ALTER TABLE bms_web.ActorCostCenterAccess ALTER COLUMN
            ActorExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL;
        ALTER TABLE bms_web.IdempotencyRecords ALTER COLUMN
            Scope nvarchar(323) COLLATE Latin1_General_100_BIN2 NOT NULL;
        ALTER TABLE bms_web.IdempotencyRecords ALTER COLUMN
            [Key] nvarchar(127) COLLATE Latin1_General_100_BIN2 NOT NULL;
        ALTER TABLE bms_web.IdempotencyRecords ALTER COLUMN
            RequestHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL;

        ALTER TABLE bms_web.ExpenseRequests WITH CHECK ADD
            CONSTRAINT CK_bms_web_ExpenseRequests_actor CHECK
            (LEN(LTRIM(RTRIM(InitiatorExternalId))) > 0
             AND DATALENGTH(InitiatorExternalId) = DATALENGTH(LTRIM(RTRIM(InitiatorExternalId)))
             AND UNICODE(LEFT(InitiatorExternalId, 1)) NOT IN
                 (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                  8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                  8232, 8233, 8239, 8287, 12288)
             AND UNICODE(RIGHT(InitiatorExternalId, 1)) NOT IN
                 (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                  8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                  8232, 8233, 8239, 8287, 12288)
             AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + NCHAR(127) + N'-' + NCHAR(159) + N']%',
                          InitiatorExternalId COLLATE Latin1_General_100_BIN2) = 0);
        ALTER TABLE bms_web.BusinessAudit WITH CHECK ADD
            CONSTRAINT CK_bms_web_BusinessAudit_Actor CHECK
            (LEN(LTRIM(RTRIM(ActorExternalId))) > 0
             AND DATALENGTH(ActorExternalId) = DATALENGTH(LTRIM(RTRIM(ActorExternalId)))
             AND UNICODE(LEFT(ActorExternalId, 1)) NOT IN
                 (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                  8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                  8232, 8233, 8239, 8287, 12288)
             AND UNICODE(RIGHT(ActorExternalId, 1)) NOT IN
                 (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                  8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                  8232, 8233, 8239, 8287, 12288)
             AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + NCHAR(127) + N'-' + NCHAR(159) + N']%',
                          ActorExternalId COLLATE Latin1_General_100_BIN2) = 0);
        ALTER TABLE bms_web.ActorOrganizationGrants WITH CHECK ADD
            CONSTRAINT CK_bms_web_ActorOrganizationGrants_Actor CHECK
            (LEN(LTRIM(RTRIM(ActorExternalId))) > 0
             AND DATALENGTH(ActorExternalId) = DATALENGTH(LTRIM(RTRIM(ActorExternalId)))
             AND UNICODE(LEFT(ActorExternalId, 1)) NOT IN
                 (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                  8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                  8232, 8233, 8239, 8287, 12288)
             AND UNICODE(RIGHT(ActorExternalId, 1)) NOT IN
                 (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                  8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                  8232, 8233, 8239, 8287, 12288)
             AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + NCHAR(127) + N'-' + NCHAR(159) + N']%',
                          ActorExternalId COLLATE Latin1_General_100_BIN2) = 0);
        ALTER TABLE bms_web.ActorCostCenterAccess WITH CHECK ADD
            CONSTRAINT CK_bms_web_ActorCostCenterAccess_Actor CHECK
            (LEN(LTRIM(RTRIM(ActorExternalId))) > 0
             AND DATALENGTH(ActorExternalId) = DATALENGTH(LTRIM(RTRIM(ActorExternalId)))
             AND UNICODE(LEFT(ActorExternalId, 1)) NOT IN
                 (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                  8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                  8232, 8233, 8239, 8287, 12288)
             AND UNICODE(RIGHT(ActorExternalId, 1)) NOT IN
                 (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                  8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                  8232, 8233, 8239, 8287, 12288)
             AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + NCHAR(127) + N'-' + NCHAR(159) + N']%',
                          ActorExternalId COLLATE Latin1_General_100_BIN2) = 0);
        ALTER TABLE bms_web.IdempotencyRecords WITH CHECK ADD
            CONSTRAINT CK_bms_web_IdempotencyRecords_hash CHECK
            (RequestHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9A-F]%'
             AND LEN(RequestHash) = 64);
        ALTER TABLE bms_web.IdempotencyRecords WITH CHECK ADD
            CONSTRAINT CK_bms_web_IdempotencyRecords_ScopeKeyCanonical CHECK
            (DATALENGTH(Scope) > 0 AND DATALENGTH([Key]) > 0
             AND DATALENGTH(Scope) = DATALENGTH(LTRIM(RTRIM(Scope)))
             AND DATALENGTH([Key]) = DATALENGTH(LTRIM(RTRIM([Key])))
             AND UNICODE(LEFT(Scope, 1)) NOT IN
                 (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                  8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                  8232, 8233, 8239, 8287, 12288)
             AND UNICODE(RIGHT(Scope, 1)) NOT IN
                 (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                  8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                  8232, 8233, 8239, 8287, 12288)
             AND UNICODE(LEFT([Key], 1)) NOT IN
                 (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                  8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                  8232, 8233, 8239, 8287, 12288)
             AND UNICODE(RIGHT([Key], 1)) NOT IN
                 (9, 10, 11, 12, 13, 32, 133, 160, 5760,
                  8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                  8232, 8233, 8239, 8287, 12288)
             AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + NCHAR(127) + N'-' + NCHAR(159) + N']%',
                          Scope COLLATE Latin1_General_100_BIN2) = 0
             AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + NCHAR(127) + N'-' + NCHAR(159) + N']%',
                          [Key] COLLATE Latin1_General_100_BIN2) = 0);
        ALTER TABLE bms_web.IdempotencyRecords WITH CHECK ADD
            CONSTRAINT CK_bms_web_IdempotencyRecords_KeyBytes CHECK
            (DATALENGTH(Scope) + DATALENGTH([Key]) <= 900);

        ALTER TABLE bms_web.ActorOrganizationGrants ADD
            CONSTRAINT PK_bms_web_ActorOrganizationGrants PRIMARY KEY CLUSTERED
            (ActorExternalId ASC, OrganizationId ASC);
        ALTER TABLE bms_web.ActorCostCenterAccess ADD
            CONSTRAINT PK_bms_web_ActorCostCenterAccess PRIMARY KEY CLUSTERED
            (ActorExternalId ASC, CostCenterId ASC, EffectiveFrom ASC);
        ALTER TABLE bms_web.IdempotencyRecords ADD
            CONSTRAINT PK_bms_web_IdempotencyRecords PRIMARY KEY CLUSTERED
            (Scope ASC, [Key] ASC);
        CREATE NONCLUSTERED INDEX IX_ExpenseRequests_InitiatorExternalId_CreatedAtUtc
            ON bms_web.ExpenseRequests (InitiatorExternalId ASC, CreatedAtUtc ASC);
    END;

    /* Final seven-column metadata contract. */
    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.ExpenseRequests', N'InitiatorExternalId', N'nvarchar', 512),
                (N'bms_web.BusinessAudit', N'ActorExternalId', N'nvarchar', 512),
                (N'bms_web.ActorOrganizationGrants', N'ActorExternalId', N'nvarchar', 512),
                (N'bms_web.ActorCostCenterAccess', N'ActorExternalId', N'nvarchar', 512),
                (N'bms_web.IdempotencyRecords', N'Scope', N'nvarchar', 646),
                (N'bms_web.IdempotencyRecords', N'Key', N'nvarchar', 254),
                (N'bms_web.IdempotencyRecords', N'RequestHash', N'char', 64)
        ) AS expected(ObjectName, ColumnName, TypeName, MaxLength)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(expected.ObjectName) AND c.name = expected.ColumnName
        WHERE c.object_id IS NULL OR TYPE_NAME(c.user_type_id) <> expected.TypeName
           OR c.max_length <> expected.MaxLength OR c.is_nullable <> 0
           OR c.is_computed <> 0 OR c.default_object_id <> 0
           OR c.rule_object_id <> 0 OR c.generated_always_type <> 0
           OR c.is_sparse <> 0 OR c.is_column_set <> 0
           OR c.encryption_type IS NOT NULL OR c.is_hidden <> 0 OR c.is_masked <> 0
           OR c.collation_name COLLATE Latin1_General_100_BIN2 <>
              N'Latin1_General_100_BIN2'
    )
        THROW 58027, N'BMS W13 refused: final seven-column BIN2 metadata contract is not exact.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.ExpenseRequests', N'IX_ExpenseRequests_InitiatorExternalId_CreatedAtUtc', 2, 0, 0, 2),
                (N'bms_web.ActorOrganizationGrants', N'PK_bms_web_ActorOrganizationGrants', 1, 1, 1, 2),
                (N'bms_web.ActorCostCenterAccess', N'PK_bms_web_ActorCostCenterAccess', 1, 1, 1, 3),
                (N'bms_web.IdempotencyRecords', N'PK_bms_web_IdempotencyRecords', 1, 1, 1, 2)
        ) AS expected(ObjectName, IndexName, IndexType, IsUnique, IsPrimaryKey, KeyCount)
        LEFT JOIN sys.indexes AS i
          ON i.object_id = OBJECT_ID(expected.ObjectName) AND i.name = expected.IndexName
        WHERE i.object_id IS NULL OR i.type <> expected.IndexType
           OR i.is_unique <> expected.IsUnique OR i.is_primary_key <> expected.IsPrimaryKey
           OR i.is_disabled <> 0 OR i.is_hypothetical <> 0 OR i.has_filter <> 0
           OR i.filter_definition IS NOT NULL OR i.ignore_dup_key <> 0
           OR i.fill_factor <> 0 OR i.is_padded <> 0
           OR i.allow_row_locks <> 1 OR i.allow_page_locks <> 1
           OR (@SqlMajorVersion >= 15
               AND ISNULL(INDEXPROPERTY(i.object_id, i.name, 'IsOptimizedForSequentialKey'), -1) <> 0)
           OR NOT EXISTS
              (
                  SELECT 1
                  FROM sys.filegroups AS fg
                  JOIN sys.data_spaces AS ds ON ds.data_space_id = fg.data_space_id
                  WHERE fg.data_space_id = i.data_space_id
                    AND fg.is_default = 1 AND ds.[type] = N'FG'
              )
           OR (SELECT COUNT(*) FROM sys.partitions AS p
               WHERE p.object_id = i.object_id AND p.index_id = i.index_id) <> 1
           OR EXISTS
              (
                  SELECT 1 FROM sys.partitions AS p
                  WHERE p.object_id = i.object_id AND p.index_id = i.index_id
                    AND p.data_compression <> 0
              )
           OR NOT EXISTS
              (
                  SELECT 1 FROM sys.stats AS s
                  WHERE s.object_id = i.object_id AND s.stats_id = i.index_id
                    AND s.no_recompute = 0 AND s.is_incremental = 0
                    AND s.auto_created = 0 AND s.user_created = 0
              )
           OR (SELECT COUNT(*) FROM sys.index_columns AS ic
               WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id) <> expected.KeyCount
    )
        THROW 58028, N'BMS W13 refused: final PK/index type or shape is not exact.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.ExpenseRequests', N'IX_ExpenseRequests_InitiatorExternalId_CreatedAtUtc', 1, N'InitiatorExternalId'),
                (N'bms_web.ExpenseRequests', N'IX_ExpenseRequests_InitiatorExternalId_CreatedAtUtc', 2, N'CreatedAtUtc'),
                (N'bms_web.ActorOrganizationGrants', N'PK_bms_web_ActorOrganizationGrants', 1, N'ActorExternalId'),
                (N'bms_web.ActorOrganizationGrants', N'PK_bms_web_ActorOrganizationGrants', 2, N'OrganizationId'),
                (N'bms_web.ActorCostCenterAccess', N'PK_bms_web_ActorCostCenterAccess', 1, N'ActorExternalId'),
                (N'bms_web.ActorCostCenterAccess', N'PK_bms_web_ActorCostCenterAccess', 2, N'CostCenterId'),
                (N'bms_web.ActorCostCenterAccess', N'PK_bms_web_ActorCostCenterAccess', 3, N'EffectiveFrom'),
                (N'bms_web.IdempotencyRecords', N'PK_bms_web_IdempotencyRecords', 1, N'Scope'),
                (N'bms_web.IdempotencyRecords', N'PK_bms_web_IdempotencyRecords', 2, N'Key')
        ) AS expected(ObjectName, IndexName, KeyOrdinal, ColumnName)
        LEFT JOIN sys.indexes AS i
          ON i.object_id = OBJECT_ID(expected.ObjectName) AND i.name = expected.IndexName
        LEFT JOIN sys.index_columns AS ic
          ON ic.object_id = i.object_id AND ic.index_id = i.index_id
         AND ic.key_ordinal = expected.KeyOrdinal
        LEFT JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE c.name <> expected.ColumnName OR c.name IS NULL
           OR ic.is_descending_key <> 0 OR ic.is_included_column <> 0
    )
        THROW 58029, N'BMS W13 refused: final PK/index key order is not exact.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.check_constraints
        WHERE is_disabled = 0 AND is_not_trusted = 0
          AND is_not_for_replication = 0 AND is_system_named = 0
          AND
          (
              (parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequests') AND name = N'CK_bms_web_ExpenseRequests_actor')
              OR (parent_object_id = OBJECT_ID(N'bms_web.BusinessAudit') AND name = N'CK_bms_web_BusinessAudit_Actor')
              OR (parent_object_id = OBJECT_ID(N'bms_web.ActorOrganizationGrants') AND name = N'CK_bms_web_ActorOrganizationGrants_Actor')
              OR (parent_object_id = OBJECT_ID(N'bms_web.ActorCostCenterAccess') AND name = N'CK_bms_web_ActorCostCenterAccess_Actor')
              OR (parent_object_id = OBJECT_ID(N'bms_web.IdempotencyRecords') AND name = N'CK_bms_web_IdempotencyRecords_hash')
              OR (parent_object_id = OBJECT_ID(N'bms_web.IdempotencyRecords') AND name = N'CK_bms_web_IdempotencyRecords_ScopeKeyCanonical')
              OR (parent_object_id = OBJECT_ID(N'bms_web.IdempotencyRecords') AND name = N'CK_bms_web_IdempotencyRecords_KeyBytes')
          )
    ) <> 7
        THROW 58030, N'BMS W13 refused: final trusted identifier CHECK set is incomplete.', 1;

    DECLARE @PostExpenseActorCheck nvarchar(max);
    DECLARE @PostBusinessAuditActorCheck nvarchar(max);
    DECLARE @PostOrganizationActorCheck nvarchar(max);
    DECLARE @PostCostCenterActorCheck nvarchar(max);
    DECLARE @PostHashCheck nvarchar(max);
    DECLARE @PostScopeKeyCheck nvarchar(max);
    DECLARE @PostKeyBytesCheck nvarchar(max);

    SELECT @PostExpenseActorCheck = definition FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
      AND name = N'CK_bms_web_ExpenseRequests_actor';
    SELECT @PostBusinessAuditActorCheck = definition FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'bms_web.BusinessAudit')
      AND name = N'CK_bms_web_BusinessAudit_Actor';
    SELECT @PostOrganizationActorCheck = definition FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'bms_web.ActorOrganizationGrants')
      AND name = N'CK_bms_web_ActorOrganizationGrants_Actor';
    SELECT @PostCostCenterActorCheck = definition FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'bms_web.ActorCostCenterAccess')
      AND name = N'CK_bms_web_ActorCostCenterAccess_Actor';
    SELECT @PostHashCheck = definition FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'bms_web.IdempotencyRecords')
      AND name = N'CK_bms_web_IdempotencyRecords_hash';
    SELECT @PostScopeKeyCheck = definition FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'bms_web.IdempotencyRecords')
      AND name = N'CK_bms_web_IdempotencyRecords_ScopeKeyCanonical';
    SELECT @PostKeyBytesCheck = definition FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'bms_web.IdempotencyRecords')
      AND name = N'CK_bms_web_IdempotencyRecords_KeyBytes';

    SELECT @PostExpenseActorCheck = REPLACE(@PostExpenseActorCheck, N'[InitiatorExternalId]', N'InitiatorExternalId');
    SELECT @PostBusinessAuditActorCheck = REPLACE(@PostBusinessAuditActorCheck, N'[ActorExternalId]', N'ActorExternalId');
    SELECT @PostOrganizationActorCheck = REPLACE(@PostOrganizationActorCheck, N'[ActorExternalId]', N'ActorExternalId');
    SELECT @PostCostCenterActorCheck = REPLACE(@PostCostCenterActorCheck, N'[ActorExternalId]', N'ActorExternalId');
    SELECT @PostHashCheck = REPLACE(@PostHashCheck, N'[RequestHash]', N'RequestHash');
    SELECT @PostScopeKeyCheck = REPLACE(REPLACE(
        @PostScopeKeyCheck, N'[Scope]', N'Scope'), N'[Key]', N'Key');
    SELECT @PostKeyBytesCheck = REPLACE(REPLACE(
        @PostKeyBytesCheck, N'[Scope]', N'Scope'), N'[Key]', N'Key');
    SELECT @PostExpenseActorCheck = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        @PostExpenseActorCheck, N'(', N''), N')', N''), N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N'');
    SELECT @PostBusinessAuditActorCheck = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        @PostBusinessAuditActorCheck, N'(', N''), N')', N''), N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N'');
    SELECT @PostOrganizationActorCheck = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        @PostOrganizationActorCheck, N'(', N''), N')', N''), N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N'');
    SELECT @PostCostCenterActorCheck = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        @PostCostCenterActorCheck, N'(', N''), N')', N''), N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N'');
    SELECT @PostHashCheck = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        @PostHashCheck, N'(', N''), N')', N''), N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N'');
    SELECT @PostScopeKeyCheck = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        @PostScopeKeyCheck, N'(', N''), N')', N''), N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N'');
    SELECT @PostKeyBytesCheck = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        @PostKeyBytesCheck, N'(', N''), N')', N''), N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N'');

    DECLARE @ExpectedActorCheckTemplate nvarchar(max) =
        N'LENLTRIMRTRIM$>0ANDDATALENGTH$=DATALENGTHLTRIMRTRIM$ANDUNICODELEFT$,1NOTIN9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288ANDUNICODERIGHT$,1NOTIN9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288ANDPATINDEXN''%[''+NCHAR1+N''-''+NCHAR31+NCHAR127+N''-''+NCHAR159+N'']%'',$COLLATELatin1_General_100_BIN2=0';
    DECLARE @ExpectedInitiatorCheck nvarchar(max) =
        REPLACE(@ExpectedActorCheckTemplate, N'$', N'InitiatorExternalId');
    DECLARE @ExpectedActorCheck nvarchar(max) =
        REPLACE(@ExpectedActorCheckTemplate, N'$', N'ActorExternalId');
    DECLARE @ExpectedScopeKeyCheck nvarchar(max) =
        N'DATALENGTHScope>0ANDDATALENGTHKey>0ANDDATALENGTHScope=DATALENGTHLTRIMRTRIMScopeANDDATALENGTHKey=DATALENGTHLTRIMRTRIMKeyANDUNICODELEFTScope,1NOTIN9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288ANDUNICODERIGHTScope,1NOTIN9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288ANDUNICODELEFTKey,1NOTIN9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288ANDUNICODERIGHTKey,1NOTIN9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288ANDPATINDEXN''%[''+NCHAR1+N''-''+NCHAR31+NCHAR127+N''-''+NCHAR159+N'']%'',ScopeCOLLATELatin1_General_100_BIN2=0ANDPATINDEXN''%[''+NCHAR1+N''-''+NCHAR31+NCHAR127+N''-''+NCHAR159+N'']%'',KeyCOLLATELatin1_General_100_BIN2=0';

    /* SQL Server canonicalizes equivalent CHECK syntax before catalog storage. */
    IF HASHBYTES(N'SHA2_256', CONVERT(varbinary(max), @PostExpenseActorCheck)) <>
           0xF50FA48178CB6C519792DEF105FB9EE607424D1AC68CCE915BBF0A2307388D5D
       OR HASHBYTES(N'SHA2_256', CONVERT(varbinary(max), @PostBusinessAuditActorCheck)) <>
           0xA21537EAA13CBC63A1BC163B1CA4A6FB2B1DF070350A43C533FCB10C73FEBED1
       OR HASHBYTES(N'SHA2_256', CONVERT(varbinary(max), @PostOrganizationActorCheck)) <>
           0xA21537EAA13CBC63A1BC163B1CA4A6FB2B1DF070350A43C533FCB10C73FEBED1
       OR HASHBYTES(N'SHA2_256', CONVERT(varbinary(max), @PostCostCenterActorCheck)) <>
           0xA21537EAA13CBC63A1BC163B1CA4A6FB2B1DF070350A43C533FCB10C73FEBED1
       OR HASHBYTES(N'SHA2_256', CONVERT(varbinary(max), @PostHashCheck)) <>
           0x838240DEE5076D11CB4BF62F349071B46B8A871D3C2405D36E62FF12178DE27A
       OR HASHBYTES(N'SHA2_256', CONVERT(varbinary(max), @PostScopeKeyCheck)) <>
           0xCEC57B4E8494E16B21D9BCB06B01310B59AFE42DBACEF55B83653BC5B8D5BBE1
       OR HASHBYTES(N'SHA2_256', CONVERT(varbinary(max), @PostKeyBytesCheck)) <>
           0xADB569BEAF4364CE299625FC28CFE88394DA2AB8FFD540FA4F3BA2B614F5F6AE
       OR EXISTS
       (
           SELECT 1
           FROM
           (
               VALUES
                   (N'bms_web.ExpenseRequests', N'CK_bms_web_ExpenseRequests_actor', N'InitiatorExternalId'),
                   (N'bms_web.BusinessAudit', N'CK_bms_web_BusinessAudit_Actor', N'ActorExternalId'),
                   (N'bms_web.ActorOrganizationGrants', N'CK_bms_web_ActorOrganizationGrants_Actor', N'ActorExternalId'),
                   (N'bms_web.ActorCostCenterAccess', N'CK_bms_web_ActorCostCenterAccess_Actor', N'ActorExternalId'),
                   (N'bms_web.IdempotencyRecords', N'CK_bms_web_IdempotencyRecords_hash', N'RequestHash'),
                   (N'bms_web.IdempotencyRecords', N'CK_bms_web_IdempotencyRecords_ScopeKeyCanonical', N''),
                   (N'bms_web.IdempotencyRecords', N'CK_bms_web_IdempotencyRecords_KeyBytes', N'')
           ) AS expected(ObjectName, ConstraintName, ParentColumnName)
           LEFT JOIN sys.check_constraints AS cc
             ON cc.parent_object_id = OBJECT_ID(expected.ObjectName)
            AND cc.name = expected.ConstraintName
           LEFT JOIN sys.columns AS c
             ON c.object_id = cc.parent_object_id
            AND c.column_id = cc.parent_column_id
           WHERE cc.object_id IS NULL
              OR (expected.ParentColumnName = N'' AND cc.parent_column_id <> 0)
              OR (expected.ParentColumnName <> N''
                  AND (c.name IS NULL OR c.name <> expected.ParentColumnName))
       )
       OR @PostExpenseActorCheck IS NULL OR @PostBusinessAuditActorCheck IS NULL
       OR @PostOrganizationActorCheck IS NULL OR @PostCostCenterActorCheck IS NULL
       OR @PostHashCheck IS NULL OR @PostScopeKeyCheck IS NULL OR @PostKeyBytesCheck IS NULL
        THROW 58031, N'BMS W13 refused: final identifier CHECK definitions are drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            SELECT InitiatorExternalId COLLATE Latin1_General_100_BIN2 AS ActorExternalId
            FROM bms_web.ExpenseRequests
            UNION ALL SELECT ActorExternalId COLLATE Latin1_General_100_BIN2 FROM bms_web.BusinessAudit
            UNION ALL SELECT ActorExternalId COLLATE Latin1_General_100_BIN2 FROM bms_web.ActorOrganizationGrants
            UNION ALL SELECT ActorExternalId COLLATE Latin1_General_100_BIN2 FROM bms_web.ActorCostCenterAccess
        ) AS actors
        WHERE DATALENGTH(actors.ActorExternalId) = 0
           OR DATALENGTH(actors.ActorExternalId) <>
              DATALENGTH(LTRIM(RTRIM(actors.ActorExternalId)))
           OR UNICODE(LEFT(actors.ActorExternalId, 1)) IN
              (9, 10, 11, 12, 13, 32, 133, 160, 5760,
               8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
               8232, 8233, 8239, 8287, 12288)
           OR UNICODE(RIGHT(actors.ActorExternalId, 1)) IN
              (9, 10, 11, 12, 13, 32, 133, 160, 5760,
               8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
               8232, 8233, 8239, 8287, 12288)
           OR PATINDEX(
                N'%[' + NCHAR(1) + N'-' + NCHAR(31) + NCHAR(127) + N'-' + NCHAR(159) + N']%',
                actors.ActorExternalId COLLATE Latin1_General_100_BIN2) > 0
    )
        THROW 58032, N'BMS W13 refused: final actor canonical-data guard failed.', 1;

    IF EXISTS
    (
        SELECT 1 FROM bms_web.IdempotencyRecords
        WHERE DATALENGTH(Scope) = 0 OR DATALENGTH([Key]) = 0
           OR DATALENGTH(Scope) <> DATALENGTH(LTRIM(RTRIM(Scope)))
           OR DATALENGTH([Key]) <> DATALENGTH(LTRIM(RTRIM([Key])))
           OR DATALENGTH(Scope) > 646 OR DATALENGTH([Key]) > 254
           OR DATALENGTH(Scope) + DATALENGTH([Key]) > 900
           OR UNICODE(LEFT(Scope, 1)) IN
              (9, 10, 11, 12, 13, 32, 133, 160, 5760,
               8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
               8232, 8233, 8239, 8287, 12288)
           OR UNICODE(RIGHT(Scope, 1)) IN
              (9, 10, 11, 12, 13, 32, 133, 160, 5760,
               8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
               8232, 8233, 8239, 8287, 12288)
           OR UNICODE(LEFT([Key], 1)) IN
              (9, 10, 11, 12, 13, 32, 133, 160, 5760,
               8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
               8232, 8233, 8239, 8287, 12288)
           OR UNICODE(RIGHT([Key], 1)) IN
              (9, 10, 11, 12, 13, 32, 133, 160, 5760,
               8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
               8232, 8233, 8239, 8287, 12288)
           OR PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + NCHAR(127) + N'-' + NCHAR(159) + N']%', Scope COLLATE Latin1_General_100_BIN2) > 0
           OR PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + NCHAR(127) + N'-' + NCHAR(159) + N']%', [Key] COLLATE Latin1_General_100_BIN2) > 0
           OR RequestHash COLLATE Latin1_General_100_BIN2 LIKE '%[^0-9A-F]%'
           OR LEN(RequestHash) <> 64
    )
        THROW 58033, N'BMS W13 refused: final idempotency canonical-data guard failed.', 1;

    IF NOT EXISTS (SELECT 1 FROM bms_web.schema_version WHERE version_no = 8)
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (8, N'008_actor_idempotency_collation.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
