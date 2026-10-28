SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME()<>N'DataBI' THROW 65501,N'DataBI required.',1;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lock int;
 EXEC @lock=sys.sp_getapplock @Resource=N'BMS:SQL055:UsageLog',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
 IF @lock<0 THROW 65502,N'SQL055 lock unavailable.',1;
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=54 AND script_name=N'054_private_attachment_files.sql')
  THROW 65503,N'Schema54 predecessor required.',1;
 -- Independent, additive telemetry module. Keep the financial schema registry
 -- unchanged so the protected predecessor application remains a valid rollback.
 IF OBJECT_ID(N'bms_web.UsageLogSchema') IS NULL
 BEGIN
  IF (SELECT COUNT(*) FROM bms_web.schema_version)<>53 OR OBJECT_ID(N'bms_web.UsageSessions') IS NOT NULL
   OR OBJECT_ID(N'bms_web.UsageTabs') IS NOT NULL THROW 65504,N'Unexpected predecessor.',1;
  CREATE TABLE bms_web.UsageSessions (
   Id uniqueidentifier NOT NULL CONSTRAINT PK_UsageSessions PRIMARY KEY,
   ActorExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
   UserName nvarchar(512) NOT NULL,
   EnteredAtUtc datetimeoffset(7) NOT NULL,
   LastSeenAtUtc datetimeoffset(7) NOT NULL,
   ExitedAtUtc datetimeoffset(7) NULL,
   EndReason varchar(16) NULL,
   CONSTRAINT CK_UsageSessions_Interval CHECK(LEN(ActorExternalId)>0 AND LEN(UserName)>0
    AND LastSeenAtUtc>=EnteredAtUtc AND (ExitedAtUtc IS NULL OR ExitedAtUtc>=LastSeenAtUtc)
    AND ((ExitedAtUtc IS NULL AND EndReason IS NULL) OR (ExitedAtUtc IS NOT NULL AND EndReason IS NOT NULL AND EndReason IN('EXIT','TIMEOUT')))));
  CREATE UNIQUE INDEX IX_UsageSessions_OpenActor ON bms_web.UsageSessions(ActorExternalId) WHERE ExitedAtUtc IS NULL;
  CREATE INDEX IX_UsageSessions_EnteredAt ON bms_web.UsageSessions(EnteredAtUtc DESC,Id) INCLUDE(UserName,LastSeenAtUtc,ExitedAtUtc,EndReason);
  CREATE TABLE bms_web.UsageTabs (
   ActorExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
   TabId uniqueidentifier NOT NULL,
   SessionId uniqueidentifier NULL,
   Sequence bigint NOT NULL,
   LastSeenAtUtc datetimeoffset(7) NOT NULL,
   IsClosed bit NOT NULL,
   CONSTRAINT PK_UsageTabs PRIMARY KEY(ActorExternalId,TabId),
   CONSTRAINT FK_UsageTabs_Session FOREIGN KEY(SessionId) REFERENCES bms_web.UsageSessions(Id),
   CONSTRAINT CK_UsageTabs_Sequence CHECK(Sequence BETWEEN 1 AND 9007199254740991 AND LEN(ActorExternalId)>0
    AND TabId<>'00000000-0000-0000-0000-000000000000'));
  CREATE INDEX IX_UsageTabs_Session ON bms_web.UsageTabs(SessionId,IsClosed,LastSeenAtUtc);
  CREATE TABLE bms_web.UsageLogSchema (
   Version int NOT NULL CONSTRAINT PK_UsageLogSchema PRIMARY KEY,
   CONSTRAINT CK_UsageLogSchema_Version CHECK(Version=1));
  INSERT bms_web.UsageLogSchema(Version) VALUES(1);
 END;
 IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.UsageSessions'))<>7
  OR (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.UsageTabs'))<>6
  OR (SELECT COUNT(*) FROM bms_web.UsageLogSchema)<>1
  OR NOT EXISTS(SELECT 1 FROM bms_web.UsageLogSchema WHERE Version=1)
  OR EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'bms_web.UsageTabs') AND (is_disabled=1 OR is_not_trusted=1))
  THROW 65505,N'SQL055 shape differs.',1;
 COMMIT;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
