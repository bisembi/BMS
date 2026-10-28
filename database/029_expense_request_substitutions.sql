-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    W619: active employee substitutions for existing expense requests.
    The request initiator remains immutable. This table stores only the
    principal/deputy relation, its inclusive UTC period and its business basis;
    it does not grant creation, approval, payment, cancellation or closing.
*/
IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS W619 refused: run SQL029 outside an ambient transaction.', 16, 1);
    RETURN;
END;
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 62501, N'BMS W619 refused: current database must be the explicitly owned BMS database.', 1;
IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 62502, N'BMS W619 refused: current database is not READ_WRITE.', 1;
IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.Employees', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.EmployeeActorMappings', N'U') IS NULL
    THROW 62503, N'BMS W619 refused: required managed tables are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no > 29
    )
        THROW 62504, N'BMS W619 refused: a future schema version is registered.', 1;

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
                (28, N'028_cash_flow_accrual_settings_version.sql')
        ) AS expected(version_no, script_name)
        INNER JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK)
          ON actual.version_no = expected.version_no
         AND actual.script_name COLLATE Latin1_General_100_BIN2 =
             expected.script_name COLLATE Latin1_General_100_BIN2
         AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
    ) <> 28
        THROW 62505, N'BMS W619 refused: exact SQL001-SQL028 predecessors are required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 29
          AND
          (
              script_name COLLATE Latin1_General_100_BIN2 <>
                  N'029_expense_request_substitutions.sql'
              OR DATALENGTH(script_name) <>
                  DATALENGTH(N'029_expense_request_substitutions.sql')
          )
    )
        THROW 62506, N'BMS W619 refused: SQL029 belongs to another script.', 1;

    DECLARE @VersionRegistered bit = CASE WHEN EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 29
    ) THEN 1 ELSE 0 END;
    DECLARE @TableId int = OBJECT_ID(N'bms_web.ExpenseRequestSubstitutions');

    IF (@VersionRegistered = 0 AND @TableId IS NOT NULL)
       OR (@VersionRegistered = 1 AND @TableId IS NULL)
        THROW 62507, N'BMS W619 refused: version registration and table ownership disagree.', 1;

    DECLARE @EmployeeLock uniqueidentifier;
    SELECT TOP (1) @EmployeeLock = Id
    FROM bms_web.Employees WITH (TABLOCKX, HOLDLOCK)
    ORDER BY Id;

    IF @VersionRegistered = 0
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM sys.objects
            WHERE name IN
            (
                N'PK_bms_web_ExpenseRequestSubstitutions',
                N'FK_bms_web_ExpenseRequestSubstitutions_PrincipalEmployee',
                N'FK_bms_web_ExpenseRequestSubstitutions_DeputyEmployee',
                N'CK_bms_web_ExpenseRequestSubstitutions_DifferentEmployees',
                N'CK_bms_web_ExpenseRequestSubstitutions_Period',
                N'CK_bms_web_ExpenseRequestSubstitutions_Reason'
            )
        )
            THROW 62508, N'BMS W619 refused: a required object name is already owned.', 1;

        EXEC sys.sp_executesql N'
            CREATE TABLE bms_web.ExpenseRequestSubstitutions
            (
                Id                  uniqueidentifier NOT NULL,
                PrincipalEmployeeId uniqueidentifier NOT NULL,
                DeputyEmployeeId    uniqueidentifier NOT NULL,
                ValidFromUtc         datetimeoffset(3) NOT NULL,
                ValidThroughUtc      datetimeoffset(3) NOT NULL,
                Reason               nvarchar(500) NOT NULL,
                IsActive             bit NOT NULL,
                CONSTRAINT PK_bms_web_ExpenseRequestSubstitutions
                    PRIMARY KEY CLUSTERED (Id ASC),
                CONSTRAINT FK_bms_web_ExpenseRequestSubstitutions_PrincipalEmployee
                    FOREIGN KEY (PrincipalEmployeeId)
                    REFERENCES bms_web.Employees (Id),
                CONSTRAINT FK_bms_web_ExpenseRequestSubstitutions_DeputyEmployee
                    FOREIGN KEY (DeputyEmployeeId)
                    REFERENCES bms_web.Employees (Id),
                CONSTRAINT CK_bms_web_ExpenseRequestSubstitutions_DifferentEmployees
                    CHECK (PrincipalEmployeeId <> DeputyEmployeeId),
                CONSTRAINT CK_bms_web_ExpenseRequestSubstitutions_Period
                    CHECK (ValidFromUtc <= ValidThroughUtc),
                CONSTRAINT CK_bms_web_ExpenseRequestSubstitutions_Reason
                    CHECK (DATALENGTH(Reason) > 0
                        AND Reason = LTRIM(RTRIM(Reason))
                        AND PATINDEX(
                            N''%['' + NCHAR(1) + N''-'' + NCHAR(31)
                                + NCHAR(127) + N''-'' + NCHAR(159) + N'']%'',
                            Reason COLLATE Latin1_General_100_BIN2) = 0)
            );
            CREATE NONCLUSTERED INDEX
                IX_bms_web_ExpenseRequestSubstitutions_Deputy_Active_Period_Principal
                ON bms_web.ExpenseRequestSubstitutions
                (
                    DeputyEmployeeId ASC,
                    IsActive ASC,
                    ValidFromUtc ASC,
                    ValidThroughUtc ASC,
                    PrincipalEmployeeId ASC,
                    Id ASC
                );';
        SET @TableId = OBJECT_ID(N'bms_web.ExpenseRequestSubstitutions');
    END;

    IF @TableId IS NULL
        THROW 62509, N'BMS W619 refused: substitution table was not created.', 1;
    IF EXISTS
    (
        SELECT 1 FROM sys.triggers
        WHERE parent_id = @TableId AND is_ms_shipped = 0
    )
        THROW 62510, N'BMS W619 refused: substitution table has an unmanaged trigger.', 1;
    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id = @TableId) <> 7
        THROW 62511, N'BMS W619 refused: substitution column count differs.', 1;
    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'Id', N'uniqueidentifier', 0, 0),
                (N'PrincipalEmployeeId', N'uniqueidentifier', 0, 0),
                (N'DeputyEmployeeId', N'uniqueidentifier', 0, 0),
                (N'ValidFromUtc', N'datetimeoffset', 3, 0),
                (N'ValidThroughUtc', N'datetimeoffset', 3, 0),
                (N'Reason', N'nvarchar', 0, 500),
                (N'IsActive', N'bit', 0, 0)
        ) AS expected(Name, TypeName, ScaleValue, CharacterLength)
        LEFT JOIN sys.columns AS actual
          ON actual.object_id = @TableId AND actual.name = expected.Name
        WHERE actual.column_id IS NULL
           OR TYPE_NAME(actual.user_type_id) <> expected.TypeName
           OR actual.is_nullable <> 0
           OR actual.is_computed <> 0
           OR actual.default_object_id <> 0
           OR actual.generated_always_type <> 0
           OR actual.is_sparse <> 0
           OR actual.is_column_set <> 0
           OR actual.is_hidden <> 0
           OR actual.encryption_type IS NOT NULL
           OR actual.is_masked <> 0
           OR (expected.ScaleValue <> 0 AND actual.scale <> expected.ScaleValue)
           OR (expected.CharacterLength <> 0
               AND actual.max_length <> expected.CharacterLength * 2)
    )
        THROW 62512, N'BMS W619 refused: substitution column metadata differs.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.indexes
        WHERE object_id = @TableId AND index_id > 0
    ) <> 2
        THROW 62513, N'BMS W619 refused: substitution index count differs.', 1;
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = @TableId
          AND name = N'PK_bms_web_ExpenseRequestSubstitutions'
          AND type = 1 AND is_primary_key = 1 AND is_unique = 1
          AND is_disabled = 0 AND has_filter = 0
    )
        THROW 62514, N'BMS W619 refused: substitution primary key differs.', 1;
    IF
    (
        SELECT STRING_AGG(c.name, N',') WITHIN GROUP (ORDER BY ic.key_ordinal)
        FROM sys.indexes AS i
        INNER JOIN sys.index_columns AS ic
          ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        INNER JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = @TableId
          AND i.name = N'PK_bms_web_ExpenseRequestSubstitutions'
          AND ic.key_ordinal > 0
    ) COLLATE Latin1_General_100_BIN2 <> N'Id'
       OR EXISTS
       (
           SELECT 1 FROM sys.index_columns AS ic
           INNER JOIN sys.indexes AS i
             ON i.object_id = ic.object_id AND i.index_id = ic.index_id
           WHERE i.object_id = @TableId
             AND i.name = N'PK_bms_web_ExpenseRequestSubstitutions'
             AND (ic.is_included_column <> 0 OR ic.is_descending_key <> 0)
       )
        THROW 62514, N'BMS W619 refused: substitution primary key shape differs.', 1;
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = @TableId
          AND name = N'IX_bms_web_ExpenseRequestSubstitutions_Deputy_Active_Period_Principal'
          AND type = 2 AND is_primary_key = 0 AND is_unique = 0
          AND is_disabled = 0 AND has_filter = 0
    )
        THROW 62515, N'BMS W619 refused: substitution lookup index differs.', 1;
    IF
    (
        SELECT STRING_AGG(c.name, N',') WITHIN GROUP (ORDER BY ic.key_ordinal)
        FROM sys.indexes AS i
        INNER JOIN sys.index_columns AS ic
          ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        INNER JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = @TableId
          AND i.name = N'IX_bms_web_ExpenseRequestSubstitutions_Deputy_Active_Period_Principal'
          AND ic.is_included_column = 0
    ) COLLATE Latin1_General_100_BIN2 <>
        N'DeputyEmployeeId,IsActive,ValidFromUtc,ValidThroughUtc,PrincipalEmployeeId,Id'
       OR EXISTS
       (
           SELECT 1 FROM sys.index_columns AS ic
           INNER JOIN sys.indexes AS i
             ON i.object_id = ic.object_id AND i.index_id = ic.index_id
           WHERE i.object_id = @TableId
             AND i.name = N'IX_bms_web_ExpenseRequestSubstitutions_Deputy_Active_Period_Principal'
             AND (ic.is_included_column <> 0 OR ic.is_descending_key <> 0)
       )
        THROW 62516, N'BMS W619 refused: substitution lookup index shape differs.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.foreign_keys
        WHERE parent_object_id = @TableId
    ) <> 2
        THROW 62517, N'BMS W619 refused: substitution foreign keys differ.', 1;
    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'FK_bms_web_ExpenseRequestSubstitutions_PrincipalEmployee',
                 N'PrincipalEmployeeId'),
                (N'FK_bms_web_ExpenseRequestSubstitutions_DeputyEmployee',
                 N'DeputyEmployeeId')
        ) AS expected(ConstraintName, ParentColumnName)
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
           OR actual_fk.referenced_object_id <> OBJECT_ID(N'bms_web.Employees')
           OR actual_fk.is_disabled <> 0 OR actual_fk.is_not_trusted <> 0
           OR actual_fk.is_not_for_replication <> 0 OR actual_fk.is_system_named <> 0
           OR actual_fk.delete_referential_action <> 0
           OR actual_fk.update_referential_action <> 0
           OR parent_column.name IS NULL
           OR parent_column.name <> expected.ParentColumnName
           OR referenced_column.name IS NULL OR referenced_column.name <> N'Id'
           OR (SELECT COUNT(*) FROM sys.foreign_key_columns
               WHERE constraint_object_id = actual_fk.object_id) <> 1
    )
        THROW 62517, N'BMS W619 refused: substitution foreign key shape differs.', 1;
    IF
    (
        SELECT COUNT(*)
        FROM sys.check_constraints
        WHERE parent_object_id = @TableId
    ) <> 3
        THROW 62518, N'BMS W619 refused: substitution checks differ.', 1;
    DECLARE @ExpectedChecks TABLE
        (ConstraintName sysname NOT NULL,
         ParentColumnId int NOT NULL,
         CanonicalDefinition nvarchar(max) NOT NULL);
    INSERT INTO @ExpectedChecks VALUES
        (N'CK_bms_web_ExpenseRequestSubstitutions_DifferentEmployees',
         0,
         N'[principalemployeeid]<>[deputyemployeeid]'),
        (N'CK_bms_web_ExpenseRequestSubstitutions_Period',
         0,
         N'[validfromutc]<=[validthroughutc]'),
        (N'CK_bms_web_ExpenseRequestSubstitutions_Reason',
         COLUMNPROPERTY(@TableId, N'Reason', N'ColumnId'),
         N'datalength[reason]>0and[reason]=ltrimrtrim[reason]andpatindexN''%[''+nchar1+N''-''+nchar31+nchar127+N''-''+nchar159+N'']%'',[reason]collateLatin1_General_100_BIN2=0');
    IF EXISTS
    (
        SELECT 1 FROM @ExpectedChecks AS expected
        LEFT JOIN sys.check_constraints AS actual
          ON actual.parent_object_id = @TableId
         AND actual.name = expected.ConstraintName
        WHERE actual.object_id IS NULL OR actual.definition IS NULL
           OR actual.is_disabled <> 0 OR actual.is_not_trusted <> 0
           OR actual.is_not_for_replication <> 0 OR actual.is_system_named <> 0
           OR actual.parent_column_id <> expected.ParentColumnId
           OR LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                actual.definition,
                N' ', N''), NCHAR(13), N''), NCHAR(10), N''), NCHAR(9), N''),
                N'(', N''), N')', N''))
                COLLATE Latin1_General_100_BIN2
              <> LOWER(expected.CanonicalDefinition) COLLATE Latin1_General_100_BIN2
    )
        THROW 62518, N'BMS W619 refused: substitution check shape differs.', 1;
    IF EXISTS
    (
        SELECT 1
        FROM bms_web.ExpenseRequestSubstitutions WITH (HOLDLOCK)
        WHERE Id = CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000')
           OR PrincipalEmployeeId = DeputyEmployeeId
           OR ValidFromUtc > ValidThroughUtc
           OR DATALENGTH(Reason) = 0
           OR Reason <> LTRIM(RTRIM(Reason))
           OR PATINDEX(
                N'%[' + NCHAR(1) + N'-' + NCHAR(31)
                    + NCHAR(127) + N'-' + NCHAR(159) + N']%',
                Reason COLLATE Latin1_General_100_BIN2) <> 0
    )
        THROW 62519, N'BMS W619 refused: stored substitution data violates its contract.', 1;

    IF @VersionRegistered = 0
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (29, N'029_expense_request_substitutions.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
