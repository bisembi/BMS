-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
IF DB_NAME()<>CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase')) AND NOT(DB_NAME()=N'BmsAccrualEdit311_Test'
 AND EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BmsAccrualEdit311Owner'
 AND CONVERT(nvarchar(128),value)=N'BMS_ACCRUAL_EDIT_311_ISOLATED'))
 THROW 65900,N'Exact the explicitly owned BMS database or owned acceptance restore required.',1;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lock int;
 EXEC @lock=sys.sp_getapplock @Resource=N'bms-web:schema-migration',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
 IF @lock<0 THROW 65901,N'Schema lock unavailable.',1;
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=58 AND script_name=N'058_accrual_department.sql')
 OR EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no>59) THROW 65902,N'Exact SQL058 predecessor required.',1;
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=59)
 BEGIN
  IF COL_LENGTH(N'bms_web.ExpenseRequests',N'PaymentOperationJson') IS NOT NULL
   THROW 65903,N'Unregistered operation column refused.',1;
  -- Null preserves legacy supplier payloads and fingerprints. No business UPDATE.
  ALTER TABLE bms_web.ExpenseRequests ADD PaymentOperationJson nvarchar(max) NULL;
  EXEC(N'ALTER TABLE bms_web.ExpenseRequests WITH CHECK ADD CONSTRAINT CK_ExpenseRequests_PaymentOperationJson
   CHECK(PaymentOperationJson IS NULL OR (ISJSON(PaymentOperationJson)=1 AND DATALENGTH(PaymentOperationJson)<=16000));');
  INSERT bms_web.schema_version(version_no,script_name) VALUES(59,N'059_expense_payment_operations.sql');
 END;
 IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.ExpenseRequests')
  AND name=N'PaymentOperationJson' AND system_type_id=231 AND max_length=-1 AND is_nullable=1)
 OR NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'bms_web.ExpenseRequests')
  AND name=N'CK_ExpenseRequests_PaymentOperationJson' AND is_disabled=0 AND is_not_trusted=0)
  THROW 65904,N'Payment operation shape mismatch.',1;
 COMMIT;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
