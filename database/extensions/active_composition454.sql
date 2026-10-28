-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
-- Scoped extension; base schema 61 and all financial tables remain unchanged.
-- Installer runs as DBA after a pinned package/SQL backup. Runtime receives no
-- DML or ALTER rights on the permit schema. Never disable a production trigger.
SET XACT_ABORT ON;
IF (SELECT MAX(version_no) FROM bms_web.schema_version)<>61 THROW 65454,N'Base schema 61 required.',1;
IF SCHEMA_ID(N'bms_version454') IS NULL EXEC(N'CREATE SCHEMA bms_version454 AUTHORIZATION dbo');
IF OBJECT_ID(N'bms_version454.MutationPermit',N'U') IS NULL
 CREATE TABLE bms_version454.MutationPermit(SessionId int NOT NULL,TransactionId bigint NOT NULL,VersionId uniqueidentifier NOT NULL,
 CONSTRAINT PK_Version454Permit PRIMARY KEY(SessionId,TransactionId,VersionId),
 CONSTRAINT CK_Version454Permit CHECK(SessionId>0 AND TransactionId>0 AND VersionId<>'00000000-0000-0000-0000-000000000000'));
IF OBJECT_ID(N'bms_version454.Contract',N'U') IS NULL
 BEGIN CREATE TABLE bms_version454.Contract(Id int NOT NULL CONSTRAINT PK_Version454Contract PRIMARY KEY,
 ContractName nvarchar(80) COLLATE Latin1_General_100_BIN2 NOT NULL,
 CONSTRAINT CK_Version454Contract CHECK(Id=1 AND ContractName=N'BmsActiveComposition454/v1'));
 INSERT bms_version454.Contract VALUES(1,N'BmsActiveComposition454/v1'); END;
IF OBJECT_ID(N'bms_version454.CompositionHistory',N'U') IS NULL
 CREATE TABLE bms_version454.CompositionHistory(Id uniqueidentifier NOT NULL CONSTRAINT PK_Version454History PRIMARY KEY,
 VersionId uniqueidentifier NOT NULL,BeforeVersionStamp uniqueidentifier NOT NULL,AfterVersionStamp uniqueidentifier NOT NULL,
 BeforeActiveStamp uniqueidentifier NOT NULL,AfterActiveStamp uniqueidentifier NOT NULL,
 BeforeIdsJson nvarchar(max) NOT NULL,AfterIdsJson nvarchar(max) NOT NULL,
 OccurredAtUtc datetimeoffset(3) NOT NULL,EmployeeId uniqueidentifier NOT NULL,ActorExternalId nvarchar(256) NOT NULL,
 CONSTRAINT UQ_Version454History_Stamp UNIQUE(AfterActiveStamp),
 CONSTRAINT CK_Version454History_Json CHECK(ISJSON(BeforeIdsJson)=1 AND ISJSON(AfterIdsJson)=1),
 CONSTRAINT CK_Version454History_Utc CHECK(DATEPART(TZOFFSET,OccurredAtUtc)=0));
