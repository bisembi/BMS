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
IF DB_NAME()<>CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase')) THROW 65401,N'the explicitly owned BMS database required.',1;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lock int;
 EXEC @lock=sys.sp_getapplock @Resource=N'BMS:SQL054:PrivateAttachments',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
 IF @lock<0 THROW 65402,N'SQL054 lock unavailable.',1;
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=53 AND script_name=N'053_expense_request_number_leases.sql')
  THROW 65403,N'Schema53 predecessor required.',1;
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=54)
 BEGIN
  IF (SELECT COUNT(*) FROM bms_web.schema_version)<>52 OR OBJECT_ID(N'bms_web.AttachmentFiles') IS NOT NULL
   OR OBJECT_ID(N'bms_web.AttachmentUploadSessions') IS NOT NULL THROW 65404,N'Unexpected predecessor.',1;
  CREATE TABLE bms_web.AttachmentUploadSessions (
   Id uniqueidentifier NOT NULL CONSTRAINT PK_AttachmentUploadSessions PRIMARY KEY,
   ActorExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
   CreatedAtUtc datetimeoffset(7) NOT NULL,
   BoundRequestId uniqueidentifier NULL,
   CONSTRAINT FK_AttachmentUploadSessions_ExpenseRequests_BoundRequestId FOREIGN KEY(BoundRequestId) REFERENCES bms_web.ExpenseRequests(Id),
   CONSTRAINT CK_AttachmentUploadSessions_Identity CHECK(Id<>'00000000-0000-0000-0000-000000000000' AND LEN(ActorExternalId)>0));
  CREATE TABLE bms_web.AttachmentFiles (
   Id uniqueidentifier NOT NULL CONSTRAINT PK_AttachmentFiles PRIMARY KEY,
   SessionId uniqueidentifier NULL,
   ExpenseRequestId uniqueidentifier NULL,
   StorageKey nvarchar(101) COLLATE Latin1_General_100_BIN2 NOT NULL,
   StorageRoot nvarchar(512) COLLATE Latin1_General_100_BIN2 NOT NULL,
   Kind nvarchar(30) COLLATE Latin1_General_100_BIN2 NOT NULL,
   FileName nvarchar(255) COLLATE Latin1_General_100_BIN2 NOT NULL,
   FileSizeBytes bigint NOT NULL,
   ContentSha256 binary(32) NOT NULL,
   UploadedAtUtc datetimeoffset(7) NOT NULL,
   UploadedByExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
   IsWithdrawn bit NOT NULL,
   CONSTRAINT FK_AttachmentFiles_AttachmentUploadSessions_SessionId FOREIGN KEY(SessionId) REFERENCES bms_web.AttachmentUploadSessions(Id),
   CONSTRAINT FK_AttachmentFiles_ExpenseRequests_ExpenseRequestId FOREIGN KEY(ExpenseRequestId) REFERENCES bms_web.ExpenseRequests(Id),
   CONSTRAINT CK_AttachmentFiles_Context CHECK(SessionId IS NOT NULL OR ExpenseRequestId IS NOT NULL),
   CONSTRAINT CK_AttachmentFiles_Content CHECK(FileSizeBytes BETWEEN 1 AND 26214400 AND LEN(FileName)>0
    AND StorageKey=LOWER(REPLACE(CONVERT(nvarchar(36),Id),N'-',N'')+N'.'+CONVERT(nvarchar(64),ContentSha256,2)+N'.bin') AND LEN(UploadedByExternalId)>0 AND LEN(StorageRoot)>3
    AND Kind IN(N'SUPPORTING',N'NEW_CONTRACT',N'ADDITIONAL')));
  CREATE UNIQUE INDEX IX_AttachmentFiles_StorageKey ON bms_web.AttachmentFiles(StorageKey);
  CREATE INDEX IX_AttachmentFiles_SessionId ON bms_web.AttachmentFiles(SessionId);
  CREATE INDEX IX_AttachmentFiles_ExpenseRequestId ON bms_web.AttachmentFiles(ExpenseRequestId);
  INSERT bms_web.schema_version(version_no,script_name) VALUES(54,N'054_private_attachment_files.sql');
 END;
 IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.AttachmentUploadSessions'))<>4
  OR (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.AttachmentFiles'))<>12
  OR NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=54 AND script_name COLLATE Latin1_General_100_BIN2=N'054_private_attachment_files.sql')
  OR EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id IN(OBJECT_ID(N'bms_web.AttachmentFiles'),OBJECT_ID(N'bms_web.AttachmentUploadSessions')) AND (is_disabled=1 OR is_not_trusted=1))
  THROW 65405,N'SQL054 shape differs.',1;
 COMMIT;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
