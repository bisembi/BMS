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
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
IF DB_NAME()<>CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase')) THROW 65501,N'the explicitly owned BMS database required.',1;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lock int;
 EXEC @lock=sys.sp_getapplock @Resource=N'BMS:SQL055:DirectorOwnerRoute',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
 IF @lock<0 THROW 65502,N'SQL055 lock unavailable.',1;
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=54
    AND script_name COLLATE Latin1_General_100_BIN2=N'054_private_attachment_files.sql')
    OR EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no NOT BETWEEN 1 AND 43 AND version_no NOT IN(45,46,47,48,49,50,51,52,53,54,55))
    OR (SELECT COUNT(*) FROM bms_web.WorkflowPermissions WHERE DefinitionVersion=1)<>26
    OR (SELECT COUNT(*) FROM bms_web.WorkflowPermissions WHERE DefinitionVersion=2)<>30
  THROW 65503,N'Exact supported predecessor required.',1;
 DECLARE @expected TABLE(DefinitionVersion int,StatusCode nvarchar(100),StageNumber int,RoleCode nvarchar(100),
  CommandCode nvarchar(100),TargetStatusCode nvarchar(100),TargetStageNumber int,ConditionCode nvarchar(100),IsVisible bit,IsEnabled bit,IsProvisional bit);
 INSERT @expected VALUES
 (3,N'DRAFT',0,N'Инициатор',N'CANCEL',N'CANCELLED',0,N'',1,1,0),
 (3,N'RETURNED',0,N'Инициатор',N'CANCEL',N'CANCELLED',0,N'',1,1,0),
 (3,N'AWAITING_NSI',0,N'NSI_SPECIALIST',N'CONFIRM_NSI',N'TO_PAY',0,N'NSI_CANDIDATE_LINKED',1,1,0),
 (3,N'AWAITING_NSI',0,N'Инициатор',N'CANCEL',N'CANCELLED',0,N'',1,1,0),
 (3,N'APPROVED',0,N'TREASURER',N'SEND_TO_PAYMENT',N'TO_PAY',0,N'',1,1,0),
 (3,N'APPROVED',0,N'Инициатор',N'CANCEL',N'CANCELLED',0,N'',1,1,0),
 (3,N'TO_PAY',0,N'Инициатор',N'CANCEL',N'CANCELLED',0,N'',1,1,0),
 (3,N'DRAFT',0,N'Инициатор',N'SUBMIT',N'IN_APPROVAL',1,N'SENIOR_GD_REQUIRED',1,1,0),
 (3,N'DRAFT',0,N'Инициатор',N'SUBMIT',N'AWAITING_NSI',0,N'SENIOR_NSI_REQUIRED',1,1,0),
 (3,N'DRAFT',0,N'Инициатор',N'SUBMIT',N'TO_PAY',0,N'SENIOR_READY_FOR_PAYMENT',1,1,0),
 (3,N'RETURNED',0,N'Инициатор',N'SUBMIT',N'IN_APPROVAL',1,N'SENIOR_GD_REQUIRED',1,1,0),
 (3,N'RETURNED',0,N'Инициатор',N'SUBMIT',N'AWAITING_NSI',0,N'SENIOR_NSI_REQUIRED',1,1,0),
 (3,N'RETURNED',0,N'Инициатор',N'SUBMIT',N'TO_PAY',0,N'SENIOR_READY_FOR_PAYMENT',1,1,0),
 (3,N'IN_APPROVAL',1,N'GENERAL_DIRECTOR',N'APPROVE',N'TO_PAY',0,N'ROUTE_LAST_NO_NSI',1,1,0),
 (3,N'IN_APPROVAL',1,N'GENERAL_DIRECTOR',N'APPROVE',N'AWAITING_NSI',0,N'ROUTE_LAST_REQUIRES_NSI',1,1,0),
 (3,N'IN_APPROVAL',1,N'GENERAL_DIRECTOR',N'RETURN',N'RETURNED',0,N'',1,1,0),
 (3,N'IN_APPROVAL',1,N'GENERAL_DIRECTOR',N'REJECT',N'REJECTED',0,N'',1,1,0),
 (3,N'IN_APPROVAL',1,N'Инициатор',N'CANCEL',N'CANCELLED',0,N'',1,1,0);
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=55)
 BEGIN
  IF (SELECT COUNT(*) FROM bms_web.schema_version)<>53
    OR EXISTS(SELECT 1 FROM bms_web.WorkflowPermissions WHERE DefinitionVersion NOT IN(1,2))
   THROW 65504,N'Unregistered route or unexpected predecessor.',1;
  INSERT bms_web.WorkflowPermissions
   (DefinitionVersion,StatusCode,StageNumber,RoleCode,CommandCode,TargetStatusCode,TargetStageNumber,ConditionCode,IsVisible,IsEnabled,IsProvisional)
   SELECT * FROM @expected;
  INSERT bms_web.schema_version(version_no,script_name) VALUES(55,N'055_director_owner_expense_route.sql');
 END;
 IF (SELECT COUNT(*) FROM bms_web.schema_version)<>54
   OR (SELECT COUNT(*) FROM bms_web.WorkflowPermissions)<>74
   OR NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=55
     AND script_name COLLATE Latin1_General_100_BIN2=N'055_director_owner_expense_route.sql'
     AND DATALENGTH(script_name)=DATALENGTH(N'055_director_owner_expense_route.sql'))
   OR EXISTS(SELECT * FROM @expected EXCEPT SELECT DefinitionVersion,StatusCode,StageNumber,RoleCode,CommandCode,
     TargetStatusCode,TargetStageNumber,ConditionCode,IsVisible,IsEnabled,IsProvisional FROM bms_web.WorkflowPermissions WHERE DefinitionVersion=3)
   OR EXISTS(SELECT DefinitionVersion,StatusCode,StageNumber,RoleCode,CommandCode,TargetStatusCode,TargetStageNumber,
     ConditionCode,IsVisible,IsEnabled,IsProvisional FROM bms_web.WorkflowPermissions WHERE DefinitionVersion=3 EXCEPT SELECT * FROM @expected)
  THROW 65505,N'SQL055 route differs from the reviewed matrix.',1;
 COMMIT;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
