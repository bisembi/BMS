-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
-- SQL051: additive fact-only historical catalog and bounded 2025/2026 actuals.
-- No existing monetary rows, snapshots, mappings or approval states are rewritten.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
IF DB_NAME()<>CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase')) THROW 65101,N'Wrong database.',1;
IF DATABASEPROPERTYEX(DB_NAME(),'Updateability')<>N'READ_WRITE' THROW 65102,N'Database must be writable.',1;
IF @@TRANCOUNT<>0 THROW 65103,N'Ambient transaction refused.',1;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
IF OBJECT_ID(N'bms_web.schema_version',N'U') IS NULL
 OR OBJECT_ID(N'bms_web.CashFlowItems',N'U') IS NULL
 OR OBJECT_ID(N'bms_web.CashFlowActualsSnapshots',N'U') IS NULL
 OR OBJECT_ID(N'bms_web.CashFlowActualsMonths',N'U') IS NULL
 OR OBJECT_ID(N'bms_web.CashFlowActualsArticles',N'U') IS NULL
 OR OBJECT_ID(N'bms_web.CashFlowActualsRows',N'U') IS NULL
 THROW 65104,N'Required managed objects absent.',1;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @LockResult int;
 EXEC @LockResult=sys.sp_getapplock @Resource=N'bms-web:company-budget-plan',
  @LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=60000;
 IF @LockResult<0 THROW 65105,N'Migration lock unavailable.',1;
 DECLARE @Prior TABLE(version_no int PRIMARY KEY,script_name nvarchar(260));
 INSERT INTO @Prior VALUES
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
(46,N'046_budget_instance_operation_rows.sql'),
(47,N'047_budget_instance_line_list_covering.sql'),
(48,N'048_company_budget_versions.sql'),
(49,N'049_cost_center_directory_metadata.sql'),
(50,N'050_cash_flow_catalog_direction.sql');
 DECLARE @Applied bit=CASE WHEN EXISTS(SELECT 1 FROM bms_web.schema_version WITH(UPDLOCK,HOLDLOCK) WHERE version_no=51) THEN 1 ELSE 0 END;
 IF (SELECT COUNT(*) FROM bms_web.schema_version WITH(UPDLOCK,HOLDLOCK))<>49+CONVERT(int,@Applied)
  OR EXISTS(SELECT 1 FROM @Prior p WHERE NOT EXISTS(SELECT 1 FROM bms_web.schema_version v
   WHERE v.version_no=p.version_no AND v.script_name COLLATE Latin1_General_100_BIN2=p.script_name
    AND DATALENGTH(v.script_name)=DATALENGTH(p.script_name)))
  OR EXISTS(SELECT 1 FROM bms_web.schema_version v WHERE v.version_no<>51 AND NOT EXISTS(SELECT 1 FROM @Prior p WHERE p.version_no=v.version_no))
  OR (@Applied=1 AND NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=51
   AND script_name COLLATE Latin1_General_100_BIN2=N'051_cash_flow_actuals_supported_years.sql'
   AND DATALENGTH(script_name)=DATALENGTH(N'051_cash_flow_actuals_supported_years.sql')))
  THROW 65106,N'Exact sparse predecessor registry required.',1;
 DECLARE @OldActualsSnapshotTrigger nvarchar(max)=N'CREATE TRIGGER bms_web.TR_CashFlowActualsSnapshots_Immutable ON bms_web.CashFlowActualsSnapshots AFTER INSERT,UPDATE,DELETE AS
    BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM deleted WHERE IsSealed=1) THROW 64210, N''Sealed actuals are immutable.'',1;
        IF EXISTS
        (
            SELECT 1 FROM inserted AS i WHERE i.IsSealed=1 AND
            (
                i.[RowCount]<>(SELECT COUNT(*) FROM bms_web.CashFlowActualsRows r WHERE r.SnapshotId=i.Id)
                OR i.ArticleCount<>(SELECT COUNT(*) FROM bms_web.CashFlowActualsArticles a WHERE a.SnapshotId=i.Id)
                OR NOT EXISTS(SELECT 1 FROM bms_web.CashFlowActualsMonths m WHERE m.SnapshotId=i.Id)
                OR i.[RowCount]<>i.ArticleCount*(SELECT COUNT(*) FROM bms_web.CashFlowActualsMonths m WHERE m.SnapshotId=i.Id)
                OR i.ReceiptsAmountKzt<>COALESCE((SELECT SUM(CASE WHEN r.SignedAmountKzt>0 THEN r.SignedAmountKzt ELSE 0 END) FROM bms_web.CashFlowActualsRows r WHERE r.SnapshotId=i.Id),0)
                OR i.PaymentsAmountKzt<>COALESCE((SELECT SUM(CASE WHEN r.SignedAmountKzt<0 THEN -r.SignedAmountKzt ELSE 0 END) FROM bms_web.CashFlowActualsRows r WHERE r.SnapshotId=i.Id),0)
            )
        ) THROW 64211,N''Actuals controls do not reconcile.'',1;
    END;';
 IF @Applied=0
 BEGIN
  IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.CashFlowItems'))<>12
   OR COL_LENGTH(N'bms_web.CashFlowItems',N'IsHistoricalForActuals') IS NOT NULL
   OR OBJECT_ID(N'bms_web.DF_CashFlowItems_IsHistoricalForActuals') IS NOT NULL
   OR OBJECT_ID(N'bms_web.CK_CashFlowItems_HistoricalForActuals') IS NOT NULL
   THROW 65107,N'Unexpected catalog before migration.',1;
  DECLARE @OldYearChecks TABLE(TableName sysname,ConstraintName sysname,ColumnId int,ExactDefinition nvarchar(max));
  INSERT INTO @OldYearChecks VALUES
