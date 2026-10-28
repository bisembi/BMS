-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/* Cover the existing business-key index for Serializable budget list aggregates.
   No financial rows, approval snapshots, audit entries or predecessor scripts are rewritten.
   Replay validates the registered covered shape; it never silently repairs index drift.
   Schema46 is nonunique. Old applications forbidding INCLUDE are not a verified rollback package. */
IF @@TRANCOUNT<>0 THROW 64700, N'Run SQL047 outside an ambient transaction.', 1;
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase')) THROW 64701, N'SQL047 requires the explicitly owned BMS database.', 1;
IF DATABASEPROPERTYEX(DB_NAME(),N'Updateability')<>N'READ_WRITE'
    THROW 64702, N'SQL047 requires a writable database.', 1;
IF OBJECT_ID(N'bms_web.schema_version',N'U') IS NULL
    OR OBJECT_ID(N'bms_web.BudgetInstanceLines',N'U') IS NULL
    THROW 64703, N'SQL047 requires the registered budget-line storage.', 1;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @registryCount int;
    SELECT @registryCount=COUNT(*) FROM bms_web.schema_version WITH(UPDLOCK,HOLDLOCK);
    IF EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no>47)
        THROW 64704, N'Future schema present.', 1;
    DECLARE @predecessors TABLE(version_no int NOT NULL PRIMARY KEY,script_name nvarchar(256) NOT NULL);
    INSERT INTO @predecessors(version_no,script_name) VALUES
        (1,N'001_bootstrap_bms_web.sql'),
        (2,N'002_budget_projection.sql'),
        (3,N'003_access_scope.sql'),
        (4,N'004_workflow_matrix.sql'),
        (5,N'005_expense_request_evidence.sql'),
        (6,N'006_expense_request_copy_source.sql'),
        (7,N'007_expense_request_line_order.sql'),
        (8,N'008_actor_idempotency_collation.sql'),
        (9,N'009_cash_flow_items_accrual_source.sql'),
        (10,N'010_expense_items_accrual_mapping.sql'),
        (11,N'011_expense_request_line_accrual_snapshot.sql'),
        (12,N'012_money_decimal18_scale2.sql'),
        (13,N'013_counterparties_reference.sql'),
        (14,N'014_contracts_reference.sql'),
        (15,N'015_employee_functional_roles.sql'),
        (16,N'016_currencies_and_daily_rates.sql'),
        (17,N'017_expense_request_currency_rate_snapshot.sql'),
        (18,N'018_expense_request_line_currency_conversion.sql'),
        (19,N'019_overpayment_deficit_storage.sql'),
        (20,N'020_treasury_payment_overpayment_storage.sql'),
        (21,N'021_budget_owner_cost_center_assignments.sql'),
        (22,N'022_expense_request_approval_route_snapshots.sql'),
        (23,N'023_employee_actor_mappings.sql'),
        (24,N'024_expense_request_workflow_activation.sql'),
        (25,N'025_expense_request_accrual_closing_cycles.sql'),
        (26,N'026_expense_request_closing_activation.sql'),
        (27,N'027_accrual_red_zone_notifications.sql'),
        (28,N'028_cash_flow_accrual_settings_version.sql'),
        (29,N'029_expense_request_substitutions.sql'),
        (30,N'030_expense_request_task_scope_index.sql'),
        (31,N'031_expense_request_attachments.sql'),
        (32,N'032_budget_instance_draft.sql'),
        (33,N'033_budget_instance_monthly_period.sql'),
        (34,N'034_budget_cost_center_directory.sql'),
        (35,N'035_budget_instance_approval.sql'),
        (36,N'036_identity_reference_import_watermark.sql'),
        (37,N'037_budget_instance_line_comment.sql'),
        (38,N'038_expense_request_numbering.sql'),
        (39,N'039_counterparty_bin.sql'),
        (40,N'040_cash_flow_activity_type.sql'),
        (41,N'041_bdds_snapshot_and_flow_direction.sql'),
        (42,N'042_cash_flow_actuals_2026.sql'),
        (43,N'043_cash_flow_item_cost_center.sql'),
        (45,N'045_cfo_initiators_and_budget_approval.sql'),
        (46,N'046_budget_instance_operation_rows.sql');
    IF EXISTS(SELECT 1 FROM @predecessors p WHERE NOT EXISTS(
        SELECT 1 FROM bms_web.schema_version v WHERE v.version_no=p.version_no
        AND v.script_name COLLATE Latin1_General_100_BIN2=p.script_name COLLATE Latin1_General_100_BIN2
        AND DATALENGTH(v.script_name)=DATALENGTH(p.script_name)))
        THROW 64705, N'Exact SQL001-SQL043, SQL045 and SQL046 predecessor identities required.', 1;
    DECLARE @registered bit=CASE WHEN EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=47) THEN 1 ELSE 0 END;
    IF @registryCount<>45+CONVERT(int,@registered)
        THROW 64706, N'SQL047 requires a dense, exact schema registry.', 1;
    IF @registered=1 AND NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=47
        AND script_name COLLATE Latin1_General_100_BIN2=N'047_budget_instance_line_list_covering.sql'
        AND DATALENGTH(script_name)=DATALENGTH(N'047_budget_instance_line_list_covering.sql'))
        THROW 64707, N'SQL047 registry identity mismatch.', 1;
    DECLARE @lines int=OBJECT_ID(N'bms_web.BudgetInstanceLines',N'U');
    DECLARE @pk int=(SELECT index_id FROM sys.indexes WHERE object_id=@lines
        AND name COLLATE Latin1_General_100_BIN2=N'PK_bms_web_BudgetInstanceLines'
        AND DATALENGTH(name)=DATALENGTH(N'PK_bms_web_BudgetInstanceLines'));
    DECLARE @ux int=(SELECT index_id FROM sys.indexes WHERE object_id=@lines
        AND name COLLATE Latin1_General_100_BIN2=N'UX_bms_web_BudgetInstanceLines_Key'
        AND DATALENGTH(name)=DATALENGTH(N'UX_bms_web_BudgetInstanceLines_Key'));
    IF @pk IS NULL OR @ux IS NULL OR NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=@lines
        AND name=N'LimitAmountKzt' AND system_type_id=106 AND user_type_id=106
        AND precision=18 AND scale=2 AND is_nullable=0 AND is_computed=0)
        THROW 64708, N'SQL047 expected indexes or money column missing.', 1;
    IF (SELECT COUNT(*) FROM sys.indexes WHERE object_id=@lines AND index_id>0)<>2
        OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=@lines
            AND name COLLATE Latin1_General_100_BIN2=N'PK_bms_web_BudgetInstanceLines'
            AND DATALENGTH(name)=DATALENGTH(N'PK_bms_web_BudgetInstanceLines')
            AND type=1 AND is_primary_key=1 AND is_unique=1 AND is_unique_constraint=0
            AND is_disabled=0 AND is_hypothetical=0 AND has_filter=0 AND filter_definition IS NULL
            AND ignore_dup_key=0)
        OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=@lines
            AND name COLLATE Latin1_General_100_BIN2=N'UX_bms_web_BudgetInstanceLines_Key'
            AND DATALENGTH(name)=DATALENGTH(N'UX_bms_web_BudgetInstanceLines_Key')
            AND type=2 AND is_primary_key=0 AND is_unique=0 AND is_unique_constraint=0
            AND is_disabled=0 AND is_hypothetical=0 AND has_filter=0 AND filter_definition IS NULL
            AND ignore_dup_key=0)
        OR (SELECT COUNT(*) FROM sys.index_columns WHERE object_id=@lines AND index_id=@pk)<>1
        OR NOT EXISTS(SELECT 1 FROM sys.index_columns WHERE object_id=@lines AND index_id=@pk
            AND key_ordinal=1 AND column_id=COLUMNPROPERTY(@lines,N'Id','ColumnId')
            AND is_descending_key=0 AND is_included_column=0)
        OR (SELECT COUNT(*) FROM sys.index_columns WHERE object_id=@lines AND index_id=@ux
            AND key_ordinal>0)<>4
        OR COALESCE((SELECT STRING_AGG(CONCAT(c.name,N':',ic.is_descending_key),N',')
                WITHIN GROUP(ORDER BY ic.key_ordinal)
            FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
            WHERE ic.object_id=@lines AND ic.index_id=@ux AND ic.key_ordinal>0),N'')
                COLLATE Latin1_General_100_BIN2<>N'BudgetInstanceId:0,CashFlowItemId:0,BudgetMonth:0,FlowDirection:0'
        OR (SELECT COUNT(*) FROM sys.index_columns WHERE object_id=@lines AND index_id=@ux
            AND is_included_column=1)<>CONVERT(int,@registered)
        OR EXISTS(SELECT 1 FROM sys.index_columns WHERE object_id=@lines AND index_id=@ux
            AND is_included_column=1 AND
                (column_id<>COLUMNPROPERTY(@lines,N'LimitAmountKzt','ColumnId')
                 OR key_ordinal<>0 OR is_descending_key<>0))
        OR (SELECT COUNT(*) FROM sys.index_columns WHERE object_id=@lines AND index_id=@ux)<>4+CONVERT(int,@registered)
        THROW 64709, N'SQL047 exact budget-line index physical invariant mismatch.', 1;
    IF @registered=0
    BEGIN
        CREATE NONCLUSTERED INDEX UX_bms_web_BudgetInstanceLines_Key
            ON bms_web.BudgetInstanceLines(BudgetInstanceId,CashFlowItemId,BudgetMonth,FlowDirection)
            INCLUDE(LimitAmountKzt) WITH(DROP_EXISTING=ON);
    END;
    IF (SELECT COUNT(*) FROM sys.indexes WHERE object_id=@lines AND index_id>0)<>2
        OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=@lines
            AND name COLLATE Latin1_General_100_BIN2=N'PK_bms_web_BudgetInstanceLines'
            AND DATALENGTH(name)=DATALENGTH(N'PK_bms_web_BudgetInstanceLines')
            AND type=1 AND is_primary_key=1 AND is_unique=1 AND is_unique_constraint=0
            AND is_disabled=0 AND is_hypothetical=0 AND has_filter=0 AND filter_definition IS NULL
            AND ignore_dup_key=0)
        OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=@lines
            AND name COLLATE Latin1_General_100_BIN2=N'UX_bms_web_BudgetInstanceLines_Key'
            AND DATALENGTH(name)=DATALENGTH(N'UX_bms_web_BudgetInstanceLines_Key')
            AND type=2 AND is_primary_key=0 AND is_unique=0 AND is_unique_constraint=0
            AND is_disabled=0 AND is_hypothetical=0 AND has_filter=0 AND filter_definition IS NULL
            AND ignore_dup_key=0)
        OR (SELECT COUNT(*) FROM sys.index_columns WHERE object_id=@lines AND index_id=@pk)<>1
        OR NOT EXISTS(SELECT 1 FROM sys.index_columns WHERE object_id=@lines AND index_id=@pk
            AND key_ordinal=1 AND column_id=COLUMNPROPERTY(@lines,N'Id','ColumnId')
            AND is_descending_key=0 AND is_included_column=0)
        OR (SELECT COUNT(*) FROM sys.index_columns WHERE object_id=@lines AND index_id=@ux
            AND key_ordinal>0)<>4
        OR COALESCE((SELECT STRING_AGG(CONCAT(c.name,N':',ic.is_descending_key),N',')
                WITHIN GROUP(ORDER BY ic.key_ordinal)
            FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
            WHERE ic.object_id=@lines AND ic.index_id=@ux AND ic.key_ordinal>0),N'')
                COLLATE Latin1_General_100_BIN2<>N'BudgetInstanceId:0,CashFlowItemId:0,BudgetMonth:0,FlowDirection:0'
        OR (SELECT COUNT(*) FROM sys.index_columns WHERE object_id=@lines AND index_id=@ux
            AND is_included_column=1)<>1
        OR EXISTS(SELECT 1 FROM sys.index_columns WHERE object_id=@lines AND index_id=@ux
            AND is_included_column=1 AND
                (column_id<>COLUMNPROPERTY(@lines,N'LimitAmountKzt','ColumnId')
                 OR key_ordinal<>0 OR is_descending_key<>0))
        OR (SELECT COUNT(*) FROM sys.index_columns WHERE object_id=@lines AND index_id=@ux)<>4+1
        THROW 64710, N'SQL047 exact budget-line index physical invariant mismatch.', 1;
    IF @registered=0
        INSERT bms_web.schema_version(version_no,script_name)
            VALUES(47,N'047_budget_instance_line_list_covering.sql');
    COMMIT;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK;
    THROW;
END CATCH;
