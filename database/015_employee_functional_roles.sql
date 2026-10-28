-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web W50A — read-only employee and functional-role projection.

    IMPORTANT
    - Apply 001 through 014 first and run this script standalone.
    - This script creates no database, login, user, role, permission or seed row.
    - Employees, FunctionalRoles and EmployeeFunctionalRoles remain empty until a
      separately controlled source import.
    - SourceDatabaseUserName projects the imported attribute ПользовательИБ as an
      imported descriptive value only. It is not an SSO NameIdentifier, it is not
      unique, it carries no index and it must never be used for authentication or
      authorisation.
    - EmployeeFunctionalRoles projects the non-periodic information register
      BMS_РолиСотрудников: two dimensions and the Активна resource, with no
      validity period (ТЗ v2.9 META-DEF-03).
    - It has not been executed against an accounting server.
*/

IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS W50A refused: run 015_employee_functional_roles.sql outside an ambient transaction.', 16, 1);
    RETURN;
END;

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 59601, N'BMS W50A refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 59602, N'BMS W50A refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
    THROW 59603, N'BMS W50A refused: managed schema bms_web is absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no > 15
    )
        THROW 59604, N'BMS W50A refused: a future schema version is already registered.', 1;

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
                (14, N'014_contracts_reference.sql')
        ) AS expected(version_no, script_name)
        INNER JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK)
          ON actual.version_no = expected.version_no
         AND actual.script_name COLLATE Latin1_General_100_BIN2 =
             expected.script_name COLLATE Latin1_General_100_BIN2
         AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
    ) <> 14
        THROW 59605, N'BMS W50A refused: exact schema versions 1 through 14 are required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 15
          AND
          (
              script_name COLLATE Latin1_General_100_BIN2 <>
                  N'015_employee_functional_roles.sql'
              OR DATALENGTH(script_name) <>
                  DATALENGTH(N'015_employee_functional_roles.sql')
          )
    )
        THROW 59606, N'BMS W50A refused: schema version 15 belongs to another script.', 1;

    IF OBJECT_ID(N'bms_web.Employees', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.Employees
        (
            Id                      uniqueidentifier NOT NULL,
            Code                    nvarchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
            Name                    nvarchar(150) NOT NULL,
            SourceDatabaseUserName  nvarchar(100) NULL,
            IsActive                bit NOT NULL,
            CONSTRAINT PK_bms_web_Employees PRIMARY KEY CLUSTERED (Id ASC),
            CONSTRAINT UQ_bms_web_Employees_Code UNIQUE NONCLUSTERED (Code ASC)
        );

        CREATE NONCLUSTERED INDEX IX_bms_web_Employees_Active_Code
            ON bms_web.Employees (IsActive ASC, Code ASC);
    END;

    IF OBJECT_ID(N'bms_web.FunctionalRoles', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.FunctionalRoles
        (
            Id        uniqueidentifier NOT NULL,
            Code      nvarchar(30) COLLATE Latin1_General_100_BIN2 NOT NULL,
            Name      nvarchar(100) NOT NULL,
            IsActive  bit NOT NULL,
            CONSTRAINT PK_bms_web_FunctionalRoles PRIMARY KEY CLUSTERED (Id ASC),
            CONSTRAINT UQ_bms_web_FunctionalRoles_Code UNIQUE NONCLUSTERED (Code ASC)
        );

        CREATE NONCLUSTERED INDEX IX_bms_web_FunctionalRoles_Active_Code
            ON bms_web.FunctionalRoles (IsActive ASC, Code ASC);
    END;

    IF OBJECT_ID(N'bms_web.EmployeeFunctionalRoles', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.EmployeeFunctionalRoles
        (
            EmployeeId        uniqueidentifier NOT NULL,
            FunctionalRoleId  uniqueidentifier NOT NULL,
            IsActive          bit NOT NULL,
            CONSTRAINT PK_bms_web_EmployeeFunctionalRoles PRIMARY KEY CLUSTERED
                (EmployeeId ASC, FunctionalRoleId ASC),
            CONSTRAINT FK_bms_web_EmployeeFunctionalRoles_Employee
                FOREIGN KEY (EmployeeId) REFERENCES bms_web.Employees (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION,
            CONSTRAINT FK_bms_web_EmployeeFunctionalRoles_FunctionalRole
                FOREIGN KEY (FunctionalRoleId) REFERENCES bms_web.FunctionalRoles (Id)
                ON DELETE NO ACTION ON UPDATE NO ACTION
        );

        CREATE NONCLUSTERED INDEX IX_bms_web_EmployeeFunctionalRoles_Role_Active_Employee
            ON bms_web.EmployeeFunctionalRoles (FunctionalRoleId ASC, IsActive ASC, EmployeeId ASC);
    END;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'Id', N'uniqueidentifier', 16, 0, CAST(NULL AS nvarchar(128))),
                (N'Code', N'nvarchar', 40, 0, N'Latin1_General_100_BIN2'),
                (N'Name', N'nvarchar', 300, 0,
                    CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128))),
                (N'SourceDatabaseUserName', N'nvarchar', 200, 1,
                    CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128))),
                (N'IsActive', N'bit', 1, 0, CAST(NULL AS nvarchar(128)))
        ) AS expected(ColumnName, TypeName, MaxLength, IsNullable, CollationName)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(N'bms_web.Employees')
         AND c.name = expected.ColumnName
        WHERE c.object_id IS NULL
           OR TYPE_NAME(c.user_type_id) <> expected.TypeName
           OR c.max_length <> expected.MaxLength
           OR c.is_nullable <> expected.IsNullable
           OR c.is_computed <> 0 OR c.is_identity <> 0
           OR c.is_rowguidcol <> 0 OR c.is_filestream <> 0
           OR c.default_object_id <> 0 OR c.rule_object_id <> 0
           OR c.generated_always_type <> 0
           OR c.is_sparse <> 0 OR c.is_column_set <> 0
           OR c.encryption_type IS NOT NULL OR c.is_hidden <> 0
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
           WHERE object_id = OBJECT_ID(N'bms_web.Employees')) <> 5
        THROW 59607, N'BMS W50A refused: Employees column contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'Id', N'uniqueidentifier', 16, 0, CAST(NULL AS nvarchar(128))),
                (N'Code', N'nvarchar', 60, 0, N'Latin1_General_100_BIN2'),
                (N'Name', N'nvarchar', 200, 0,
                    CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128))),
                (N'IsActive', N'bit', 1, 0, CAST(NULL AS nvarchar(128)))
        ) AS expected(ColumnName, TypeName, MaxLength, IsNullable, CollationName)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(N'bms_web.FunctionalRoles')
         AND c.name = expected.ColumnName
        WHERE c.object_id IS NULL
           OR TYPE_NAME(c.user_type_id) <> expected.TypeName
           OR c.max_length <> expected.MaxLength
           OR c.is_nullable <> expected.IsNullable
           OR c.is_computed <> 0 OR c.is_identity <> 0
           OR c.is_rowguidcol <> 0 OR c.is_filestream <> 0
           OR c.default_object_id <> 0 OR c.rule_object_id <> 0
           OR c.generated_always_type <> 0
           OR c.is_sparse <> 0 OR c.is_column_set <> 0
           OR c.encryption_type IS NOT NULL OR c.is_hidden <> 0
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
           WHERE object_id = OBJECT_ID(N'bms_web.FunctionalRoles')) <> 4
        THROW 59608, N'BMS W50A refused: FunctionalRoles column contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'EmployeeId', N'uniqueidentifier', 16, 0),
                (N'FunctionalRoleId', N'uniqueidentifier', 16, 0),
                (N'IsActive', N'bit', 1, 0)
        ) AS expected(ColumnName, TypeName, MaxLength, IsNullable)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(N'bms_web.EmployeeFunctionalRoles')
         AND c.name = expected.ColumnName
        WHERE c.object_id IS NULL
           OR TYPE_NAME(c.user_type_id) <> expected.TypeName
           OR c.max_length <> expected.MaxLength
           OR c.is_nullable <> expected.IsNullable
           OR c.is_computed <> 0 OR c.is_identity <> 0
           OR c.is_rowguidcol <> 0 OR c.is_filestream <> 0
           OR c.default_object_id <> 0 OR c.rule_object_id <> 0
           OR c.generated_always_type <> 0
           OR c.is_sparse <> 0 OR c.is_column_set <> 0
           OR c.encryption_type IS NOT NULL OR c.is_hidden <> 0
           OR c.is_masked <> 0
           OR c.collation_name IS NOT NULL
    )
       OR (SELECT COUNT(*) FROM sys.columns
           WHERE object_id = OBJECT_ID(N'bms_web.EmployeeFunctionalRoles')) <> 3
        THROW 59609, N'BMS W50A refused: EmployeeFunctionalRoles column contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.Employees', N'PK_bms_web_Employees', 1, 1, 1, 0, 1),
                (N'bms_web.Employees', N'UQ_bms_web_Employees_Code', 2, 1, 0, 1, 1),
                (N'bms_web.Employees', N'IX_bms_web_Employees_Active_Code', 2, 0, 0, 0, 2),
                (N'bms_web.FunctionalRoles', N'PK_bms_web_FunctionalRoles', 1, 1, 1, 0, 1),
                (N'bms_web.FunctionalRoles', N'UQ_bms_web_FunctionalRoles_Code', 2, 1, 0, 1, 1),
                (N'bms_web.FunctionalRoles', N'IX_bms_web_FunctionalRoles_Active_Code', 2, 0, 0, 0, 2),
                (N'bms_web.EmployeeFunctionalRoles', N'PK_bms_web_EmployeeFunctionalRoles', 1, 1, 1, 0, 2),
                (N'bms_web.EmployeeFunctionalRoles',
                    N'IX_bms_web_EmployeeFunctionalRoles_Role_Active_Employee', 2, 0, 0, 0, 3)
        ) AS expected(TableName, IndexName, IndexType, IsUnique, IsPrimaryKey, IsUniqueConstraint, KeyCount)
        LEFT JOIN sys.indexes AS i
          ON i.object_id = OBJECT_ID(expected.TableName)
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
           WHERE parent_object_id = OBJECT_ID(N'bms_web.Employees')) <> 2
       OR (SELECT COUNT(*) FROM sys.key_constraints
           WHERE parent_object_id = OBJECT_ID(N'bms_web.FunctionalRoles')) <> 2
       OR (SELECT COUNT(*) FROM sys.key_constraints
           WHERE parent_object_id = OBJECT_ID(N'bms_web.EmployeeFunctionalRoles')) <> 1
       OR (SELECT COUNT(*) FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'bms_web.Employees') AND index_id > 0) <> 3
       OR (SELECT COUNT(*) FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'bms_web.FunctionalRoles') AND index_id > 0) <> 3
       OR (SELECT COUNT(*) FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'bms_web.EmployeeFunctionalRoles') AND index_id > 0) <> 2
       OR (SELECT COUNT(*) FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'bms_web.Employees')
             AND index_id > 0 AND is_unique = 1) <> 2
       OR (SELECT COUNT(*) FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'bms_web.FunctionalRoles')
             AND index_id > 0 AND is_unique = 1) <> 2
       OR (SELECT COUNT(*) FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'bms_web.EmployeeFunctionalRoles')
             AND index_id > 0 AND is_unique = 1) <> 1
        THROW 59610, N'BMS W50A refused: employee/role key and index contract is drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.Employees', N'PK_bms_web_Employees', 1, N'Id'),
                (N'bms_web.Employees', N'UQ_bms_web_Employees_Code', 1, N'Code'),
                (N'bms_web.Employees', N'IX_bms_web_Employees_Active_Code', 1, N'IsActive'),
                (N'bms_web.Employees', N'IX_bms_web_Employees_Active_Code', 2, N'Code'),
                (N'bms_web.FunctionalRoles', N'PK_bms_web_FunctionalRoles', 1, N'Id'),
                (N'bms_web.FunctionalRoles', N'UQ_bms_web_FunctionalRoles_Code', 1, N'Code'),
                (N'bms_web.FunctionalRoles', N'IX_bms_web_FunctionalRoles_Active_Code', 1, N'IsActive'),
                (N'bms_web.FunctionalRoles', N'IX_bms_web_FunctionalRoles_Active_Code', 2, N'Code'),
                (N'bms_web.EmployeeFunctionalRoles',
                    N'PK_bms_web_EmployeeFunctionalRoles', 1, N'EmployeeId'),
                (N'bms_web.EmployeeFunctionalRoles',
                    N'PK_bms_web_EmployeeFunctionalRoles', 2, N'FunctionalRoleId'),
                (N'bms_web.EmployeeFunctionalRoles',
                    N'IX_bms_web_EmployeeFunctionalRoles_Role_Active_Employee', 1, N'FunctionalRoleId'),
                (N'bms_web.EmployeeFunctionalRoles',
                    N'IX_bms_web_EmployeeFunctionalRoles_Role_Active_Employee', 2, N'IsActive'),
                (N'bms_web.EmployeeFunctionalRoles',
                    N'IX_bms_web_EmployeeFunctionalRoles_Role_Active_Employee', 3, N'EmployeeId')
        ) AS expected(TableName, IndexName, KeyOrdinal, ColumnName)
        LEFT JOIN sys.indexes AS i
          ON i.object_id = OBJECT_ID(expected.TableName)
         AND i.name = expected.IndexName
        LEFT JOIN sys.index_columns AS ic
          ON ic.object_id = i.object_id AND ic.index_id = i.index_id
         AND ic.key_ordinal = expected.KeyOrdinal
        LEFT JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE c.name <> expected.ColumnName OR c.name IS NULL
           OR ic.is_descending_key <> 0 OR ic.is_included_column <> 0
    )
        THROW 59611, N'BMS W50A refused: employee/role key order is drifted.', 1;

    /* Exact trusted NO ACTION foreign keys and their one-column mappings. */
    IF (SELECT COUNT(*) FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'bms_web.EmployeeFunctionalRoles')) <> 2
       OR EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'FK_bms_web_EmployeeFunctionalRoles_Employee',
                    N'Employees', N'EmployeeId'),
                (N'FK_bms_web_EmployeeFunctionalRoles_FunctionalRole',
                    N'FunctionalRoles', N'FunctionalRoleId')
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
              AND fk.parent_object_id = OBJECT_ID(N'bms_web.EmployeeFunctionalRoles')
              AND fk.referenced_object_id = OBJECT_ID(N'bms_web.' + expected.ReferencedTableName)
              AND parent_column.name = expected.ParentColumnName
              AND referenced_column.name = N'Id'
              AND fk.delete_referential_action = 0
              AND fk.update_referential_action = 0
              AND fk.is_disabled = 0
              AND fk.is_not_trusted = 0
              AND fk.is_not_for_replication = 0
              AND fk.is_system_named = 0
              AND (SELECT COUNT(*) FROM sys.foreign_key_columns AS exact_fkc
                   WHERE exact_fkc.constraint_object_id = fk.object_id) = 1
        )
    )
       OR (SELECT COUNT(*) FROM sys.foreign_keys
           WHERE parent_object_id = OBJECT_ID(N'bms_web.Employees')
              OR parent_object_id = OBJECT_ID(N'bms_web.FunctionalRoles')) <> 0
       OR (SELECT COUNT(*) FROM sys.foreign_keys
           WHERE referenced_object_id = OBJECT_ID(N'bms_web.Employees')) <> 1
       OR (SELECT COUNT(*) FROM sys.foreign_keys
           WHERE referenced_object_id = OBJECT_ID(N'bms_web.FunctionalRoles')) <> 1
       OR (SELECT COUNT(*) FROM sys.foreign_keys
           WHERE referenced_object_id = OBJECT_ID(N'bms_web.EmployeeFunctionalRoles')) <> 0
        THROW 59612, N'BMS W50A refused: employee/role foreign-key contract is missing or drifted.', 1;

    IF (SELECT COUNT(*) FROM sys.check_constraints
        WHERE parent_object_id IN
        (
            OBJECT_ID(N'bms_web.Employees'),
            OBJECT_ID(N'bms_web.FunctionalRoles'),
            OBJECT_ID(N'bms_web.EmployeeFunctionalRoles')
        )) <> 0
        THROW 59613, N'BMS W50A refused: employee/role tables must have no invented CHECK rules.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.triggers
        WHERE parent_id IN
        (
            OBJECT_ID(N'bms_web.Employees'),
            OBJECT_ID(N'bms_web.FunctionalRoles'),
            OBJECT_ID(N'bms_web.EmployeeFunctionalRoles')
        )
          AND is_ms_shipped = 0
    )
        THROW 59614, N'BMS W50A refused: an employee/role table has a user DML trigger.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 15
    )
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (15, N'015_employee_functional_roles.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