(N'CashFlowActualsSnapshots',N'CK_bms_web_CashFlowActualsSnapshots_Year',2,N'([SourceYear]=(2026))'),
(N'CashFlowActualsMonths',N'CK_bms_web_CashFlowActualsMonths_Year',2,N'(datepart(year,[BudgetMonth])=(2026)ANDdatepart(day,[BudgetMonth])=(1))'),
(N'CashFlowActualsRows',N'CK_bms_web_CashFlowActualsRows_Year',0,N'(datepart(year,[BudgetMonth])=(2026)ANDdatepart(day,[BudgetMonth])=(1)AND[SourceOrdinal]>(0))');
  IF EXISTS(SELECT 1 FROM @OldYearChecks e WHERE NOT EXISTS
   (SELECT 1 FROM sys.check_constraints c WHERE c.parent_object_id=OBJECT_ID(N'bms_web.'+e.TableName)
    AND c.name COLLATE Latin1_General_100_BIN2=e.ConstraintName AND DATALENGTH(c.name)=DATALENGTH(e.ConstraintName)
    AND c.is_disabled=0 AND c.is_not_trusted=0 AND c.is_not_for_replication=0 AND c.is_system_named=0
    AND c.parent_column_id=e.ColumnId AND REPLACE(REPLACE(REPLACE(REPLACE(c.definition,N' ',N''),NCHAR(9),N''),NCHAR(13),N''),NCHAR(10),N'') COLLATE Latin1_General_100_BIN2=e.ExactDefinition))
   THROW 65108,N'Existing year constraints differ.',1;
  DECLARE @OldActualsSnapshotDefinition nvarchar(max)=REPLACE(OBJECT_DEFINITION(OBJECT_ID(N'bms_web.TR_CashFlowActualsSnapshots_Immutable')),NCHAR(13)+NCHAR(10),NCHAR(10));
  IF NOT EXISTS(SELECT 1 FROM sys.triggers t JOIN sys.sql_modules sm ON sm.object_id=t.object_id
    WHERE t.object_id=OBJECT_ID(N'bms_web.TR_CashFlowActualsSnapshots_Immutable')
    AND t.parent_class=1 AND t.parent_id=OBJECT_ID(N'bms_web.CashFlowActualsSnapshots') AND t.type=N'TR'
    AND t.is_disabled=0 AND t.is_instead_of_trigger=0 AND t.is_not_for_replication=0
    AND sm.uses_ansi_nulls=1 AND sm.uses_quoted_identifier=1 AND sm.is_schema_bound=0
    AND (SELECT COUNT(*) FROM sys.trigger_events WHERE object_id=t.object_id)=3
    AND NOT EXISTS(SELECT 1 FROM sys.trigger_events WHERE object_id=t.object_id AND type NOT IN(1,2,3)))
   OR ISNULL(HASHBYTES('SHA2_256',CONVERT(varbinary(max),CASE
    WHEN LEFT(@OldActualsSnapshotDefinition,(DATALENGTH(N'CREATE OR ALTER TRIGGER ')/2)) COLLATE Latin1_General_100_BIN2=N'CREATE OR ALTER TRIGGER ' THEN STUFF(@OldActualsSnapshotDefinition,1,(DATALENGTH(N'CREATE OR ALTER TRIGGER ')/2),N'CREATE TRIGGER ')
    WHEN LEFT(@OldActualsSnapshotDefinition,(DATALENGTH(N'CREATE   TRIGGER ')/2)) COLLATE Latin1_General_100_BIN2=N'CREATE   TRIGGER ' THEN STUFF(@OldActualsSnapshotDefinition,1,(DATALENGTH(N'CREATE   TRIGGER ')/2),N'CREATE TRIGGER ')
    WHEN LEFT(@OldActualsSnapshotDefinition,(DATALENGTH(N'ALTER    TRIGGER ')/2)) COLLATE Latin1_General_100_BIN2=N'ALTER    TRIGGER ' THEN STUFF(@OldActualsSnapshotDefinition,1,(DATALENGTH(N'ALTER    TRIGGER ')/2),N'CREATE TRIGGER ')
    WHEN LEFT(@OldActualsSnapshotDefinition,(DATALENGTH(N'ALTER TRIGGER ')/2)) COLLATE Latin1_General_100_BIN2=N'ALTER TRIGGER ' THEN STUFF(@OldActualsSnapshotDefinition,1,(DATALENGTH(N'ALTER TRIGGER ')/2),N'CREATE TRIGGER ')
    WHEN LEFT(@OldActualsSnapshotDefinition,(DATALENGTH(N'CREATE TRIGGER ')/2)) COLLATE Latin1_General_100_BIN2=N'CREATE TRIGGER ' THEN @OldActualsSnapshotDefinition
    ELSE NULL END)),0x)
    <>HASHBYTES('SHA2_256',CONVERT(varbinary(max),@OldActualsSnapshotTrigger))
   THROW 65109,N'Existing snapshot trigger differs.',1;
  ALTER TABLE bms_web.CashFlowItems ADD IsHistoricalForActuals bit NOT NULL
   CONSTRAINT DF_CashFlowItems_IsHistoricalForActuals DEFAULT ((0)) WITH VALUES;
  EXEC(N'ALTER TABLE bms_web.CashFlowItems WITH CHECK ADD CONSTRAINT CK_CashFlowItems_HistoricalForActuals
   CHECK ([IsHistoricalForActuals]=0 OR ([IsActive]=0 AND [IsGroup]=0 AND [CostCenterId] IS NULL));');
  ALTER TABLE bms_web.CashFlowActualsSnapshots DROP CONSTRAINT CK_bms_web_CashFlowActualsSnapshots_Year;
  ALTER TABLE bms_web.CashFlowActualsSnapshots WITH CHECK ADD CONSTRAINT CK_bms_web_CashFlowActualsSnapshots_Year
   CHECK (SourceYear=2025 OR SourceYear=2026);
  ALTER TABLE bms_web.CashFlowActualsMonths DROP CONSTRAINT CK_bms_web_CashFlowActualsMonths_Year;
  ALTER TABLE bms_web.CashFlowActualsMonths WITH CHECK ADD CONSTRAINT CK_bms_web_CashFlowActualsMonths_Year
   CHECK ((YEAR(BudgetMonth)=2025 OR YEAR(BudgetMonth)=2026) AND DAY(BudgetMonth)=1);
  ALTER TABLE bms_web.CashFlowActualsRows DROP CONSTRAINT CK_bms_web_CashFlowActualsRows_Year;
  ALTER TABLE bms_web.CashFlowActualsRows WITH CHECK ADD CONSTRAINT CK_bms_web_CashFlowActualsRows_Year
   CHECK ((YEAR(BudgetMonth)=2025 OR YEAR(BudgetMonth)=2026) AND DAY(BudgetMonth)=1 AND SourceOrdinal>0);
 END;
 IF @Applied=0
 BEGIN
  DECLARE @NewActualsSnapshotTrigger nvarchar(max)=STUFF(N'CREATE TRIGGER bms_web.TR_CashFlowActualsSnapshots_Immutable ON bms_web.CashFlowActualsSnapshots AFTER INSERT,UPDATE,DELETE AS
    BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM deleted WHERE IsSealed=1) THROW 64210, N''Sealed actuals are immutable.'',1;
        IF EXISTS
        (
            SELECT 1 FROM inserted AS i WHERE i.IsSealed=1 AND
            (
                i.[RowCount]<>(SELECT COUNT(*) FROM bms_web.CashFlowActualsRows r WHERE r.SnapshotId=i.Id)
                OR i.ArticleCount<>(SELECT COUNT(*) FROM bms_web.CashFlowActualsArticles a WHERE a.SnapshotId=i.Id)
                OR NOT EXISTS(SELECT 1 FROM bms_web.CashFlowActualsMonths m WHERE m.SnapshotId=i.Id)
                OR EXISTS(SELECT 1 FROM bms_web.CashFlowActualsMonths m WHERE m.SnapshotId=i.Id AND YEAR(m.BudgetMonth)<>i.SourceYear)
                OR i.[RowCount]<>i.ArticleCount*(SELECT COUNT(*) FROM bms_web.CashFlowActualsMonths m WHERE m.SnapshotId=i.Id)
                OR i.ReceiptsAmountKzt<>COALESCE((SELECT SUM(CASE WHEN r.SignedAmountKzt>0 THEN r.SignedAmountKzt ELSE 0 END) FROM bms_web.CashFlowActualsRows r WHERE r.SnapshotId=i.Id),0)
                OR i.PaymentsAmountKzt<>COALESCE((SELECT SUM(CASE WHEN r.SignedAmountKzt<0 THEN -r.SignedAmountKzt ELSE 0 END) FROM bms_web.CashFlowActualsRows r WHERE r.SnapshotId=i.Id),0)
            )
        ) THROW 64211,N''Actuals controls do not reconcile.'',1;
    END;',1,DATALENGTH(N'CREATE TRIGGER ')/2,N'ALTER TRIGGER ');
  EXEC(@NewActualsSnapshotTrigger);
 END;
 /* SQL051 ACTUALS YEARS SHAPE BEGIN */
 DECLARE @ActualsYearsReady bit=1;
 DECLARE @ActualsSnapshotTrigger nvarchar(max)=N'CREATE TRIGGER bms_web.TR_CashFlowActualsSnapshots_Immutable ON bms_web.CashFlowActualsSnapshots AFTER INSERT,UPDATE,DELETE AS
    BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM deleted WHERE IsSealed=1) THROW 64210, N''Sealed actuals are immutable.'',1;
        IF EXISTS
        (
            SELECT 1 FROM inserted AS i WHERE i.IsSealed=1 AND
            (
                i.[RowCount]<>(SELECT COUNT(*) FROM bms_web.CashFlowActualsRows r WHERE r.SnapshotId=i.Id)
                OR i.ArticleCount<>(SELECT COUNT(*) FROM bms_web.CashFlowActualsArticles a WHERE a.SnapshotId=i.Id)
                OR NOT EXISTS(SELECT 1 FROM bms_web.CashFlowActualsMonths m WHERE m.SnapshotId=i.Id)
                OR EXISTS(SELECT 1 FROM bms_web.CashFlowActualsMonths m WHERE m.SnapshotId=i.Id AND YEAR(m.BudgetMonth)<>i.SourceYear)
                OR i.[RowCount]<>i.ArticleCount*(SELECT COUNT(*) FROM bms_web.CashFlowActualsMonths m WHERE m.SnapshotId=i.Id)
                OR i.ReceiptsAmountKzt<>COALESCE((SELECT SUM(CASE WHEN r.SignedAmountKzt>0 THEN r.SignedAmountKzt ELSE 0 END) FROM bms_web.CashFlowActualsRows r WHERE r.SnapshotId=i.Id),0)
                OR i.PaymentsAmountKzt<>COALESCE((SELECT SUM(CASE WHEN r.SignedAmountKzt<0 THEN -r.SignedAmountKzt ELSE 0 END) FROM bms_web.CashFlowActualsRows r WHERE r.SnapshotId=i.Id),0)
            )
        ) THROW 64211,N''Actuals controls do not reconcile.'',1;
    END;';
 DECLARE @ActualsTriggerShapes TABLE(TriggerName sysname,TableName sysname,ExactDefinition nvarchar(max));
 INSERT INTO @ActualsTriggerShapes VALUES
 (N'TR_CashFlowActualsSnapshots_Immutable',N'CashFlowActualsSnapshots',@ActualsSnapshotTrigger),
 (N'TR_CashFlowActualsMonths_Immutable',N'CashFlowActualsMonths',N'CREATE TRIGGER bms_web.TR_CashFlowActualsMonths_Immutable ON bms_web.CashFlowActualsMonths AFTER INSERT,UPDATE,DELETE AS
    BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM bms_web.CashFlowActualsSnapshots s WHERE s.IsSealed=1 AND s.Id IN(SELECT SnapshotId FROM inserted UNION SELECT SnapshotId FROM deleted))
            THROW 64212,N''Sealed actuals months are immutable.'',1;
    END;'),
 (N'TR_CashFlowActualsArticles_Immutable',N'CashFlowActualsArticles',N'CREATE TRIGGER bms_web.TR_CashFlowActualsArticles_Immutable ON bms_web.CashFlowActualsArticles AFTER INSERT,UPDATE,DELETE AS
    BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM bms_web.CashFlowActualsSnapshots s WHERE s.IsSealed=1 AND s.Id IN(SELECT SnapshotId FROM inserted UNION SELECT SnapshotId FROM deleted))
            THROW 64213,N''Sealed actuals articles are immutable.'',1;
    END;'),
 (N'TR_CashFlowActualsRows_Immutable',N'CashFlowActualsRows',N'CREATE TRIGGER bms_web.TR_CashFlowActualsRows_Immutable ON bms_web.CashFlowActualsRows AFTER INSERT,UPDATE,DELETE AS
    BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM bms_web.CashFlowActualsSnapshots s WHERE s.IsSealed=1 AND s.Id IN(SELECT SnapshotId FROM inserted UNION SELECT SnapshotId FROM deleted))
            THROW 64214,N''Sealed actuals rows are immutable.'',1;
    END;');
 DECLARE @ActualsYearCheckShapes TABLE(TableName sysname,ConstraintName sysname,ColumnId int,ExactDefinition nvarchar(max));
 INSERT INTO @ActualsYearCheckShapes VALUES
 (N'CashFlowActualsSnapshots',N'CK_bms_web_CashFlowActualsSnapshots_Year',2,N'([SourceYear]=(2025)OR[SourceYear]=(2026))'),
 (N'CashFlowActualsMonths',N'CK_bms_web_CashFlowActualsMonths_Year',2,N'((datepart(year,[BudgetMonth])=(2025)ORdatepart(year,[BudgetMonth])=(2026))ANDdatepart(day,[BudgetMonth])=(1))'),
 (N'CashFlowActualsRows',N'CK_bms_web_CashFlowActualsRows_Year',0,N'((datepart(year,[BudgetMonth])=(2025)ORdatepart(year,[BudgetMonth])=(2026))ANDdatepart(day,[BudgetMonth])=(1)AND[SourceOrdinal]>(0))');
 DECLARE @ActualsRequiredColumns TABLE(TableName sysname,ColumnName sysname,ColumnId int,TypeId int,MaxLength int,Nullable bit);
 INSERT INTO @ActualsRequiredColumns VALUES
 (N'CashFlowItems',N'IsActive',5,104,1,0),
 (N'CashFlowItems',N'IsGroup',6,104,1,0),
 (N'CashFlowItems',N'CostCenterId',11,36,16,1),
 (N'CashFlowActualsSnapshots',N'SourceYear',2,56,4,0),
 (N'CashFlowActualsMonths',N'BudgetMonth',2,40,3,0),
 (N'CashFlowActualsRows',N'SourceOrdinal',2,56,4,0),
 (N'CashFlowActualsRows',N'BudgetMonth',4,40,3,0);
 IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.CashFlowItems'))<>13
  OR EXISTS(SELECT 1 FROM @ActualsRequiredColumns e WHERE NOT EXISTS
   (SELECT 1 FROM sys.columns c WHERE c.object_id=OBJECT_ID(N'bms_web.'+e.TableName)
    AND c.name COLLATE Latin1_General_100_BIN2=e.ColumnName AND DATALENGTH(c.name)=DATALENGTH(e.ColumnName)
    AND c.column_id=e.ColumnId AND c.system_type_id=e.TypeId AND c.user_type_id=e.TypeId
    AND c.max_length=e.MaxLength AND c.is_nullable=e.Nullable AND c.is_computed=0 AND c.is_identity=0
    AND c.is_sparse=0 AND c.is_column_set=0 AND c.is_hidden=0 AND c.is_masked=0
    AND c.generated_always_type=0 AND c.default_object_id=0 AND c.rule_object_id=0 AND c.encryption_type IS NULL))
  OR NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.CashFlowItems')
   AND name COLLATE Latin1_General_100_BIN2=N'IsHistoricalForActuals' AND DATALENGTH(name)=DATALENGTH(N'IsHistoricalForActuals')
   AND column_id=13 AND system_type_id=104 AND user_type_id=104 AND max_length=1 AND is_nullable=0
   AND is_computed=0 AND is_identity=0 AND is_sparse=0 AND is_column_set=0 AND is_hidden=0 AND is_masked=0
   AND generated_always_type=0 AND rule_object_id=0 AND encryption_type IS NULL
   AND default_object_id=OBJECT_ID(N'bms_web.DF_CashFlowItems_IsHistoricalForActuals'))
  OR NOT EXISTS(SELECT 1 FROM sys.default_constraints d
   WHERE d.parent_object_id=OBJECT_ID(N'bms_web.CashFlowItems') AND d.parent_column_id=13
   AND d.name COLLATE Latin1_General_100_BIN2=N'DF_CashFlowItems_IsHistoricalForActuals'
   AND DATALENGTH(d.name)=DATALENGTH(N'DF_CashFlowItems_IsHistoricalForActuals') AND d.is_system_named=0
   AND d.definition COLLATE Latin1_General_100_BIN2=N'((0))')
  OR NOT EXISTS(SELECT 1 FROM sys.check_constraints c WHERE c.parent_object_id=OBJECT_ID(N'bms_web.CashFlowItems')
   AND c.name COLLATE Latin1_General_100_BIN2=N'CK_CashFlowItems_HistoricalForActuals'
   AND DATALENGTH(c.name)=DATALENGTH(N'CK_CashFlowItems_HistoricalForActuals') AND c.parent_column_id=0
   AND c.is_disabled=0 AND c.is_not_trusted=0 AND c.is_not_for_replication=0 AND c.is_system_named=0
   AND REPLACE(REPLACE(REPLACE(REPLACE(c.definition,N' ',N''),NCHAR(9),N''),NCHAR(13),N''),NCHAR(10),N'') COLLATE Latin1_General_100_BIN2=N'([IsHistoricalForActuals]=(0)OR[IsActive]=(0)AND[IsGroup]=(0)AND[CostCenterId]ISNULL)')
  OR EXISTS(SELECT 1 FROM @ActualsYearCheckShapes e WHERE NOT EXISTS
   (SELECT 1 FROM sys.check_constraints c WHERE c.parent_object_id=OBJECT_ID(N'bms_web.'+e.TableName)
    AND c.name COLLATE Latin1_General_100_BIN2=e.ConstraintName AND DATALENGTH(c.name)=DATALENGTH(e.ConstraintName)
    AND c.is_disabled=0 AND c.is_not_trusted=0 AND c.is_not_for_replication=0 AND c.is_system_named=0
    AND c.parent_column_id=e.ColumnId AND REPLACE(REPLACE(REPLACE(REPLACE(c.definition,N' ',N''),NCHAR(9),N''),NCHAR(13),N''),NCHAR(10),N'') COLLATE Latin1_General_100_BIN2=e.ExactDefinition))
  SET @ActualsYearsReady=0;
 IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys f
  WHERE f.object_id=OBJECT_ID(N'bms_web.FK_bms_web_CashFlowActualsRows_Month')
   AND f.name COLLATE Latin1_General_100_BIN2=N'FK_bms_web_CashFlowActualsRows_Month'
   AND DATALENGTH(f.name)=DATALENGTH(N'FK_bms_web_CashFlowActualsRows_Month')
   AND f.parent_object_id=OBJECT_ID(N'bms_web.CashFlowActualsRows')
   AND f.referenced_object_id=OBJECT_ID(N'bms_web.CashFlowActualsMonths')
   AND f.is_disabled=0 AND f.is_not_trusted=0 AND f.is_not_for_replication=0
   AND f.delete_referential_action=0 AND f.update_referential_action=0
   AND (SELECT COUNT(*) FROM sys.foreign_key_columns WHERE constraint_object_id=f.object_id)=2
   AND EXISTS(SELECT 1 FROM sys.foreign_key_columns WHERE constraint_object_id=f.object_id
    AND constraint_column_id=1 AND parent_column_id=1 AND referenced_column_id=1)
   AND EXISTS(SELECT 1 FROM sys.foreign_key_columns WHERE constraint_object_id=f.object_id
    AND constraint_column_id=2 AND parent_column_id=4 AND referenced_column_id=2))
  OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys f
   WHERE f.object_id=OBJECT_ID(N'bms_web.FK_bms_web_CashFlowActualsMonths_Snapshot')
    AND f.name COLLATE Latin1_General_100_BIN2=N'FK_bms_web_CashFlowActualsMonths_Snapshot'
    AND DATALENGTH(f.name)=DATALENGTH(N'FK_bms_web_CashFlowActualsMonths_Snapshot')
    AND f.parent_object_id=OBJECT_ID(N'bms_web.CashFlowActualsMonths')
    AND f.referenced_object_id=OBJECT_ID(N'bms_web.CashFlowActualsSnapshots')
    AND f.is_disabled=0 AND f.is_not_trusted=0 AND f.is_not_for_replication=0
    AND f.delete_referential_action=0 AND f.update_referential_action=0
    AND (SELECT COUNT(*) FROM sys.foreign_key_columns WHERE constraint_object_id=f.object_id)=1
    AND EXISTS(SELECT 1 FROM sys.foreign_key_columns WHERE constraint_object_id=f.object_id
     AND constraint_column_id=1 AND parent_column_id=1 AND referenced_column_id=1))
  SET @ActualsYearsReady=0;
 IF EXISTS(SELECT 1 FROM @ActualsTriggerShapes e
  LEFT JOIN sys.triggers t ON t.object_id=OBJECT_ID(N'bms_web.'+e.TriggerName)
  LEFT JOIN sys.sql_modules sm ON sm.object_id=t.object_id
  CROSS APPLY(SELECT REPLACE(sm.definition,NCHAR(13)+NCHAR(10),NCHAR(10)) AS ModuleDefinition) d
  CROSS APPLY(SELECT CASE
    WHEN LEFT(d.ModuleDefinition,(DATALENGTH(N'CREATE OR ALTER TRIGGER ')/2)) COLLATE Latin1_General_100_BIN2=N'CREATE OR ALTER TRIGGER ' THEN STUFF(d.ModuleDefinition,1,(DATALENGTH(N'CREATE OR ALTER TRIGGER ')/2),N'CREATE TRIGGER ')
    WHEN LEFT(d.ModuleDefinition,(DATALENGTH(N'CREATE   TRIGGER ')/2)) COLLATE Latin1_General_100_BIN2=N'CREATE   TRIGGER ' THEN STUFF(d.ModuleDefinition,1,(DATALENGTH(N'CREATE   TRIGGER ')/2),N'CREATE TRIGGER ')
    WHEN LEFT(d.ModuleDefinition,(DATALENGTH(N'ALTER    TRIGGER ')/2)) COLLATE Latin1_General_100_BIN2=N'ALTER    TRIGGER ' THEN STUFF(d.ModuleDefinition,1,(DATALENGTH(N'ALTER    TRIGGER ')/2),N'CREATE TRIGGER ')
    WHEN LEFT(d.ModuleDefinition,(DATALENGTH(N'ALTER TRIGGER ')/2)) COLLATE Latin1_General_100_BIN2=N'ALTER TRIGGER ' THEN STUFF(d.ModuleDefinition,1,(DATALENGTH(N'ALTER TRIGGER ')/2),N'CREATE TRIGGER ')
    WHEN LEFT(d.ModuleDefinition,(DATALENGTH(N'CREATE TRIGGER ')/2)) COLLATE Latin1_General_100_BIN2=N'CREATE TRIGGER ' THEN d.ModuleDefinition
    ELSE NULL END AS CanonicalDefinition) canonical
  WHERE t.object_id IS NULL OR t.name COLLATE Latin1_General_100_BIN2<>e.TriggerName OR DATALENGTH(t.name)<>DATALENGTH(e.TriggerName)
   OR t.parent_class<>1 OR t.parent_id<>OBJECT_ID(N'bms_web.'+e.TableName) OR t.type<>N'TR'
   OR t.is_disabled<>0 OR t.is_instead_of_trigger<>0 OR t.is_not_for_replication<>0
   OR sm.uses_ansi_nulls<>1 OR sm.uses_quoted_identifier<>1 OR sm.is_schema_bound<>0 OR sm.definition IS NULL
   OR (SELECT COUNT(*) FROM sys.trigger_events WHERE object_id=t.object_id)<>3
   OR EXISTS(SELECT 1 FROM sys.trigger_events WHERE object_id=t.object_id AND type NOT IN(1,2,3))
   OR ISNULL(HASHBYTES('SHA2_256',CONVERT(varbinary(max),canonical.CanonicalDefinition)),0x)<>HASHBYTES('SHA2_256',CONVERT(varbinary(max),e.ExactDefinition)))
  SET @ActualsYearsReady=0;
 /* SQL051 ACTUALS YEARS SHAPE END */
 IF @ActualsYearsReady<>1 THROW 65110,N'SQL051 shape differs.',1;
 IF @Applied=0 INSERT INTO bms_web.schema_version(version_no,script_name) VALUES(51,N'051_cash_flow_actuals_supported_years.sql');
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
