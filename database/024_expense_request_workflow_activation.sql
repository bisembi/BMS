-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web W420 — activate the approved expense-request workflow registry.

    This is a managed, local-acceptance migration. It never contacts a remote
    database and must be executed standalone against the explicitly owned BMS database only.

    Safety contract:
    - only the former aggregate token Draft is migrated to DRAFT;
    - any other legacy status is refused, never inferred;
    - existing registry rows are brought to the approved v1 values, but unknown
      rows are retained and make the migration fail rather than being deleted;
    - the active route pointer may exist only while a request is IN_APPROVAL;
    - old idempotent ExpenseRequest responses receive the same canonical DRAFT
      token so a replay cannot publish a stale representation.
*/

IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR(N'BMS W420 refused: run 024_expense_request_workflow_activation.sql outside an ambient transaction.', 16, 1);
    RETURN;
END;

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 62001, N'BMS W420 refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 62002, N'BMS W420 refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.ExpenseRequests', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.DocumentStatuses', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.WorkflowCommands', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.WorkflowPermissions', N'U') IS NULL
   OR OBJECT_ID(N'bms_web.IdempotencyRecords', N'U') IS NULL
    THROW 62003, N'BMS W420 refused: required managed workflow tables are absent.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @LockWitness bigint;
    SELECT @LockWitness = COUNT_BIG(*) FROM bms_web.schema_version WITH (TABLOCKX, HOLDLOCK);
    SELECT @LockWitness = COUNT_BIG(*) FROM bms_web.ExpenseRequests WITH (TABLOCKX, HOLDLOCK);
    SELECT @LockWitness = COUNT_BIG(*) FROM bms_web.DocumentStatuses WITH (TABLOCKX, HOLDLOCK);
    SELECT @LockWitness = COUNT_BIG(*) FROM bms_web.WorkflowCommands WITH (TABLOCKX, HOLDLOCK);
    SELECT @LockWitness = COUNT_BIG(*) FROM bms_web.WorkflowPermissions WITH (TABLOCKX, HOLDLOCK);
    SELECT @LockWitness = COUNT_BIG(*) FROM bms_web.IdempotencyRecords WITH (TABLOCKX, HOLDLOCK);

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no > 24
    )
        THROW 62004, N'BMS W420 refused: a future schema version is already registered.', 1;

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
                (23, N'023_employee_actor_mappings.sql')
        ) AS expected(version_no, script_name)
        INNER JOIN bms_web.schema_version AS actual WITH (UPDLOCK, HOLDLOCK)
          ON actual.version_no = expected.version_no
         AND actual.script_name COLLATE Latin1_General_100_BIN2 =
             expected.script_name COLLATE Latin1_General_100_BIN2
         AND DATALENGTH(actual.script_name) = DATALENGTH(expected.script_name)
    ) <> 23
        THROW 62005, N'BMS W420 refused: exact schema versions 1 through 23 are required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 24
          AND
          (
              script_name COLLATE Latin1_General_100_BIN2 <>
                  N'024_expense_request_workflow_activation.sql'
              OR DATALENGTH(script_name) <>
                  DATALENGTH(N'024_expense_request_workflow_activation.sql')
          )
    )
        THROW 62006, N'BMS W420 refused: schema version 24 belongs to another script.', 1;

    DECLARE @Version24Applied bit = CASE WHEN EXISTS
    (
        SELECT 1
        FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 24
    ) THEN 1 ELSE 0 END;

    IF @Version24Applied = 0 AND EXISTS
    (
        SELECT 1
        FROM bms_web.ExpenseRequests WITH (UPDLOCK, HOLDLOCK)
        WHERE [Status] NOT IN (N'Draft', N'DRAFT')
    )
        THROW 62007, N'BMS W420 refused: an unsupported legacy expense-request status exists.', 1;

    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'ActiveApprovalRouteSnapshotNumber') IS NULL
    BEGIN
        ALTER TABLE bms_web.ExpenseRequests ADD
            ActiveApprovalRouteSnapshotNumber int NULL;
    END;

    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'ActiveApprovalRouteStageNumber') IS NULL
    BEGIN
        ALTER TABLE bms_web.ExpenseRequests ADD
            ActiveApprovalRouteStageNumber int NOT NULL
                CONSTRAINT DF_bms_web_ExpenseRequests_ActiveApprovalRouteStageNumber DEFAULT (0)
                WITH VALUES;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.default_constraints AS dc
        INNER JOIN sys.columns AS column_definition
          ON column_definition.object_id = dc.parent_object_id
         AND column_definition.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
          AND column_definition.name = N'ActiveApprovalRouteStageNumber'
          AND dc.name <> N'DF_bms_web_ExpenseRequests_ActiveApprovalRouteStageNumber'
    )
        THROW 62008, N'BMS W420 refused: the route-stage default constraint is unexpected.', 1;

    IF OBJECT_ID(N'bms_web.DF_bms_web_ExpenseRequests_ActiveApprovalRouteStageNumber', N'D') IS NOT NULL
        ALTER TABLE bms_web.ExpenseRequests
            DROP CONSTRAINT DF_bms_web_ExpenseRequests_ActiveApprovalRouteStageNumber;

    IF EXISTS
    (
        SELECT 1
        FROM sys.columns AS actual
        WHERE actual.object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
          AND actual.name = N'ActiveApprovalRouteSnapshotNumber'
          AND
          (
              TYPE_NAME(actual.user_type_id) <> N'int'
              OR actual.max_length <> 4
              OR actual.is_nullable <> 1
              OR actual.is_identity <> 0 OR actual.is_computed <> 0
              OR actual.default_object_id <> 0
          )
    )
       OR EXISTS
    (
        SELECT 1
        FROM sys.columns AS actual
        WHERE actual.object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
          AND actual.name = N'ActiveApprovalRouteStageNumber'
          AND
          (
              TYPE_NAME(actual.user_type_id) <> N'int'
              OR actual.max_length <> 4
              OR actual.is_nullable <> 0
              OR actual.is_identity <> 0 OR actual.is_computed <> 0
              OR actual.default_object_id <> 0
          )
    )
        THROW 62009, N'BMS W420 refused: active approval-route column contract is drifted.', 1;

    IF @Version24Applied = 0
    BEGIN
        EXEC(N'
            UPDATE bms_web.ExpenseRequests
               SET [Status] = N''DRAFT'',
                   ActiveApprovalRouteSnapshotNumber = NULL,
                   ActiveApprovalRouteStageNumber = 0
             WHERE [Status] = N''Draft'';');
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
          AND name = N'CK_bms_web_ExpenseRequests_ApprovalRouteState'
    )
    BEGIN
        -- The two columns may have been added just above.  Compile the CHECK
        -- only after that DDL has executed; otherwise a fresh database can
        -- bind this batch against the pre-ALTER table definition.
        EXEC(N'
            ALTER TABLE bms_web.ExpenseRequests WITH CHECK ADD
                CONSTRAINT CK_bms_web_ExpenseRequests_ApprovalRouteState CHECK
                (
                    ([Status] = N''IN_APPROVAL''
                     AND ActiveApprovalRouteSnapshotNumber IS NOT NULL
                     AND ActiveApprovalRouteSnapshotNumber > 0
                     AND ActiveApprovalRouteStageNumber > 0)
                    OR
                    ([Status] <> N''IN_APPROVAL''
                     AND ActiveApprovalRouteSnapshotNumber IS NULL
                     AND ActiveApprovalRouteStageNumber = 0)
                );');
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.ExpenseRequests')
          AND name = N'CK_bms_web_ExpenseRequests_ApprovalRouteState'
          AND is_disabled = 0 AND is_not_trusted = 0 AND is_not_for_replication = 0
    )
        THROW 62010, N'BMS W420 refused: active approval-route check is absent or untrusted.', 1;

    DECLARE @Statuses TABLE
    (
        Code nvarchar(100) NOT NULL PRIMARY KEY,
        Name nvarchar(200) NOT NULL,
        LogicalStateCode nvarchar(100) NOT NULL,
        Color nvarchar(20) NOT NULL,
        SortOrder int NOT NULL,
        IsProvisional bit NOT NULL
    );

    INSERT INTO @Statuses (Code, Name, LogicalStateCode, Color, SortOrder, IsProvisional)
    VALUES
        (N'DRAFT', N'Черновик', N'DRAFT', N'neutral', 10, 0),
        (N'RETURNED', N'Возвращена', N'RETURNED', N'neutral', 20, 0),
        (N'IN_APPROVAL', N'На согласовании', N'IN_APPROVAL', N'neutral', 30, 0),
        (N'APPROVED', N'Утверждено', N'APPROVED', N'neutral', 40, 0),
        (N'AWAITING_NSI', N'Ожидает НСИ', N'AWAITING_NSI', N'neutral', 50, 0),
        (N'TO_PAY', N'К оплате', N'TO_PAY', N'neutral', 60, 0),
        (N'PARTIALLY_PAID', N'Оплачена частично', N'PARTIALLY_PAID', N'neutral', 70, 0),
        (N'PAID', N'Оплачена', N'PAID', N'neutral', 80, 0),
        (N'CLOSED', N'Закрыта', N'CLOSED', N'neutral', 90, 0),
        (N'REJECTED', N'Отклонена', N'REJECTED', N'neutral', 100, 0),
        (N'CANCELLED', N'Отменена', N'CANCELLED', N'neutral', 110, 0);

    UPDATE actual
       SET Name = expected.Name,
           LogicalStateCode = expected.LogicalStateCode,
           Color = expected.Color,
           SortOrder = expected.SortOrder,
           IsProvisional = expected.IsProvisional
      FROM bms_web.DocumentStatuses AS actual
      INNER JOIN @Statuses AS expected ON expected.Code = actual.Code;

    INSERT INTO bms_web.DocumentStatuses
        (Code, Name, LogicalStateCode, Color, SortOrder, IsProvisional)
    SELECT Code, Name, LogicalStateCode, Color, SortOrder, IsProvisional
    FROM @Statuses AS expected
    WHERE NOT EXISTS
    (
        SELECT 1 FROM bms_web.DocumentStatuses AS actual
        WHERE actual.Code = expected.Code
    );

    DECLARE @Commands TABLE
    (
        Code nvarchar(100) NOT NULL PRIMARY KEY,
        Name nvarchar(200) NOT NULL,
        RequiresReason bit NOT NULL,
        IsApprovalAction bit NOT NULL,
        SortOrder int NOT NULL,
        IsProvisional bit NOT NULL
    );

    INSERT INTO @Commands (Code, Name, RequiresReason, IsApprovalAction, SortOrder, IsProvisional)
    VALUES
        (N'SUBMIT', N'Подать', 0, 0, 10, 0),
        (N'APPROVE', N'Согласовать', 0, 1, 20, 0),
        (N'RETURN', N'Вернуть', 1, 1, 30, 0),
        (N'REJECT', N'Отклонить', 1, 1, 40, 0),
        (N'CANCEL', N'Отменить', 1, 0, 50, 0),
        (N'CONFIRM_NSI', N'Подтвердить НСИ', 0, 0, 60, 0),
        (N'SEND_TO_PAYMENT', N'В оплату', 0, 0, 70, 0),
        (N'CLOSE_PARTIAL', N'Закрыть частично', 1, 0, 80, 1),
        (N'CLOSE_FULL', N'Закрыть полностью', 1, 0, 90, 1),
        (N'REOPEN', N'Открыть повторно', 1, 0, 100, 1),
        (N'ADD_ATTACHMENT', N'Добавить вложение', 1, 0, 110, 1);

    UPDATE actual
       SET Name = expected.Name,
           RequiresReason = expected.RequiresReason,
           IsApprovalAction = expected.IsApprovalAction,
           SortOrder = expected.SortOrder,
           IsProvisional = expected.IsProvisional
      FROM bms_web.WorkflowCommands AS actual
      INNER JOIN @Commands AS expected ON expected.Code = actual.Code;

    INSERT INTO bms_web.WorkflowCommands
        (Code, Name, RequiresReason, IsApprovalAction, SortOrder, IsProvisional)
    SELECT Code, Name, RequiresReason, IsApprovalAction, SortOrder, IsProvisional
    FROM @Commands AS expected
    WHERE NOT EXISTS
    (
        SELECT 1 FROM bms_web.WorkflowCommands AS actual
        WHERE actual.Code = expected.Code
    );

    DECLARE @Permissions TABLE
    (
        DefinitionVersion int NOT NULL,
        StatusCode nvarchar(100) NOT NULL,
        StageNumber int NOT NULL,
        RoleCode nvarchar(100) NOT NULL,
        CommandCode nvarchar(100) NOT NULL,
        TargetStatusCode nvarchar(100) NULL,
        TargetStageNumber int NOT NULL,
        ConditionCode nvarchar(100) NOT NULL,
        IsVisible bit NOT NULL,
        IsEnabled bit NOT NULL,
        IsProvisional bit NOT NULL,
        PRIMARY KEY (DefinitionVersion, StatusCode, StageNumber, RoleCode, CommandCode, ConditionCode)
    );

    INSERT INTO @Permissions
        (DefinitionVersion, StatusCode, StageNumber, RoleCode, CommandCode,
         TargetStatusCode, TargetStageNumber, ConditionCode,
         IsVisible, IsEnabled, IsProvisional)
    VALUES
        (1, N'DRAFT', 0, N'Инициатор', N'SUBMIT', N'IN_APPROVAL', 1, N'SUBMISSION_READY', 1, 1, 0),
        (1, N'DRAFT', 0, N'Инициатор', N'CANCEL', N'CANCELLED', 0, N'', 1, 1, 0),
        (1, N'RETURNED', 0, N'Инициатор', N'SUBMIT', N'IN_APPROVAL', 1, N'SUBMISSION_READY', 1, 1, 0),
        (1, N'RETURNED', 0, N'Инициатор', N'CANCEL', N'CANCELLED', 0, N'', 1, 1, 0),
        (1, N'IN_APPROVAL', 1, N'FIN_CONTROLLER', N'APPROVE', N'IN_APPROVAL', 2, N'ROUTE_HAS_NEXT_STAGE', 1, 1, 0),
        (1, N'IN_APPROVAL', 1, N'FIN_CONTROLLER', N'APPROVE', N'APPROVED', 0, N'ROUTE_LAST_NO_NSI', 1, 1, 0),
        (1, N'IN_APPROVAL', 1, N'FIN_CONTROLLER', N'APPROVE', N'AWAITING_NSI', 0, N'ROUTE_LAST_REQUIRES_NSI', 1, 1, 0),
        (1, N'IN_APPROVAL', 2, N'FIN_DIRECTOR', N'APPROVE', N'IN_APPROVAL', 3, N'ROUTE_HAS_NEXT_STAGE', 1, 1, 0),
        (1, N'IN_APPROVAL', 2, N'FIN_DIRECTOR', N'APPROVE', N'APPROVED', 0, N'ROUTE_LAST_NO_NSI', 1, 1, 0),
        (1, N'IN_APPROVAL', 2, N'FIN_DIRECTOR', N'APPROVE', N'AWAITING_NSI', 0, N'ROUTE_LAST_REQUIRES_NSI', 1, 1, 0),
        (1, N'IN_APPROVAL', 3, N'GENERAL_DIRECTOR', N'APPROVE', N'APPROVED', 0, N'ROUTE_LAST_NO_NSI', 1, 1, 0),
        (1, N'IN_APPROVAL', 3, N'GENERAL_DIRECTOR', N'APPROVE', N'AWAITING_NSI', 0, N'ROUTE_LAST_REQUIRES_NSI', 1, 1, 0),
        (1, N'IN_APPROVAL', 1, N'FIN_CONTROLLER', N'RETURN', N'RETURNED', 0, N'', 1, 1, 0),
        (1, N'IN_APPROVAL', 2, N'FIN_DIRECTOR', N'RETURN', N'RETURNED', 0, N'', 1, 1, 0),
        (1, N'IN_APPROVAL', 3, N'GENERAL_DIRECTOR', N'RETURN', N'RETURNED', 0, N'', 1, 1, 0),
        (1, N'IN_APPROVAL', 1, N'FIN_CONTROLLER', N'REJECT', N'REJECTED', 0, N'', 1, 1, 0),
        (1, N'IN_APPROVAL', 2, N'FIN_DIRECTOR', N'REJECT', N'REJECTED', 0, N'', 1, 1, 0),
        (1, N'IN_APPROVAL', 3, N'GENERAL_DIRECTOR', N'REJECT', N'REJECTED', 0, N'', 1, 1, 0),
        (1, N'IN_APPROVAL', 1, N'Инициатор', N'CANCEL', N'CANCELLED', 0, N'', 1, 1, 0),
        (1, N'IN_APPROVAL', 2, N'Инициатор', N'CANCEL', N'CANCELLED', 0, N'', 1, 1, 0),
        (1, N'IN_APPROVAL', 3, N'Инициатор', N'CANCEL', N'CANCELLED', 0, N'', 1, 1, 0),
        (1, N'AWAITING_NSI', 0, N'NSI_SPECIALIST', N'CONFIRM_NSI', N'APPROVED', 0, N'NSI_CANDIDATE_LINKED', 1, 1, 0),
        (1, N'AWAITING_NSI', 0, N'Инициатор', N'CANCEL', N'CANCELLED', 0, N'', 1, 1, 0),
        (1, N'APPROVED', 0, N'TREASURER', N'SEND_TO_PAYMENT', N'TO_PAY', 0, N'', 1, 1, 0),
        (1, N'APPROVED', 0, N'Инициатор', N'CANCEL', N'CANCELLED', 0, N'', 1, 1, 0),
        (1, N'TO_PAY', 0, N'Инициатор', N'CANCEL', N'CANCELLED', 0, N'', 1, 1, 0);

    UPDATE actual
       SET TargetStatusCode = expected.TargetStatusCode,
           TargetStageNumber = expected.TargetStageNumber,
           IsVisible = expected.IsVisible,
           IsEnabled = expected.IsEnabled,
           IsProvisional = expected.IsProvisional
      FROM bms_web.WorkflowPermissions AS actual
      INNER JOIN @Permissions AS expected
        ON expected.DefinitionVersion = actual.DefinitionVersion
       AND expected.StatusCode = actual.StatusCode
       AND expected.StageNumber = actual.StageNumber
       AND expected.RoleCode = actual.RoleCode
       AND expected.CommandCode = actual.CommandCode
       AND expected.ConditionCode = actual.ConditionCode;

    INSERT INTO bms_web.WorkflowPermissions
        (DefinitionVersion, StatusCode, StageNumber, RoleCode, CommandCode,
         TargetStatusCode, TargetStageNumber, ConditionCode,
         IsVisible, IsEnabled, IsProvisional)
    SELECT DefinitionVersion, StatusCode, StageNumber, RoleCode, CommandCode,
           TargetStatusCode, TargetStageNumber, ConditionCode,
           IsVisible, IsEnabled, IsProvisional
    FROM @Permissions AS expected
    WHERE NOT EXISTS
    (
        SELECT 1 FROM bms_web.WorkflowPermissions AS actual
        WHERE actual.DefinitionVersion = expected.DefinitionVersion
          AND actual.StatusCode = expected.StatusCode
          AND actual.StageNumber = expected.StageNumber
          AND actual.RoleCode = expected.RoleCode
          AND actual.CommandCode = expected.CommandCode
          AND actual.ConditionCode = expected.ConditionCode
    );

    IF (SELECT COUNT(*) FROM bms_web.DocumentStatuses) <> 11
       OR EXISTS
       (
           SELECT 1
           FROM @Statuses AS expected
           LEFT JOIN bms_web.DocumentStatuses AS actual ON actual.Code = expected.Code
           WHERE actual.Code IS NULL
              OR actual.Name <> expected.Name
              OR actual.LogicalStateCode <> expected.LogicalStateCode
              OR actual.Color <> expected.Color
              OR actual.SortOrder <> expected.SortOrder
              OR actual.IsProvisional <> expected.IsProvisional
       )
        THROW 62011, N'BMS W420 refused: document-status registry is not the approved v1 matrix.', 1;

    IF (SELECT COUNT(*) FROM bms_web.WorkflowCommands) <> 11
       OR EXISTS
       (
           SELECT 1
           FROM @Commands AS expected
           LEFT JOIN bms_web.WorkflowCommands AS actual ON actual.Code = expected.Code
           WHERE actual.Code IS NULL
              OR actual.Name <> expected.Name
              OR actual.RequiresReason <> expected.RequiresReason
              OR actual.IsApprovalAction <> expected.IsApprovalAction
              OR actual.SortOrder <> expected.SortOrder
              OR actual.IsProvisional <> expected.IsProvisional
       )
        THROW 62012, N'BMS W420 refused: workflow-command registry is not the approved v1 matrix.', 1;

    IF (SELECT COUNT(*) FROM bms_web.WorkflowPermissions) <> (SELECT COUNT(*) FROM @Permissions)
       OR EXISTS
       (
           SELECT 1
           FROM @Permissions AS expected
           LEFT JOIN bms_web.WorkflowPermissions AS actual
             ON actual.DefinitionVersion = expected.DefinitionVersion
            AND actual.StatusCode = expected.StatusCode
            AND actual.StageNumber = expected.StageNumber
            AND actual.RoleCode = expected.RoleCode
            AND actual.CommandCode = expected.CommandCode
            AND actual.ConditionCode = expected.ConditionCode
           WHERE actual.DefinitionVersion IS NULL
              OR ISNULL(actual.TargetStatusCode, N'') <> ISNULL(expected.TargetStatusCode, N'')
              OR actual.TargetStageNumber <> expected.TargetStageNumber
              OR actual.IsVisible <> expected.IsVisible
              OR actual.IsEnabled <> expected.IsEnabled
              OR actual.IsProvisional <> expected.IsProvisional
       )
        THROW 62013, N'BMS W420 refused: workflow-permission registry is not the approved v1 matrix.', 1;

    UPDATE bms_web.IdempotencyRecords
       SET ResponseJson = JSON_MODIFY(ResponseJson, N'$.status', N'DRAFT')
     WHERE JSON_VALUE(ResponseJson, N'$.status') = N'Draft';

    IF @Version24Applied = 0
    BEGIN
        EXEC(N'
            IF EXISTS
            (
                SELECT 1
                FROM bms_web.ExpenseRequests
                WHERE [Status] <> N''DRAFT''
                   OR ActiveApprovalRouteSnapshotNumber IS NOT NULL
                   OR ActiveApprovalRouteStageNumber <> 0
            )
                THROW 62014, N''BMS W420 refused: legacy requests did not reach the canonical draft state.'', 1;');
    END;

    IF EXISTS
    (
        SELECT 1
        FROM bms_web.ExpenseRequests AS request
        LEFT JOIN @Statuses AS known_status ON known_status.Code = request.[Status]
        WHERE known_status.Code IS NULL
    )
        THROW 62014, N'BMS W420 refused: legacy requests did not reach the canonical draft state.', 1;

    IF NOT EXISTS
    (
        SELECT 1 FROM bms_web.schema_version WITH (UPDLOCK, HOLDLOCK)
        WHERE version_no = 24
    )
        INSERT INTO bms_web.schema_version (version_no, script_name)
        VALUES (24, N'024_expense_request_workflow_activation.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
