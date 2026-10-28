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
 THROW 66000,N'Exact the explicitly owned BMS database or owned acceptance restore required.',1;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lock int;
 EXEC @lock=sys.sp_getapplock @Resource=N'bms-web:schema-migration',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
 IF @lock<0 THROW 66001,N'Schema lock unavailable.',1;
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=59 AND script_name=N'059_expense_payment_operations.sql')
 OR EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no>60) THROW 66002,N'Exact SQL059 predecessor required.',1;
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=60)
 BEGIN
  IF OBJECT_ID(N'bms_web.CompanyBudgetVersionDeletions') IS NOT NULL THROW 66003,N'Unregistered deletion table refused.',1;
  CREATE TABLE bms_web.CompanyBudgetVersionDeletions(
   VersionId uniqueidentifier NOT NULL CONSTRAINT PK_CompanyBudgetVersionDeletions PRIMARY KEY,
   EmployeeId uniqueidentifier NOT NULL,
   ActorExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
   DeletedAtUtc datetimeoffset(3) NOT NULL,
   CONSTRAINT FK_CompanyBudgetVersionDeletions_Version FOREIGN KEY(VersionId) REFERENCES bms_web.CompanyBudgetVersions(Id),
   CONSTRAINT FK_CompanyBudgetVersionDeletions_Employee FOREIGN KEY(EmployeeId) REFERENCES bms_web.Employees(Id),
   CONSTRAINT CK_CompanyBudgetVersionDeletions_Actor CHECK(LEN(LTRIM(RTRIM(ActorExternalId)))>0 AND DATALENGTH(ActorExternalId)=DATALENGTH(LTRIM(RTRIM(ActorExternalId)))),
   CONSTRAINT CK_CompanyBudgetVersionDeletions_UTC CHECK(DATEPART(TZOFFSET,DeletedAtUtc)=0));
  EXEC(N'CREATE TRIGGER bms_web.TR_CompanyBudgetVersionDeletions_Immutable ON bms_web.CompanyBudgetVersionDeletions AFTER INSERT, UPDATE, DELETE AS
   BEGIN SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM deleted) THROW 66004,N''Deletion records are immutable.'',1;
    IF EXISTS(SELECT 1 FROM inserted i JOIN bms_web.CompanyBudgetActiveVersion a WITH(UPDLOCK,HOLDLOCK) ON a.VersionId=i.VersionId)
     THROW 66005,N''An active budget version requires a replacement before deletion.'',1;
   END;');
  EXEC(N'CREATE TRIGGER bms_web.TR_CompanyBudgetActiveVersion_NotDeleted ON bms_web.CompanyBudgetActiveVersion AFTER INSERT, UPDATE AS
   BEGIN SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted i JOIN bms_web.CompanyBudgetVersionDeletions d WITH(UPDLOCK,HOLDLOCK) ON d.VersionId=i.VersionId)
     THROW 66005,N''A deleted budget version cannot become active.'',1;
   END;');
  INSERT bms_web.schema_version(version_no,script_name) VALUES(60,N'060_company_budget_version_deletions.sql');
 END;
 IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.CompanyBudgetVersionDeletions'))<>4
 OR (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'bms_web.CompanyBudgetVersionDeletions') AND is_disabled=0 AND is_not_trusted=0 AND delete_referential_action=0)<>2
 OR (SELECT COUNT(*) FROM sys.triggers WHERE name IN(N'TR_CompanyBudgetVersionDeletions_Immutable',N'TR_CompanyBudgetActiveVersion_NotDeleted') AND is_disabled=0)<>2
  THROW 66006,N'Version deletion shape mismatch.',1;
 COMMIT;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