GO
CREATE OR ALTER TRIGGER bms_version454.TR_CompositionHistory_Immutable ON bms_version454.CompositionHistory AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted) THROW 65454,N'Composition history is immutable.',1;
END
GO
CREATE OR ALTER TRIGGER bms_web.TR_CompanyBudgetVersions_Immutable ON bms_web.CompanyBudgetVersions AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d WHERE d.PublishedAtUtc IS NOT NULL AND
   (NOT EXISTS(SELECT 1 FROM inserted i WHERE i.Id=d.Id)
    OR NOT EXISTS(SELECT 1 FROM bms_version454.MutationPermit p WHERE p.SessionId=@@SPID
      AND p.TransactionId=CURRENT_TRANSACTION_ID() AND p.VersionId=d.Id)
    OR NOT EXISTS(SELECT 1 FROM bms_web.CompanyBudgetActiveVersion a WHERE a.Id=1 AND a.VersionId=d.Id)))
  THROW 64811,N'Published company versions cannot be changed or deleted.',1;
 IF EXISTS(SELECT d.Id,d.Name,d.CreatedAtUtc,d.CreatedByEmployeeId,d.CreatedByExternalId,d.PublishedAtUtc
   FROM deleted d WHERE d.PublishedAtUtc IS NOT NULL
   EXCEPT SELECT i.Id,i.Name,i.CreatedAtUtc,i.CreatedByEmployeeId,i.CreatedByExternalId,i.PublishedAtUtc FROM inserted i)
  THROW 65454,N'Active composition cannot change published provenance.',1;
 IF EXISTS(SELECT 1 FROM inserted i WHERE i.PublishedAtUtc IS NOT NULL AND EXISTS
   (SELECT 1 FROM bms_web.CompanyBudgetVersionInstances m JOIN bms_web.BudgetInstances b ON b.Id=m.BudgetInstanceId
    WHERE m.VersionId=i.Id AND (b.Status COLLATE Latin1_General_100_BIN2<>N'Approved'
      OR b.ApprovalOrdinal IS NULL OR b.ApprovedAtUtc IS NULL)))
  THROW 64812,N'Only Approved documents can be published.',1;
 IF EXISTS(SELECT 1 FROM inserted i WHERE i.PublishedAtUtc IS NOT NULL AND
   i.PublishedInstanceCount<>(SELECT COUNT(*) FROM bms_web.CompanyBudgetVersionInstances m WHERE m.VersionId=i.Id))
  THROW 64821,N'Published member count is inconsistent.',1;
END
GO
CREATE OR ALTER TRIGGER bms_web.TR_CompanyBudgetMembers_Immutable ON bms_web.CompanyBudgetVersionInstances AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM (SELECT VersionId FROM inserted UNION SELECT VersionId FROM deleted) x
   JOIN bms_web.CompanyBudgetVersions v ON v.Id=x.VersionId WHERE v.PublishedAtUtc IS NOT NULL AND
   (NOT EXISTS(SELECT 1 FROM bms_version454.MutationPermit p WHERE p.SessionId=@@SPID
      AND p.TransactionId=CURRENT_TRANSACTION_ID() AND p.VersionId=v.Id)
    OR NOT EXISTS(SELECT 1 FROM bms_web.CompanyBudgetActiveVersion a WHERE a.Id=1 AND a.VersionId=v.Id)))
  THROW 64813,N'Published company version membership is immutable.',1;
END
GO
CREATE OR ALTER PROCEDURE bms_version454.UpdateActiveComposition
 @VersionId uniqueidentifier,@ExpectedVersionStamp uniqueidentifier,@ExpectedActiveStamp uniqueidentifier,
 @BudgetInstanceIds nvarchar(max),@EmployeeId uniqueidentifier,@ActorExternalId nvarchar(256),@AtUtc datetimeoffset(3)
