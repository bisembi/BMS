-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web — structure-only workflow definition storage.

    IMPORTANT
    - Apply 001_bootstrap_bms_web.sql, 002_budget_projection.sql and
      003_access_scope.sql first.
    - OPEN-01/OPEN-02 are unresolved. This script inserts no status, command,
      role, transition or other business value.
    - Provisional configuration may be stored for review, but application code
      must always deny its execution.
    - This script creates no database, login, user, role or permission.
    - It has not been executed against an accounting server.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 54001, N'BMS workflow refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 54002, N'BMS workflow refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
    THROW 54003, N'BMS workflow refused: managed schema bms_web is absent.', 1;

IF NOT EXISTS
(
    SELECT 1 FROM bms_web.schema_version
    WHERE version_no = 1
      AND script_name COLLATE Latin1_General_100_BIN2 = N'001_bootstrap_bms_web.sql'
)
    THROW 54004, N'BMS workflow refused: exact schema version 1 is not registered.', 1;

IF NOT EXISTS
(
    SELECT 1 FROM bms_web.schema_version
    WHERE version_no = 2
      AND script_name COLLATE Latin1_General_100_BIN2 = N'002_budget_projection.sql'
)
    THROW 54005, N'BMS workflow refused: exact schema version 2 is not registered.', 1;

IF NOT EXISTS
(
    SELECT 1 FROM bms_web.schema_version
    WHERE version_no = 3
      AND script_name COLLATE Latin1_General_100_BIN2 = N'003_access_scope.sql'
)
    THROW 54006, N'BMS workflow refused: exact schema version 3 is not registered.', 1;

