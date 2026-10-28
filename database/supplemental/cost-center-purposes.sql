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
IF DB_NAME() NOT IN(CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase')),N'BmsCostOnly457_Test') THROW 65457,'PURPOSE457_DATABASE',1;
IF @@TRANCOUNT=0 THROW 65457,'PURPOSE457_OUTER_TRANSACTION_REQUIRED',1;
IF DB_NAME()=N'BmsCostOnly457_Test' AND NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BmsCostOnly457Owner' AND CONVERT(nvarchar(128),value)=N'BMS_COSTONLY457_ISOLATED') THROW 65457,'PURPOSE457_OWNER',1;
IF OBJECT_ID(N'bms_web.CostCenterPurposes457',N'U') IS NULL
 CREATE TABLE bms_web.CostCenterPurposes457(
  CostCenterId uniqueidentifier NOT NULL CONSTRAINT PK_CostCenterPurposes457 PRIMARY KEY,
  OnlyCostDepartment bit NOT NULL,
  ConcurrencyStamp uniqueidentifier NOT NULL,
  CONSTRAINT FK_CostCenterPurposes457_CostCenters FOREIGN KEY(CostCenterId) REFERENCES bms_web.CostCenters(Id),
  CONSTRAINT CK_CostCenterPurposes457_Stamp CHECK(ConcurrencyStamp<>'00000000-0000-0000-0000-000000000000'));
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.CostCenterPurposes457'))<>3
 OR COL_LENGTH(N'bms_web.CostCenterPurposes457',N'CostCenterId')<>16
 OR COL_LENGTH(N'bms_web.CostCenterPurposes457',N'OnlyCostDepartment')<>1
 OR COL_LENGTH(N'bms_web.CostCenterPurposes457',N'ConcurrencyStamp')<>16 THROW 65457,'PURPOSE457_SHAPE',1;
IF DB_NAME()=CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
BEGIN
 IF DATABASE_PRINCIPAL_ID(N'bms_current_runtime') IS NULL THROW 65457,'PURPOSE457_RUNTIME_PRINCIPAL',1;
 GRANT SELECT,INSERT,UPDATE ON OBJECT::bms_web.CostCenterPurposes457 TO bms_current_runtime;
 DENY DELETE,ALTER ON OBJECT::bms_web.CostCenterPurposes457 TO bms_current_runtime;
END;