WITH EXECUTE AS OWNER
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @@TRANCOUNT<>1 OR XACT_STATE()<>1 OR
    (SELECT transaction_isolation_level FROM sys.dm_exec_sessions WHERE session_id=@@SPID)<>4 OR
    ISNULL(APPLOCK_MODE(N'public',N'bms-web:company-budget-plan',N'Transaction'),N'NoLock')<>N'Exclusive'
  THROW 65454,N'Exclusive Serializable company-budget transaction required.',1;
 IF @VersionId IS NULL OR @ExpectedVersionStamp IS NULL OR @ExpectedActiveStamp IS NULL OR @EmployeeId IS NULL
   OR @VersionId='00000000-0000-0000-0000-000000000000' OR @ExpectedVersionStamp='00000000-0000-0000-0000-000000000000'
   OR @ExpectedActiveStamp='00000000-0000-0000-0000-000000000000' OR @AtUtc IS NULL OR DATEPART(TZOFFSET,@AtUtc)<>0
   OR NULLIF(LTRIM(RTRIM(@ActorExternalId)),N'') IS NULL OR @BudgetInstanceIds IS NULL OR ISJSON(@BudgetInstanceIds,ARRAY)<>1
   OR DATALENGTH(@BudgetInstanceIds)>100000
  THROW 65455,N'Invalid active composition command.',1;
 IF (SELECT COUNT_BIG(*) FROM bms_web.EmployeeActorMappings m WHERE m.ActorExternalId COLLATE Latin1_General_100_BIN2=@ActorExternalId COLLATE Latin1_General_100_BIN2
     AND DATALENGTH(m.ActorExternalId)=DATALENGTH(@ActorExternalId))<>1
   OR NOT EXISTS(SELECT 1 FROM bms_web.EmployeeActorMappings m JOIN bms_web.Employees e ON e.Id=m.EmployeeId
     WHERE m.ActorExternalId COLLATE Latin1_General_100_BIN2=@ActorExternalId COLLATE Latin1_General_100_BIN2
       AND DATALENGTH(m.ActorExternalId)=DATALENGTH(@ActorExternalId) AND m.EmployeeId=@EmployeeId AND m.IsActive=1 AND e.IsActive=1)
   OR NOT EXISTS(SELECT 1 FROM bms_web.EmployeeFunctionalRoles a JOIN bms_web.FunctionalRoles r ON r.Id=a.FunctionalRoleId
     WHERE a.EmployeeId=@EmployeeId AND a.IsActive=1 AND r.IsActive=1 AND r.Code COLLATE Latin1_General_100_BIN2=N'FIN_DIRECTOR'
       AND DATALENGTH(r.Code)=DATALENGTH(N'FIN_DIRECTOR'))
  THROW 65456,N'Current financial director authority required.',1;
 IF NOT EXISTS(SELECT 1 FROM bms_web.CompanyBudgetVersions v WITH(UPDLOCK,HOLDLOCK)
   JOIN bms_web.CompanyBudgetActiveVersion a WITH(UPDLOCK,HOLDLOCK) ON a.VersionId=v.Id AND a.Id=1
   WHERE v.Id=@VersionId AND v.ConcurrencyStamp=@ExpectedVersionStamp AND a.ConcurrencyStamp=@ExpectedActiveStamp
     AND v.PublishedAtUtc IS NOT NULL AND NOT EXISTS(SELECT 1 FROM bms_web.CompanyBudgetVersionDeletions d WHERE d.VersionId=v.Id))
  THROW 65457,N'Active composition has changed.',1;
 DECLARE @Ids TABLE(Id uniqueidentifier NOT NULL PRIMARY KEY);
 IF EXISTS(SELECT 1 FROM OPENJSON(@BudgetInstanceIds) WHERE type<>1 OR DATALENGTH(value)<>72
   OR TRY_CONVERT(uniqueidentifier,value) IS NULL OR TRY_CONVERT(uniqueidentifier,value)='00000000-0000-0000-0000-000000000000')
  OR (SELECT COUNT(*) FROM OPENJSON(@BudgetInstanceIds)) NOT BETWEEN 1 AND 1000
  OR (SELECT COUNT(*) FROM OPENJSON(@BudgetInstanceIds))<>(SELECT COUNT(DISTINCT TRY_CONVERT(uniqueidentifier,value)) FROM OPENJSON(@BudgetInstanceIds))
  THROW 65455,N'One to 1000 unique budget documents required.',1;
 INSERT @Ids SELECT CONVERT(uniqueidentifier,value) FROM OPENJSON(@BudgetInstanceIds);
 IF (SELECT COUNT(*) FROM @Ids)<>(SELECT COUNT(*) FROM @Ids i JOIN bms_web.BudgetInstances b WITH(HOLDLOCK) ON b.Id=i.Id
   WHERE b.Status COLLATE Latin1_General_100_BIN2=N'Approved' AND b.ApprovalOrdinal IS NOT NULL AND b.ApprovedAtUtc IS NOT NULL)
  THROW 65455,N'Only approved existing budget documents are allowed.',1;
 IF EXISTS(SELECT 1 FROM bms_version454.MutationPermit WHERE SessionId=@@SPID AND TransactionId=CURRENT_TRANSACTION_ID())
  THROW 65454,N'Nested composition mutation refused.',1;
 DECLARE @BeforeJson nvarchar(max)=(SELECT BudgetInstanceId FROM bms_web.CompanyBudgetVersionInstances
   WHERE VersionId=@VersionId ORDER BY BudgetInstanceId FOR JSON PATH);
 DECLARE @AfterJson nvarchar(max)=(SELECT Id AS BudgetInstanceId FROM @Ids ORDER BY Id FOR JSON PATH);
 BEGIN TRY
  INSERT bms_version454.MutationPermit VALUES(@@SPID,CURRENT_TRANSACTION_ID(),@VersionId);
  DELETE m FROM bms_web.CompanyBudgetVersionInstances m WHERE m.VersionId=@VersionId AND NOT EXISTS(SELECT 1 FROM @Ids i WHERE i.Id=m.BudgetInstanceId);
  INSERT bms_web.CompanyBudgetVersionInstances(VersionId,BudgetInstanceId)
   SELECT @VersionId,i.Id FROM @Ids i WHERE NOT EXISTS(SELECT 1 FROM bms_web.CompanyBudgetVersionInstances m WHERE m.VersionId=@VersionId AND m.BudgetInstanceId=i.Id);
  DECLARE @NewVersionStamp uniqueidentifier=NEWID();
  UPDATE bms_web.CompanyBudgetVersions SET PublishedInstanceCount=(SELECT COUNT(*) FROM @Ids),ConcurrencyStamp=@NewVersionStamp WHERE Id=@VersionId;
  DECLARE @NewActiveStamp uniqueidentifier=NEWID();
  UPDATE bms_web.CompanyBudgetActiveVersion SET ConcurrencyStamp=@NewActiveStamp,ActivatedAtUtc=@AtUtc,
    ActivatedByEmployeeId=@EmployeeId,ActivatedByExternalId=@ActorExternalId WHERE Id=1 AND VersionId=@VersionId;
  INSERT bms_web.CompanyBudgetActivations(Id,VersionId,PreviousVersionId,ActiveConcurrencyStamp,ActivatedAtUtc,EmployeeId,ActorExternalId)
   VALUES(NEWID(),@VersionId,@VersionId,@NewActiveStamp,@AtUtc,@EmployeeId,@ActorExternalId);
  INSERT bms_version454.CompositionHistory(Id,VersionId,BeforeVersionStamp,AfterVersionStamp,BeforeActiveStamp,AfterActiveStamp,
    BeforeIdsJson,AfterIdsJson,OccurredAtUtc,EmployeeId,ActorExternalId)
   VALUES(NEWID(),@VersionId,@ExpectedVersionStamp,@NewVersionStamp,@ExpectedActiveStamp,@NewActiveStamp,
    @BeforeJson,@AfterJson,@AtUtc,@EmployeeId,@ActorExternalId);
  DELETE bms_version454.MutationPermit WHERE SessionId=@@SPID AND TransactionId=CURRENT_TRANSACTION_ID() AND VersionId=@VersionId;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()=1 DELETE bms_version454.MutationPermit WHERE SessionId=@@SPID AND TransactionId=CURRENT_TRANSACTION_ID() AND VersionId=@VersionId;
  THROW;
 END CATCH
END
GO
-- The role is the already restricted application runtime, never PUBLIC.
IF DATABASE_PRINCIPAL_ID(N'bms_current_runtime') IS NULL THROW 65454,N'Runtime role required.',1;
GRANT VIEW DEFINITION ON SCHEMA::bms_version454 TO bms_current_runtime;
GRANT SELECT ON SCHEMA::bms_version454 TO bms_current_runtime;
DENY INSERT,UPDATE,DELETE,ALTER ON SCHEMA::bms_version454 TO bms_current_runtime;
GRANT EXECUTE ON OBJECT::bms_version454.UpdateActiveComposition TO bms_current_runtime;
