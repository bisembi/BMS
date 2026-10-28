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
 THROW 65800,N'Exact the explicitly owned BMS database or owned acceptance restore required.',1;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lock int;
 EXEC @lock=sys.sp_getapplock @Resource=N'bms-web:schema-migration',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
 IF @lock<0 THROW 65801,N'Schema lock unavailable.',1;
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=57 AND script_name=N'057_budget_instance_adjustments.sql')
 OR EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no>58) THROW 65802,N'Exact SQL057 predecessor required.',1;
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=58)
 BEGIN
  IF COL_LENGTH(N'bms_web.ExpenseRequestLines',N'AccrualCostCenterId') IS NOT NULL
   THROW 65803,N'Unregistered accrual dimension refused.',1;
  -- Nullable fallback deliberately preserves all historical lines and paid cycle snapshots.
  -- CostCenterId remains the budget and access-control dimension. No business UPDATE.
  ALTER TABLE bms_web.ExpenseRequestLines ADD AccrualCostCenterId uniqueidentifier NULL;
  ALTER TABLE bms_web.ExpenseRequestLines WITH CHECK ADD
   CONSTRAINT FK_ExpenseRequestLines_AccrualCostCenter FOREIGN KEY(AccrualCostCenterId) REFERENCES bms_web.CostCenters(Id),
   CONSTRAINT CK_ExpenseRequestLines_AccrualCostCenter CHECK(AccrualCostCenterId IS NULL OR AccrualCostCenterId<>CONVERT(uniqueidentifier,'00000000-0000-0000-0000-000000000000'));
  INSERT bms_web.schema_version(version_no,script_name) VALUES(58,N'058_accrual_department.sql');
 END;
 IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.ExpenseRequestLines')
  AND name=N'AccrualCostCenterId' AND system_type_id=36 AND is_nullable=1)
 OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'bms_web.ExpenseRequestLines')
  AND name=N'FK_ExpenseRequestLines_AccrualCostCenter' AND is_disabled=0 AND is_not_trusted=0)
  THROW 65804,N'Accrual dimension shape mismatch.',1;
 COMMIT;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
