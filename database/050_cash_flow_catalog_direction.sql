-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
-- Additive catalog direction. Historical budget/actual rows are not rewritten.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME()<>CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase')) THROW 65001,N'Wrong database.',1;
IF @@TRANCOUNT<>0 THROW 65002,N'Ambient transaction refused.',1;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @LockResult int;
 EXEC @LockResult=sys.sp_getapplock @Resource=N'bms-web:company-budget-plan',
  @LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=60000;
 IF @LockResult<0 THROW 65003,N'Migration lock unavailable.',1;
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
(49,N'049_cost_center_directory_metadata.sql');
 DECLARE @Applied bit=CASE WHEN EXISTS(SELECT 1 FROM bms_web.schema_version WITH(UPDLOCK,HOLDLOCK) WHERE version_no=50) THEN 1 ELSE 0 END;
 IF (SELECT COUNT(*) FROM bms_web.schema_version)<>48+CONVERT(int,@Applied)
  OR EXISTS(SELECT 1 FROM @Prior p WHERE NOT EXISTS(SELECT 1 FROM bms_web.schema_version v
   WHERE v.version_no=p.version_no AND v.script_name COLLATE Latin1_General_100_BIN2=p.script_name
    AND DATALENGTH(v.script_name)=DATALENGTH(p.script_name)))
  OR EXISTS(SELECT 1 FROM bms_web.schema_version v WHERE v.version_no<>50 AND NOT EXISTS(SELECT 1 FROM @Prior p WHERE p.version_no=v.version_no))
  OR (@Applied=1 AND NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=50
   AND script_name COLLATE Latin1_General_100_BIN2=N'050_cash_flow_catalog_direction.sql'
   AND DATALENGTH(script_name)=DATALENGTH(N'050_cash_flow_catalog_direction.sql')))
  THROW 65004,N'Exact predecessor registry required.',1;
 IF @Applied=0
 BEGIN
  IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.CashFlowItems'))<>11
   OR COL_LENGTH(N'bms_web.CashFlowItems',N'FlowDirection') IS NOT NULL
   OR OBJECT_ID(N'bms_web.CK_CashFlowItems_FlowDirection') IS NOT NULL
   THROW 65005,N'Unexpected catalog before migration.',1;
  ALTER TABLE bms_web.CashFlowItems ADD FlowDirection nvarchar(10) COLLATE Latin1_General_100_BIN2 NULL;
  EXEC(N'ALTER TABLE bms_web.CashFlowItems WITH CHECK ADD CONSTRAINT CK_CashFlowItems_FlowDirection
   CHECK ([FlowDirection] IS NULL OR ([FlowDirection]=N''Payment'' AND DATALENGTH([FlowDirection])=14)
    OR ([FlowDirection]=N''Receipt'' AND DATALENGTH([FlowDirection])=14));');
 END;
 /* SQL050 CATALOG DIRECTION SHAPE BEGIN */
 DECLARE @CatalogDirectionReady bit=1;
 IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.CashFlowItems'))<>12
  OR NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.CashFlowItems')
   AND name COLLATE Latin1_General_100_BIN2=N'FlowDirection' AND DATALENGTH(name)=DATALENGTH(N'FlowDirection')
   AND column_id=12 AND system_type_id=231 AND user_type_id=231 AND max_length=20 AND is_nullable=1
   AND collation_name=N'Latin1_General_100_BIN2' AND is_computed=0 AND is_identity=0 AND is_sparse=0
   AND is_column_set=0 AND is_hidden=0 AND is_masked=0 AND generated_always_type=0
   AND default_object_id=0 AND rule_object_id=0 AND encryption_type IS NULL)
  OR NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'bms_web.CashFlowItems')
   AND name COLLATE Latin1_General_100_BIN2=N'CK_CashFlowItems_FlowDirection'
   AND DATALENGTH(name)=DATALENGTH(N'CK_CashFlowItems_FlowDirection') AND is_disabled=0 AND is_not_trusted=0
   AND is_not_for_replication=0 AND is_system_named=0 AND parent_column_id=12
   AND REPLACE(REPLACE(REPLACE(REPLACE(definition,N' ',N''),NCHAR(9),N''),NCHAR(13),N''),NCHAR(10),N'')
    COLLATE Latin1_General_100_BIN2=N'([FlowDirection]ISNULLOR[FlowDirection]=N''Payment''ANDdatalength([FlowDirection])=(14)OR[FlowDirection]=N''Receipt''ANDdatalength([FlowDirection])=(14))')
  SET @CatalogDirectionReady=0;
 /* SQL050 CATALOG DIRECTION SHAPE END */
 IF @CatalogDirectionReady<>1 THROW 65006,N'Catalog direction shape differs.',1;
 IF @Applied=0 INSERT INTO bms_web.schema_version(version_no,script_name) VALUES(50,N'050_cash_flow_catalog_direction.sql');
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
