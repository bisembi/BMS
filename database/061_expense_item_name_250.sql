-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
-- Additive widening only. Never shorten names, change codes or rewrite business rows.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
IF DB_NAME()<>CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase')) AND NOT(DB_NAME()=N'BmsAccrualEdit311_Test'
 AND EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BmsAccrualEdit311Owner'
 AND CONVERT(nvarchar(128),value)=N'BMS_ACCRUAL_EDIT_311_ISOLATED'))
 THROW 66100,N'Exact the explicitly owned BMS database or owned acceptance restore required.',1;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lock int;
 EXEC @lock=sys.sp_getapplock @Resource=N'bms-web:schema-migration',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
 IF @lock<0 THROW 66101,N'Schema lock unavailable.',1;
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=60 AND script_name COLLATE Latin1_General_100_BIN2=N'060_company_budget_version_deletions.sql'
  AND DATALENGTH(script_name)=DATALENGTH(N'060_company_budget_version_deletions.sql'))
 OR EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no>61)
  THROW 66102,N'Exact SQL060 predecessor required.',1;
 IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.ExpenseItems',N'U'))<>3
  THROW 66103,N'ExpenseItems table shape differs.',1;
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=61)
 BEGIN
  IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.ExpenseItems')
   AND name=N'Name' AND system_type_id=231 AND max_length=50 AND is_nullable=0)
   THROW 66104,N'Expected nvarchar(25) NOT NULL predecessor.',1;
  ALTER TABLE bms_web.ExpenseItems ALTER COLUMN Name nvarchar(250) NOT NULL;
  INSERT bms_web.schema_version(version_no,script_name) VALUES(61,N'061_expense_item_name_250.sql');
 END;
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=61 AND script_name COLLATE Latin1_General_100_BIN2=N'061_expense_item_name_250.sql'
  AND DATALENGTH(script_name)=DATALENGTH(N'061_expense_item_name_250.sql'))
 OR NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.ExpenseItems')
   AND name=N'Name' AND system_type_id=231 AND max_length=500 AND is_nullable=0)
  THROW 66105,N'Expense item name extension shape mismatch.',1;
 COMMIT;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
