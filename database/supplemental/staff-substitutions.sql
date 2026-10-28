-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
-- Additive, replay-safe feature storage. No existing financial rows are changed.
SET XACT_ABORT ON;
SET NOCOUNT ON;
IF DB_NAME() NOT IN (CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase')),N'BmsSubstitution471_Test') THROW 65471,'STAFF471_DATABASE_SCOPE',1;
IF SCHEMA_ID(N'bms_web') IS NULL THROW 65471,'STAFF471_BASE_SCHEMA_REQUIRED',1;
BEGIN TRANSACTION;
DECLARE @lock int;
EXEC @lock=sys.sp_getapplock @Resource=N'BMS:StaffSubstitution471:Schema',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
IF @lock<0 THROW 65471,'STAFF471_SCHEMA_BUSY',1;
IF OBJECT_ID(N'bms_web.StaffSubstitutions471',N'U') IS NULL
BEGIN
 CREATE TABLE bms_web.StaffSubstitutions471(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_StaffSubstitutions471 PRIMARY KEY,
  Number bigint IDENTITY(1,1) NOT NULL,
  PrincipalEmployeeId uniqueidentifier NOT NULL,
  DeputyEmployeeId uniqueidentifier NOT NULL,
  RoleCode nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL,
  [From] date NOT NULL, Through date NULL,
  ValidFromUtc datetimeoffset(7) NOT NULL, ValidBeforeUtc datetimeoffset(7) NULL,
  Status nvarchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
  Version int NOT NULL, CreatedAtUtc datetimeoffset(7) NOT NULL,
  UpdatedAtUtc datetimeoffset(7) NOT NULL, AcceptedAtUtc datetimeoffset(7) NULL,
  CONSTRAINT FK_Staff471_Principal FOREIGN KEY(PrincipalEmployeeId) REFERENCES bms_web.Employees(Id),
  CONSTRAINT FK_Staff471_Deputy FOREIGN KEY(DeputyEmployeeId) REFERENCES bms_web.Employees(Id),
  CONSTRAINT CK_Staff471_Different CHECK(PrincipalEmployeeId<>DeputyEmployeeId AND Id<>'00000000-0000-0000-0000-000000000000'),
  CONSTRAINT CK_Staff471_Period CHECK(YEAR([From]) BETWEEN 1900 AND 9998 AND (Through IS NULL OR Through>=[From] AND YEAR(Through)<=9998)
      AND (ValidBeforeUtc IS NULL OR ValidBeforeUtc>ValidFromUtc)),
  CONSTRAINT CK_Staff471_Role CHECK(RoleCode IN(N'ACCOUNTANT',N'AUDITOR',N'BUDGET_OWNER',N'CHIEF_ACCOUNTANT',N'CFO_INITIATOR',N'FIN_CONTROLLER',
      N'FIN_DIRECTOR',N'GENERAL_DIRECTOR',N'NSI_SPECIALIST',N'PAYROLL_CALCULATOR',N'RESPONSIBLE_ACCOUNTANT',N'TREASURER')
      AND DATALENGTH(RoleCode)=DATALENGTH(LTRIM(RTRIM(RoleCode)))),
  CONSTRAINT CK_Staff471_State CHECK(Status IN(N'DRAFT',N'PENDING',N'CONFIRMED',N'REJECTED',N'REVOKED')
      AND DATALENGTH(Status)=DATALENGTH(LTRIM(RTRIM(Status))) AND Version>0
      AND (Status<>N'CONFIRMED' OR AcceptedAtUtc IS NOT NULL))
 );
 CREATE UNIQUE INDEX IX_StaffSubstitutions471_Number ON bms_web.StaffSubstitutions471(Number);
 CREATE INDEX IX_StaffSubstitutions471_PrincipalEmployeeId_RoleCode_Status_ValidFromUtc ON bms_web.StaffSubstitutions471(PrincipalEmployeeId,RoleCode,Status,ValidFromUtc) INCLUDE(ValidBeforeUtc,DeputyEmployeeId);
 CREATE INDEX IX_StaffSubstitutions471_DeputyEmployeeId_Status_ValidFromUtc ON bms_web.StaffSubstitutions471(DeputyEmployeeId,Status,ValidFromUtc) INCLUDE(ValidBeforeUtc,PrincipalEmployeeId,RoleCode);
 EXEC sys.sp_addextendedproperty @name=N'BmsStaff471Version',@value=N'1',@level0type=N'SCHEMA',@level0name=N'bms_web',@level1type=N'TABLE',@level1name=N'StaffSubstitutions471';
END;
IF OBJECT_ID(N'bms_web.StaffSubstitutionCommands471',N'U') IS NULL
BEGIN
 CREATE TABLE bms_web.StaffSubstitutionCommands471(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_StaffSubstitutionCommands471 PRIMARY KEY,
  DocumentId uniqueidentifier NOT NULL,
  ActorExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
  Action nvarchar(20) NOT NULL, PayloadSHA nvarchar(64) NOT NULL,
  ResponseJson nvarchar(max) NOT NULL, AtUtc datetimeoffset(7) NOT NULL,
  CONSTRAINT FK_Staff471_CommandDoc FOREIGN KEY(DocumentId) REFERENCES bms_web.StaffSubstitutions471(Id),
  CONSTRAINT CK_Staff471_Command CHECK(Action IN(N'create',N'update',N'submit',N'confirm',N'reject',N'revoke')
      AND ISJSON(ResponseJson)=1 AND DATALENGTH(PayloadSHA)=128 AND DATALENGTH(ActorExternalId)>0)
 );
 CREATE INDEX IX_StaffSubstitutionCommands471_DocumentId_AtUtc ON bms_web.StaffSubstitutionCommands471(DocumentId,AtUtc);
 EXEC sys.sp_addextendedproperty @name=N'BmsStaff471Version',@value=N'1',@level0type=N'SCHEMA',@level0name=N'bms_web',@level1type=N'TABLE',@level1name=N'StaffSubstitutionCommands471';
END;
IF OBJECT_ID(N'bms_web.StaffSubstitutionActions471',N'U') IS NULL
BEGIN
 CREATE TABLE bms_web.StaffSubstitutionActions471(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_StaffSubstitutionActions471 PRIMARY KEY,
  DocumentId uniqueidentifier NOT NULL, PrincipalEmployeeId uniqueidentifier NOT NULL, DeputyEmployeeId uniqueidentifier NOT NULL,
  ActorExternalId nvarchar(256) NOT NULL, RoleCode nvarchar(100) NOT NULL,
  AtUtc datetimeoffset(7) NOT NULL, ObjectType nvarchar(100) NOT NULL,
  ObjectId nvarchar(100) NOT NULL, Action nvarchar(100) NOT NULL,
  CONSTRAINT FK_Staff471_ActionDoc FOREIGN KEY(DocumentId) REFERENCES bms_web.StaffSubstitutions471(Id),
  CONSTRAINT CK_Staff471_Action CHECK(PrincipalEmployeeId<>DeputyEmployeeId AND DATALENGTH(ActorExternalId)>0)
 );
 CREATE INDEX IX_StaffSubstitutionActions471_DocumentId_AtUtc ON bms_web.StaffSubstitutionActions471(DocumentId,AtUtc);
 EXEC sys.sp_addextendedproperty @name=N'BmsStaff471Version',@value=N'1',@level0type=N'SCHEMA',@level0name=N'bms_web',@level1type=N'TABLE',@level1name=N'StaffSubstitutionActions471';
END;
IF (SELECT COUNT(*) FROM sys.extended_properties WHERE name=N'BmsStaff471Version' AND class=1 AND CONVERT(nvarchar(20),value)=N'1'
 AND major_id IN(OBJECT_ID(N'bms_web.StaffSubstitutions471'),OBJECT_ID(N'bms_web.StaffSubstitutionCommands471'),OBJECT_ID(N'bms_web.StaffSubstitutionActions471')))<>3
 THROW 65471,'STAFF471_FOREIGN_SCHEMA',1;
COMMIT;
