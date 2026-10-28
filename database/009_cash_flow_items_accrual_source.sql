-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web W15 — cash-flow item projection and accrual-source metadata.

    IMPORTANT
    - Apply 001 through 008 first.
    - This script creates no database, login, user, role, permission or seed row.
    - CashFlowItems remains empty until a separately controlled source import.
    - It has not been executed against an accounting server.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 59001, N'BMS W15 refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 59002, N'BMS W15 refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
    THROW 59003, N'BMS W15 refused: managed schema bms_web is absent.', 1;

IF EXISTS (SELECT 1 FROM bms_web.schema_version WHERE version_no > 9)
    THROW 59004, N'BMS W15 refused: a future schema version is already registered.', 1;

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
            (8, N'008_actor_idempotency_collation.sql')
    ) AS expected(version_no, script_name)
    INNER JOIN bms_web.schema_version AS actual
      ON actual.version_no = expected.version_no
     AND actual.script_name COLLATE Latin1_General_100_BIN2 =
         expected.script_name COLLATE Latin1_General_100_BIN2
     AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
) <> 8
    THROW 59005, N'BMS W15 refused: exact schema versions 1 through 8 are required.', 1;

IF EXISTS
(
    SELECT 1
    FROM bms_web.schema_version
    WHERE version_no = 9
      AND
      (
          script_name COLLATE Latin1_General_100_BIN2 <>
              N'009_cash_flow_items_accrual_source.sql'
          OR DATALENGTH(script_name) <>
              DATALENGTH(N'009_cash_flow_items_accrual_source.sql')
      )
)
    THROW 59006, N'BMS W15 refused: schema version 9 belongs to another script.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'bms_web.CashFlowItems', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.CashFlowItems
        (
            Id                       uniqueidentifier NOT NULL,
            Code                     nvarchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
            Name                     nvarchar(150) NOT NULL,
            ParentId                 uniqueidentifier NULL,
            IsActive                 bit NOT NULL,
            IsGroup                  bit NOT NULL,
            FormsManagerialAccrual   bit NOT NULL,
            AccrualExpenseItemId     uniqueidentifier NULL,
            CONSTRAINT PK_bms_web_CashFlowItems PRIMARY KEY CLUSTERED (Id ASC),
            CONSTRAINT UQ_bms_web_CashFlowItems_Code UNIQUE NONCLUSTERED (Code ASC),
            CONSTRAINT FK_bms_web_CashFlowItems_Parent FOREIGN KEY (ParentId)
                REFERENCES bms_web.CashFlowItems (Id) ON DELETE NO ACTION,
            CONSTRAINT CK_bms_web_CashFlowItems_Code CHECK
                (DATALENGTH(Code) > 0 AND DATALENGTH(Code) = DATALENGTH(LTRIM(RTRIM(Code)))),
            CONSTRAINT CK_bms_web_CashFlowItems_Name CHECK
                (DATALENGTH(Name) > 0 AND DATALENGTH(Name) = DATALENGTH(LTRIM(RTRIM(Name)))),
            CONSTRAINT CK_bms_web_CashFlowItems_Parent CHECK
                (ParentId IS NULL OR ParentId <> Id)
        );
    END;

    /* Refuse success against a partial or manually drifted W15 table. */
    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'Id', N'uniqueidentifier', 16, 0, CAST(NULL AS nvarchar(128))),
                (N'Code', N'nvarchar', 40, 0, N'Latin1_General_100_BIN2'),
                (N'Name', N'nvarchar', 300, 0, CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128))),
                (N'ParentId', N'uniqueidentifier', 16, 1, CAST(NULL AS nvarchar(128))),
                (N'IsActive', N'bit', 1, 0, CAST(NULL AS nvarchar(128))),
                (N'IsGroup', N'bit', 1, 0, CAST(NULL AS nvarchar(128))),
                (N'FormsManagerialAccrual', N'bit', 1, 0, CAST(NULL AS nvarchar(128))),
                (N'AccrualExpenseItemId', N'uniqueidentifier', 16, 1, CAST(NULL AS nvarchar(128)))
        ) AS expected(ColumnName, TypeName, MaxLength, IsNullable, CollationName)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(N'bms_web.CashFlowItems')
         AND c.name = expected.ColumnName
        WHERE c.object_id IS NULL
           OR TYPE_NAME(c.user_type_id) <> expected.TypeName
           OR c.max_length <> expected.MaxLength
           OR c.is_nullable <> expected.IsNullable
           OR c.is_computed <> 0
           OR c.default_object_id <> 0
           OR c.rule_object_id <> 0
           OR c.generated_always_type <> 0
           OR c.is_sparse <> 0
           OR c.is_column_set <> 0
           OR c.encryption_type IS NOT NULL
           OR c.is_hidden <> 0
           OR c.is_masked <> 0
           OR
           (
               expected.CollationName IS NULL AND c.collation_name IS NOT NULL
               OR expected.CollationName IS NOT NULL AND
                  c.collation_name COLLATE Latin1_General_100_BIN2 <>
                  expected.CollationName COLLATE Latin1_General_100_BIN2
           )
    )
       OR (SELECT COUNT(*) FROM sys.columns
           WHERE object_id = OBJECT_ID(N'bms_web.CashFlowItems')) <> 8
        THROW 59007, N'BMS W15 refused: CashFlowItems column contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'PK_bms_web_CashFlowItems', 1, 1, 1, 0, 1),
                (N'UQ_bms_web_CashFlowItems_Code', 2, 1, 0, 1, 1)
        ) AS expected(IndexName, IndexType, IsUnique, IsPrimaryKey, IsUniqueConstraint, KeyCount)
        LEFT JOIN sys.indexes AS i
          ON i.object_id = OBJECT_ID(N'bms_web.CashFlowItems')
         AND i.name = expected.IndexName
        WHERE i.object_id IS NULL OR i.type <> expected.IndexType
           OR i.is_unique <> expected.IsUnique
           OR i.is_primary_key <> expected.IsPrimaryKey
           OR i.is_unique_constraint <> expected.IsUniqueConstraint
           OR i.is_disabled <> 0 OR i.is_hypothetical <> 0
           OR i.has_filter <> 0 OR i.filter_definition IS NOT NULL
           OR i.ignore_dup_key <> 0
           OR (SELECT COUNT(*) FROM sys.index_columns AS ic
               WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id) <>
              expected.KeyCount
    )
       OR (SELECT COUNT(*) FROM sys.key_constraints
           WHERE parent_object_id = OBJECT_ID(N'bms_web.CashFlowItems')) <> 2
       OR (SELECT COUNT(*) FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'bms_web.CashFlowItems')
             AND index_id > 0 AND is_unique = 1) <> 2
        THROW 59008, N'BMS W15 refused: CashFlowItems key/index contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'PK_bms_web_CashFlowItems', 1, N'Id'),
                (N'UQ_bms_web_CashFlowItems_Code', 1, N'Code')
        ) AS expected(IndexName, KeyOrdinal, ColumnName)
        LEFT JOIN sys.indexes AS i
          ON i.object_id = OBJECT_ID(N'bms_web.CashFlowItems')
         AND i.name = expected.IndexName
        LEFT JOIN sys.index_columns AS ic
          ON ic.object_id = i.object_id AND ic.index_id = i.index_id
         AND ic.key_ordinal = expected.KeyOrdinal
        LEFT JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE c.name <> expected.ColumnName OR c.name IS NULL
           OR ic.is_descending_key <> 0 OR ic.is_included_column <> 0
    )
        THROW 59009, N'BMS W15 refused: CashFlowItems key order is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'FK_bms_web_CashFlowItems_Parent', N'ParentId', N'Id')
        ) AS expected(ConstraintName, ParentColumn, ReferencedColumn)
        LEFT JOIN sys.foreign_keys AS fk
          ON fk.parent_object_id = OBJECT_ID(N'bms_web.CashFlowItems')
         AND fk.referenced_object_id = OBJECT_ID(N'bms_web.CashFlowItems')
         AND fk.name = expected.ConstraintName
        LEFT JOIN sys.foreign_key_columns AS fkc
          ON fkc.constraint_object_id = fk.object_id AND fkc.constraint_column_id = 1
        LEFT JOIN sys.columns AS pc
          ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
        LEFT JOIN sys.columns AS rc
          ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        WHERE fk.object_id IS NULL OR fk.is_disabled <> 0 OR fk.is_not_trusted <> 0
           OR fk.is_not_for_replication <> 0
           OR fk.delete_referential_action <> 0 OR fk.update_referential_action <> 0
           OR pc.name <> expected.ParentColumn OR rc.name <> expected.ReferencedColumn
           OR (SELECT COUNT(*) FROM sys.foreign_key_columns AS x
               WHERE x.constraint_object_id = fk.object_id) <> 1
    )
       OR (SELECT COUNT(*) FROM sys.foreign_keys
           WHERE parent_object_id = OBJECT_ID(N'bms_web.CashFlowItems')) <> 1
        THROW 59010, N'BMS W15 refused: CashFlowItems hierarchy FK is drifted.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.CashFlowItems')
          AND name IN
          (
              N'CK_bms_web_CashFlowItems_Code',
              N'CK_bms_web_CashFlowItems_Name',
              N'CK_bms_web_CashFlowItems_Parent'
          )
          AND is_disabled = 0 AND is_not_trusted = 0
          AND is_not_for_replication = 0 AND is_system_named = 0
          AND
          (
              (name = N'CK_bms_web_CashFlowItems_Code'
               AND parent_column_id = COLUMNPROPERTY(
                   OBJECT_ID(N'bms_web.CashFlowItems'), N'Code', N'ColumnId'))
              OR (name = N'CK_bms_web_CashFlowItems_Name'
                  AND parent_column_id = COLUMNPROPERTY(
                      OBJECT_ID(N'bms_web.CashFlowItems'), N'Name', N'ColumnId'))
              OR (name = N'CK_bms_web_CashFlowItems_Parent'
                  AND parent_column_id = 0)
          )
    ) <> 3
       OR (SELECT COUNT(*) FROM sys.check_constraints
           WHERE parent_object_id = OBJECT_ID(N'bms_web.CashFlowItems')) <> 3
        THROW 59011, N'BMS W15 refused: CashFlowItems trusted CHECK set is incomplete.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.triggers
        WHERE parent_id = OBJECT_ID(N'bms_web.CashFlowItems')
          AND is_ms_shipped = 0
    )
        THROW 59014, N'BMS W15 refused: CashFlowItems has a user DML trigger.', 1;

    DECLARE @CodeCheck nvarchar(max);
    DECLARE @NameCheck nvarchar(max);
    DECLARE @ParentCheck nvarchar(max);
    SELECT @CodeCheck = definition FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'bms_web.CashFlowItems')
      AND name = N'CK_bms_web_CashFlowItems_Code';
    SELECT @NameCheck = definition FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'bms_web.CashFlowItems')
      AND name = N'CK_bms_web_CashFlowItems_Name';
    SELECT @ParentCheck = definition FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'bms_web.CashFlowItems')
      AND name = N'CK_bms_web_CashFlowItems_Parent';
    SELECT @CodeCheck = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        @CodeCheck, N'[Code]', N'Code'), N'(', N''), N')', N''), N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N'');
    SELECT @NameCheck = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        @NameCheck, N'[Name]', N'Name'), N'(', N''), N')', N''), N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N'');
    SELECT @ParentCheck = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        @ParentCheck, N'[ParentId]', N'ParentId'), N'[Id]', N'Id'), N'(', N''), N')', N''), N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N'');

    IF @CodeCheck COLLATE Latin1_General_100_BIN2 <>
           N'datalengthCode>0ANDdatalengthCode=datalengthltrimrtrimCode' COLLATE Latin1_General_100_BIN2
       OR @NameCheck COLLATE Latin1_General_100_BIN2 <>
           N'datalengthName>0ANDdatalengthName=datalengthltrimrtrimName' COLLATE Latin1_General_100_BIN2
       OR @ParentCheck COLLATE Latin1_General_100_BIN2 <>
           N'ParentIdISNULLORParentId<>Id' COLLATE Latin1_General_100_BIN2
       OR @CodeCheck IS NULL OR @NameCheck IS NULL OR @ParentCheck IS NULL
        THROW 59012, N'BMS W15 refused: CashFlowItems CHECK definitions are drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.CashFlowItems
        WHERE DATALENGTH(Code) = 0
           OR DATALENGTH(Code) <> DATALENGTH(LTRIM(RTRIM(Code)))
           OR DATALENGTH(Name) = 0
           OR DATALENGTH(Name) <> DATALENGTH(LTRIM(RTRIM(Name)))
           OR ParentId = Id
    )
        THROW 59013, N'BMS W15 refused: CashFlowItems canonical-data guard failed.', 1;

    IF NOT EXISTS (SELECT 1 FROM bms_web.schema_version WHERE version_no = 9)
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (9, N'009_cash_flow_items_accrual_source.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
