-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
-- SQL052: additive persistent presentation order for the existing cash-flow catalogue.
-- Only DisplayOrder is initialized; classifications, directions, accrual/CFO links and money are untouched.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME()<>CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase')) THROW 65201,N'Wrong database.',1;
IF DATABASEPROPERTYEX(DB_NAME(),'Updateability')<>N'READ_WRITE' THROW 65202,N'Database must be writable.',1;
IF @@TRANCOUNT<>0 THROW 65203,N'Ambient transaction refused.',1;
IF OBJECT_ID(N'bms_web.schema_version',N'U') IS NULL
 OR OBJECT_ID(N'bms_web.CashFlowItems',N'U') IS NULL
 THROW 65204,N'Required managed objects absent.',1;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @LockResult int;
 EXEC @LockResult=sys.sp_getapplock @Resource=N'bms-web:company-budget-plan',
  @LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=60000;
 IF @LockResult<0 THROW 65205,N'Migration lock unavailable.',1;

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
(50,N'050_cash_flow_catalog_direction.sql'),
(51,N'051_cash_flow_actuals_supported_years.sql');
 DECLARE @Applied bit=CASE WHEN EXISTS(SELECT 1 FROM bms_web.schema_version WITH(UPDLOCK,HOLDLOCK)
  WHERE version_no=52) THEN 1 ELSE 0 END;
 IF (SELECT COUNT(*) FROM bms_web.schema_version WITH(UPDLOCK,HOLDLOCK))<>50+CONVERT(int,@Applied)
  OR EXISTS(SELECT 1 FROM @Prior p WHERE NOT EXISTS(SELECT 1 FROM bms_web.schema_version v
   WHERE v.version_no=p.version_no AND v.script_name COLLATE Latin1_General_100_BIN2=p.script_name
    AND DATALENGTH(v.script_name)=DATALENGTH(p.script_name)))
  OR EXISTS(SELECT 1 FROM bms_web.schema_version v WHERE v.version_no<>52
   AND NOT EXISTS(SELECT 1 FROM @Prior p WHERE p.version_no=v.version_no))
  OR (@Applied=1 AND NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=52
   AND script_name COLLATE Latin1_General_100_BIN2=N'052_cash_flow_item_display_order.sql'
   AND DATALENGTH(script_name)=DATALENGTH(N'052_cash_flow_item_display_order.sql')))
  THROW 65206,N'Exact sparse schema51 predecessor registry required.',1;

 DECLARE @RowsBefore int=(SELECT COUNT(*) FROM bms_web.CashFlowItems WITH(HOLDLOCK));
 DECLARE @EligibleBefore int=(SELECT COUNT(*) FROM bms_web.CashFlowItems WITH(HOLDLOCK)
  WHERE IsActive=1 AND IsGroup=0 AND IsHistoricalForActuals=0);
 IF @EligibleBefore>10000 THROW 65207,N'Cash-flow order exceeds the supported bound.',1;
 IF @Applied=0
 BEGIN
  IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.CashFlowItems'))<>13
   OR COL_LENGTH(N'bms_web.CashFlowItems',N'DisplayOrder') IS NOT NULL
   OR OBJECT_ID(N'bms_web.CK_CashFlowItems_DisplayOrder') IS NOT NULL
   THROW 65208,N'Unexpected cash-flow catalogue before migration.',1;
  ALTER TABLE bms_web.CashFlowItems ADD DisplayOrder int NULL;
  EXEC sys.sp_executesql N'ALTER TABLE bms_web.CashFlowItems WITH CHECK
   ADD CONSTRAINT CK_CashFlowItems_DisplayOrder
   CHECK ([DisplayOrder] IS NULL OR [DisplayOrder]>=(0));';

  -- DisplayOrder did not exist when this outer batch was compiled. Keep every
  -- reference to the new column in a post-DDL dynamic batch so a genuine
  -- schema-51 apply cannot fail name resolution before ALTER runs.
  EXEC sys.sp_executesql N'
  ;WITH Labels AS
  (
   SELECT item.Id,item.Code,item.Name,item.ActivityType,item.FlowDirection,
    LTRIM(RTRIM(COALESCE(parentItem.Name,N''''))) AS RawGroup
   FROM bms_web.CashFlowItems item
   LEFT JOIN bms_web.CashFlowItems parentItem ON parentItem.Id=item.ParentId AND parentItem.IsGroup=1
   WHERE item.IsActive=1 AND item.IsGroup=0 AND item.IsHistoricalForActuals=0
  ), Prefix1 AS
  (
   SELECT *,CASE WHEN RawGroup LIKE N''[0-9]%[.] %'' AND CHARINDEX(N'' '',RawGroup)>0
    THEN LTRIM(SUBSTRING(RawGroup,CHARINDEX(N'' '',RawGroup)+1,150)) ELSE RawGroup END AS Group1
   FROM Labels
  ), Prefix2 AS
  (
   SELECT *,CASE WHEN UPPER(Group1) LIKE N''CF[0-9]% %'' AND CHARINDEX(N'' '',Group1)>0
    THEN LTRIM(SUBSTRING(Group1,CHARINDEX(N'' '',Group1)+1,150)) ELSE Group1 END AS Group2
   FROM Prefix1
  ), Canonical AS
  (
   SELECT *,UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(Group2,
    N''  '',N'' ''),N''  '',N'' ''),N''  '',N'' ''),N''  '',N'' ''),N''  '',N'' ''),N''  '',N'' '')) AS GroupKey
   FROM Prefix2
  ), Ranked AS
  (
   SELECT Id,CONVERT(int,ROW_NUMBER() OVER(ORDER BY
    CASE ActivityType WHEN N''Operating'' THEN 0 WHEN N''Investing'' THEN 1 WHEN N''Financing'' THEN 2 ELSE 3 END,
    CASE FlowDirection WHEN N''Receipt'' THEN 0 WHEN N''Payment'' THEN 1 ELSE 2 END,
    CASE WHEN ActivityType=N''Operating'' AND FlowDirection=N''Receipt''
      AND GroupKey IN(N''ПРОЧИЕ ПОСТУПЛЕНИЯ'',N''ПРОЧИЕ ОПЕРАЦИОННЫЕ ПОСТУПЛЕНИЯ'')
     THEN N''ПРОЧИЕ ОПЕРАЦИОННЫЕ ПОСТУПЛЕНИЯ'' ELSE GroupKey END COLLATE Latin1_General_100_BIN2,
    Code COLLATE Latin1_General_100_BIN2,Name,Id)-1) AS DisplayOrder
   FROM Canonical
  )
  UPDATE item SET [DisplayOrder]=ranked.DisplayOrder
  FROM bms_web.CashFlowItems item INNER JOIN Ranked ranked ON ranked.Id=item.Id;';
 END;

 /* SQL052 CASH FLOW DISPLAY ORDER SHAPE BEGIN */
 DECLARE @InitialRanksReady bit=1;
 IF @Applied=0
  EXEC sys.sp_executesql N'
   SELECT @Ready=CONVERT(bit,CASE
    WHEN EXISTS(SELECT 1 FROM bms_web.CashFlowItems
      WHERE IsActive=1 AND IsGroup=0 AND IsHistoricalForActuals=0 AND [DisplayOrder] IS NULL)
      OR (SELECT COUNT(DISTINCT [DisplayOrder]) FROM bms_web.CashFlowItems
       WHERE IsActive=1 AND IsGroup=0 AND IsHistoricalForActuals=0)<>@Eligible
    THEN 0 ELSE 1 END);',
   N'@Eligible int,@Ready bit OUTPUT',@Eligible=@EligibleBefore,@Ready=@InitialRanksReady OUTPUT;
 DECLARE @DisplayOrderReady bit=1;
 IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.CashFlowItems'))<>14
  OR NOT EXISTS(SELECT 1 FROM sys.columns c WHERE c.object_id=OBJECT_ID(N'bms_web.CashFlowItems')
   AND c.name COLLATE Latin1_General_100_BIN2=N'DisplayOrder'
   AND DATALENGTH(c.name)=DATALENGTH(N'DisplayOrder') AND c.column_id=14
   AND c.system_type_id=56 AND c.user_type_id=56 AND c.max_length=4 AND c.is_nullable=1
   AND c.is_computed=0 AND c.is_identity=0 AND c.is_sparse=0 AND c.is_column_set=0
   AND c.is_hidden=0 AND c.is_masked=0 AND c.generated_always_type=0
   AND c.default_object_id=0 AND c.rule_object_id=0 AND c.encryption_type IS NULL)
  OR NOT EXISTS(SELECT 1 FROM sys.check_constraints c
   WHERE c.parent_object_id=OBJECT_ID(N'bms_web.CashFlowItems')
    AND c.name COLLATE Latin1_General_100_BIN2=N'CK_CashFlowItems_DisplayOrder'
    AND DATALENGTH(c.name)=DATALENGTH(N'CK_CashFlowItems_DisplayOrder')
    AND c.parent_column_id=14 AND c.is_disabled=0 AND c.is_not_trusted=0
    AND c.is_not_for_replication=0 AND c.is_system_named=0
    AND REPLACE(REPLACE(REPLACE(REPLACE(c.definition,N' ',N''),NCHAR(9),N''),NCHAR(13),N''),NCHAR(10),N'')
     COLLATE Latin1_General_100_BIN2=N'([DisplayOrder]ISNULLOR[DisplayOrder]>=(0))')
  OR EXISTS(SELECT 1 FROM sys.index_columns WHERE object_id=OBJECT_ID(N'bms_web.CashFlowItems')
   AND column_id=14)
  OR EXISTS(SELECT 1 FROM sys.foreign_key_columns WHERE
   (parent_object_id=OBJECT_ID(N'bms_web.CashFlowItems') AND parent_column_id=14)
   OR (referenced_object_id=OBJECT_ID(N'bms_web.CashFlowItems') AND referenced_column_id=14))
  OR (SELECT COUNT(*) FROM bms_web.CashFlowItems)<>@RowsBefore
  OR (SELECT COUNT(*) FROM bms_web.CashFlowItems
      WHERE IsActive=1 AND IsGroup=0 AND IsHistoricalForActuals=0)<>@EligibleBefore
  OR @InitialRanksReady<>1
  SET @DisplayOrderReady=0;
 /* SQL052 CASH FLOW DISPLAY ORDER SHAPE END */
 IF @DisplayOrderReady<>1 THROW 65209,N'SQL052 display-order shape differs.',1;
 IF @Applied=0 INSERT INTO bms_web.schema_version(version_no,script_name)
  VALUES(52,N'052_cash_flow_item_display_order.sql');
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