IF EXISTS
(
    SELECT 1 FROM bms_web.schema_version
    WHERE version_no = 4
      AND script_name COLLATE Latin1_General_100_BIN2 <> N'004_workflow_matrix.sql'
)
    THROW 54007, N'BMS workflow refused: schema version 4 belongs to another script.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'bms_web.DocumentStatuses', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.DocumentStatuses
        (
            Code             nvarchar(100) NOT NULL,
            Name             nvarchar(200) NOT NULL,
            LogicalStateCode nvarchar(100) NOT NULL,
            Color            nvarchar(20)  NOT NULL,
            SortOrder        int           NOT NULL,
            IsProvisional    bit           NOT NULL,
            CONSTRAINT PK_bms_web_DocumentStatuses PRIMARY KEY (Code),
            CONSTRAINT CK_bms_web_DocumentStatuses_Code CHECK
                (LEN(LTRIM(RTRIM(Code))) > 0),
            CONSTRAINT CK_bms_web_DocumentStatuses_Name CHECK
                (LEN(LTRIM(RTRIM(Name))) > 0),
            CONSTRAINT CK_bms_web_DocumentStatuses_LogicalStateCode CHECK
                (LEN(LTRIM(RTRIM(LogicalStateCode))) > 0),
            CONSTRAINT CK_bms_web_DocumentStatuses_Color CHECK
                (LEN(LTRIM(RTRIM(Color))) > 0)
        );
    END;

    IF OBJECT_ID(N'bms_web.WorkflowCommands', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.WorkflowCommands
        (
            Code             nvarchar(100) NOT NULL,
            Name             nvarchar(200) NOT NULL,
            RequiresReason   bit           NOT NULL,
            IsApprovalAction bit           NOT NULL,
            SortOrder        int           NOT NULL,
            IsProvisional    bit           NOT NULL,
            CONSTRAINT PK_bms_web_WorkflowCommands PRIMARY KEY (Code),
            CONSTRAINT CK_bms_web_WorkflowCommands_Code CHECK
                (LEN(LTRIM(RTRIM(Code))) > 0),
            CONSTRAINT CK_bms_web_WorkflowCommands_Name CHECK
                (LEN(LTRIM(RTRIM(Name))) > 0)
        );
    END;

    IF OBJECT_ID(N'bms_web.WorkflowPermissions', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.WorkflowPermissions
        (
            DefinitionVersion int           NOT NULL,
            StatusCode        nvarchar(100) NOT NULL,
            StageNumber       int           NOT NULL,
            RoleCode          nvarchar(100) NOT NULL,
            CommandCode       nvarchar(100) NOT NULL,
            TargetStatusCode  nvarchar(100) NULL,
            TargetStageNumber int           NOT NULL,
            ConditionCode     nvarchar(100) NOT NULL
                CONSTRAINT DF_bms_web_WorkflowPermissions_ConditionCode DEFAULT (N''),
            IsVisible         bit           NOT NULL,
            IsEnabled         bit           NOT NULL,
            IsProvisional     bit           NOT NULL,
            CONSTRAINT PK_bms_web_WorkflowPermissions PRIMARY KEY
                (DefinitionVersion, StatusCode, StageNumber, RoleCode, CommandCode, ConditionCode),
            CONSTRAINT FK_bms_web_WorkflowPermissions_Status FOREIGN KEY (StatusCode)
                REFERENCES bms_web.DocumentStatuses (Code) ON DELETE NO ACTION,
            CONSTRAINT FK_bms_web_WorkflowPermissions_Command FOREIGN KEY (CommandCode)
                REFERENCES bms_web.WorkflowCommands (Code) ON DELETE NO ACTION,
            CONSTRAINT FK_bms_web_WorkflowPermissions_TargetStatus FOREIGN KEY (TargetStatusCode)
                REFERENCES bms_web.DocumentStatuses (Code) ON DELETE NO ACTION,
            CONSTRAINT CK_bms_web_WorkflowPermissions_DefinitionVersion CHECK
                (DefinitionVersion > 0),
            CONSTRAINT CK_bms_web_WorkflowPermissions_StageNumber CHECK
                (StageNumber >= 0),
            CONSTRAINT CK_bms_web_WorkflowPermissions_TargetStageNumber CHECK
                (TargetStageNumber >= 0),
            CONSTRAINT CK_bms_web_WorkflowPermissions_Role CHECK
                (LEN(LTRIM(RTRIM(RoleCode))) > 0),
            CONSTRAINT CK_bms_web_WorkflowPermissions_TargetStatus CHECK
                (TargetStatusCode IS NULL OR LEN(LTRIM(RTRIM(TargetStatusCode))) > 0),
            CONSTRAINT CK_bms_web_WorkflowPermissions_Condition CHECK
                (ConditionCode = LTRIM(RTRIM(ConditionCode)))
        );
    END;

    /* Exact column count/type/length/nullability drift contract. */
    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'bms_web.DocumentStatuses')) <> 6
       OR (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'bms_web.WorkflowCommands')) <> 6
       OR (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'bms_web.WorkflowPermissions')) <> 11
        THROW 54008, N'BMS workflow refused: W-05 table column count is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.DocumentStatuses', N'Code', N'nvarchar', 200, 0),
                (N'bms_web.DocumentStatuses', N'Name', N'nvarchar', 400, 0),
                (N'bms_web.DocumentStatuses', N'LogicalStateCode', N'nvarchar', 200, 0),
                (N'bms_web.DocumentStatuses', N'Color', N'nvarchar', 40, 0),
                (N'bms_web.DocumentStatuses', N'SortOrder', N'int', 4, 0),
                (N'bms_web.DocumentStatuses', N'IsProvisional', N'bit', 1, 0),
                (N'bms_web.WorkflowCommands', N'Code', N'nvarchar', 200, 0),
                (N'bms_web.WorkflowCommands', N'Name', N'nvarchar', 400, 0),
                (N'bms_web.WorkflowCommands', N'RequiresReason', N'bit', 1, 0),
                (N'bms_web.WorkflowCommands', N'IsApprovalAction', N'bit', 1, 0),
                (N'bms_web.WorkflowCommands', N'SortOrder', N'int', 4, 0),
                (N'bms_web.WorkflowCommands', N'IsProvisional', N'bit', 1, 0),
                (N'bms_web.WorkflowPermissions', N'DefinitionVersion', N'int', 4, 0),
                (N'bms_web.WorkflowPermissions', N'StatusCode', N'nvarchar', 200, 0),
                (N'bms_web.WorkflowPermissions', N'StageNumber', N'int', 4, 0),
                (N'bms_web.WorkflowPermissions', N'RoleCode', N'nvarchar', 200, 0),
                (N'bms_web.WorkflowPermissions', N'CommandCode', N'nvarchar', 200, 0),
                (N'bms_web.WorkflowPermissions', N'TargetStatusCode', N'nvarchar', 200, 1),
                (N'bms_web.WorkflowPermissions', N'TargetStageNumber', N'int', 4, 0),
                (N'bms_web.WorkflowPermissions', N'ConditionCode', N'nvarchar', 200, 0),
                (N'bms_web.WorkflowPermissions', N'IsVisible', N'bit', 1, 0),
                (N'bms_web.WorkflowPermissions', N'IsEnabled', N'bit', 1, 0),
                (N'bms_web.WorkflowPermissions', N'IsProvisional', N'bit', 1, 0)
        ) AS expected(ObjectName, ColumnName, TypeName, MaxLength, IsNullable)
        LEFT JOIN sys.columns AS actual
          ON actual.object_id = OBJECT_ID(expected.ObjectName)
         AND actual.name = expected.ColumnName
        WHERE actual.object_id IS NULL
           OR TYPE_NAME(actual.user_type_id) <> expected.TypeName
           OR actual.max_length <> expected.MaxLength
           OR actual.is_nullable <> expected.IsNullable
    )
        THROW 54009, N'BMS workflow refused: W-05 column contract is missing or drifted.', 1;

    /* Exact primary-key names, widths and column order. */
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.key_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.DocumentStatuses')
          AND [type] = N'PK' AND name = N'PK_bms_web_DocumentStatuses'
    )
       OR (SELECT COUNT(*)
           FROM sys.index_columns AS ic
           INNER JOIN sys.key_constraints AS kc
             ON kc.parent_object_id = ic.object_id AND kc.unique_index_id = ic.index_id
           WHERE kc.parent_object_id = OBJECT_ID(N'bms_web.DocumentStatuses')
             AND kc.name = N'PK_bms_web_DocumentStatuses' AND ic.key_ordinal > 0) <> 1
       OR NOT EXISTS
    (
        SELECT 1
        FROM sys.index_columns AS ic
        INNER JOIN sys.key_constraints AS kc
          ON kc.parent_object_id = ic.object_id AND kc.unique_index_id = ic.index_id
        INNER JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE kc.name = N'PK_bms_web_DocumentStatuses'
          AND kc.parent_object_id = OBJECT_ID(N'bms_web.DocumentStatuses')
          AND ic.key_ordinal = 1 AND c.name = N'Code'
    )
        THROW 54010, N'BMS workflow refused: DocumentStatuses primary key is drifted.', 1;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.key_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.WorkflowCommands')
          AND [type] = N'PK' AND name = N'PK_bms_web_WorkflowCommands'
    )
       OR (SELECT COUNT(*)
           FROM sys.index_columns AS ic
           INNER JOIN sys.key_constraints AS kc
             ON kc.parent_object_id = ic.object_id AND kc.unique_index_id = ic.index_id
           WHERE kc.parent_object_id = OBJECT_ID(N'bms_web.WorkflowCommands')
             AND kc.name = N'PK_bms_web_WorkflowCommands' AND ic.key_ordinal > 0) <> 1
       OR NOT EXISTS
    (
        SELECT 1
        FROM sys.index_columns AS ic
        INNER JOIN sys.key_constraints AS kc
          ON kc.parent_object_id = ic.object_id AND kc.unique_index_id = ic.index_id
        INNER JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE kc.name = N'PK_bms_web_WorkflowCommands'
          AND kc.parent_object_id = OBJECT_ID(N'bms_web.WorkflowCommands')
          AND ic.key_ordinal = 1 AND c.name = N'Code'
    )
        THROW 54011, N'BMS workflow refused: WorkflowCommands primary key is drifted.', 1;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.key_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.WorkflowPermissions')
          AND [type] = N'PK' AND name = N'PK_bms_web_WorkflowPermissions'
    )
       OR (SELECT COUNT(*)
           FROM sys.index_columns AS ic
           INNER JOIN sys.key_constraints AS kc
             ON kc.parent_object_id = ic.object_id AND kc.unique_index_id = ic.index_id
           WHERE kc.parent_object_id = OBJECT_ID(N'bms_web.WorkflowPermissions')
             AND kc.name = N'PK_bms_web_WorkflowPermissions' AND ic.key_ordinal > 0) <> 6
       OR EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (1, N'DefinitionVersion'),
                (2, N'StatusCode'),
                (3, N'StageNumber'),
                (4, N'RoleCode'),
                (5, N'CommandCode'),
                (6, N'ConditionCode')
        ) AS expected(KeyOrdinal, ColumnName)
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM sys.index_columns AS ic
            INNER JOIN sys.key_constraints AS kc
              ON kc.parent_object_id = ic.object_id AND kc.unique_index_id = ic.index_id
            INNER JOIN sys.columns AS c
              ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE kc.name = N'PK_bms_web_WorkflowPermissions'
              AND kc.parent_object_id = OBJECT_ID(N'bms_web.WorkflowPermissions')
              AND ic.key_ordinal = expected.KeyOrdinal
              AND c.name = expected.ColumnName
        )
    )
        THROW 54012, N'BMS workflow refused: WorkflowPermissions primary key is drifted.', 1;

    /* Exact trusted NO ACTION foreign keys and their one-column mappings. */
    IF (SELECT COUNT(*) FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'bms_web.WorkflowPermissions')) <> 3
       OR EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'FK_bms_web_WorkflowPermissions_Status', N'DocumentStatuses', N'StatusCode'),
                (N'FK_bms_web_WorkflowPermissions_Command', N'WorkflowCommands', N'CommandCode'),
                (N'FK_bms_web_WorkflowPermissions_TargetStatus', N'DocumentStatuses', N'TargetStatusCode')
        ) AS expected(ForeignKeyName, ReferencedTableName, ParentColumnName)
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM sys.foreign_keys AS fk
            INNER JOIN sys.foreign_key_columns AS fkc
              ON fkc.constraint_object_id = fk.object_id
            INNER JOIN sys.columns AS parent_column
              ON parent_column.object_id = fkc.parent_object_id
             AND parent_column.column_id = fkc.parent_column_id
            INNER JOIN sys.columns AS referenced_column
              ON referenced_column.object_id = fkc.referenced_object_id
             AND referenced_column.column_id = fkc.referenced_column_id
            WHERE fk.name = expected.ForeignKeyName
              AND fk.parent_object_id = OBJECT_ID(N'bms_web.WorkflowPermissions')
              AND fk.referenced_object_id = OBJECT_ID(N'bms_web.' + expected.ReferencedTableName)
              AND parent_column.name = expected.ParentColumnName
              AND referenced_column.name = N'Code'
              AND fk.delete_referential_action = 0
              AND fk.update_referential_action = 0
              AND fk.is_disabled = 0
              AND fk.is_not_trusted = 0
              AND (SELECT COUNT(*) FROM sys.foreign_key_columns AS exact_fkc
                   WHERE exact_fkc.constraint_object_id = fk.object_id) = 1
        )
    )
        THROW 54013, N'BMS workflow refused: W-05 foreign-key contract is missing or drifted.', 1;

    /* Exact enabled/trusted check names and normalized definitions. */
    IF (SELECT COUNT(*) FROM sys.check_constraints
        WHERE parent_object_id IN
        (
            OBJECT_ID(N'bms_web.DocumentStatuses'),
            OBJECT_ID(N'bms_web.WorkflowCommands'),
            OBJECT_ID(N'bms_web.WorkflowPermissions')
        )) <> 12
       OR EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.DocumentStatuses', N'CK_bms_web_DocumentStatuses_Code',
                    N'lenltrimrtrimcode>0'),
                (N'bms_web.DocumentStatuses', N'CK_bms_web_DocumentStatuses_Name',
                    N'lenltrimrtrimname>0'),
                (N'bms_web.DocumentStatuses', N'CK_bms_web_DocumentStatuses_LogicalStateCode',
                    N'lenltrimrtrimlogicalstatecode>0'),
                (N'bms_web.DocumentStatuses', N'CK_bms_web_DocumentStatuses_Color',
                    N'lenltrimrtrimcolor>0'),
                (N'bms_web.WorkflowCommands', N'CK_bms_web_WorkflowCommands_Code',
                    N'lenltrimrtrimcode>0'),
                (N'bms_web.WorkflowCommands', N'CK_bms_web_WorkflowCommands_Name',
                    N'lenltrimrtrimname>0'),
                (N'bms_web.WorkflowPermissions', N'CK_bms_web_WorkflowPermissions_DefinitionVersion',
                    N'definitionversion>0'),
                (N'bms_web.WorkflowPermissions', N'CK_bms_web_WorkflowPermissions_StageNumber',
                    N'stagenumber>=0'),
                (N'bms_web.WorkflowPermissions', N'CK_bms_web_WorkflowPermissions_TargetStageNumber',
                    N'targetstagenumber>=0'),
                (N'bms_web.WorkflowPermissions', N'CK_bms_web_WorkflowPermissions_Role',
                    N'lenltrimrtrimrolecode>0'),
                (N'bms_web.WorkflowPermissions', N'CK_bms_web_WorkflowPermissions_TargetStatus',
                    N'targetstatuscodeisnullorlenltrimrtrimtargetstatuscode>0'),
                (N'bms_web.WorkflowPermissions', N'CK_bms_web_WorkflowPermissions_Condition',
                    N'conditioncode=ltrimrtrimconditioncode')
        ) AS expected(ObjectName, ConstraintName, NormalizedDefinition)
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM sys.check_constraints AS cc
            CROSS APPLY
            (
                VALUES
                (
                    LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        cc.definition,
                        N'[', N''), N']', N''), N'(', N''), N')', N''), N' ', N''),
                        NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N''))
                        COLLATE Latin1_General_100_BIN2
                )
            ) AS actual(NormalizedDefinition)
            WHERE cc.parent_object_id = OBJECT_ID(expected.ObjectName)
              AND cc.name = expected.ConstraintName
              AND cc.is_disabled = 0
              AND cc.is_not_trusted = 0
              AND actual.NormalizedDefinition =
                  expected.NormalizedDefinition COLLATE Latin1_General_100_BIN2
        )
    )
        THROW 54014, N'BMS workflow refused: W-05 check constraints are missing, untrusted or definition-drifted.', 1;

    /* Exact default name, column and normalized Unicode-empty-string definition. */
    IF (SELECT COUNT(*) FROM sys.default_constraints
        WHERE parent_object_id IN
        (
            OBJECT_ID(N'bms_web.DocumentStatuses'),
            OBJECT_ID(N'bms_web.WorkflowCommands'),
            OBJECT_ID(N'bms_web.WorkflowPermissions')
        )) <> 1
       OR NOT EXISTS
    (
        SELECT 1
        FROM sys.default_constraints AS dc
        INNER JOIN sys.columns AS c
          ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
        CROSS APPLY
        (
            VALUES
            (
                LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                    dc.definition,
                    N'[', N''), N']', N''), N'(', N''), N')', N''), N' ', N''),
                    NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N''))
                    COLLATE Latin1_General_100_BIN2
            )
        ) AS actual(NormalizedDefinition)
        WHERE dc.parent_object_id = OBJECT_ID(N'bms_web.WorkflowPermissions')
          AND dc.name = N'DF_bms_web_WorkflowPermissions_ConditionCode'
          AND c.name = N'ConditionCode'
          AND actual.NormalizedDefinition =
              (N'n' + NCHAR(39) + NCHAR(39)) COLLATE Latin1_General_100_BIN2
    )
        THROW 54015, N'BMS workflow refused: ConditionCode default is missing or drifted.', 1;

    IF NOT EXISTS (SELECT 1 FROM bms_web.schema_version WHERE version_no = 4)
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (4, N'004_workflow_matrix.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
