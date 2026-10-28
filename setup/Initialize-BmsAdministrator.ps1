#requires -Version 7.0
[CmdletBinding()]
param(
 [Parameter(Mandatory)][ValidatePattern('^Bms_[A-Za-z0-9_]{1,80}$')][string]$Database,
 [Parameter(Mandatory)][guid]$InstallationId,
 [Parameter(Mandatory)][ValidatePattern('^[^\\\s]+\\[^\\\r\n]+$')][string]$Account,
 [Parameter(Mandatory)][ValidateLength(1,150)][string]$DisplayName,
 [Parameter(Mandatory)][ValidateLength(1,1000)][string]$Reason,
 [ValidateLength(1,20)][string]$EmployeeCode='BMS-FIRST-ADMIN',
 [string]$Server='lpc:.\BMSLOCAL',
 [switch]$TrustServerCertificate
)
# One-time installation bootstrap, run by a Windows/SQL administrator. This is
# not a business role editor and cannot grant rights in an established database.
$ErrorActionPreference='Stop'
if(![OperatingSystem]::IsWindows()){throw 'Windows is required.'}
if($InstallationId -eq [guid]::Empty){throw 'InstallationId is required.'}
foreach($value in @($Account,$DisplayName,$Reason,$EmployeeCode)){
 if($value -cne $value.Trim() -or $value.ToCharArray().Where({[char]::IsControl($_)}).Count -ne 0){throw 'Canonical single-line input is required.'}
}
$sid=([Security.Principal.NTAccount]::new($Account)).Translate([Security.Principal.SecurityIdentifier])
if(!$sid.IsAccountSid()){throw 'An individual Windows account SID is required.'}
$operator=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$builder=[Data.SqlClient.SqlConnectionStringBuilder]::new()
$builder['Data Source']=$Server;$builder['Initial Catalog']=$Database
$builder['Integrated Security']=$true;$builder['Encrypt']=$true
$builder['TrustServerCertificate']=[bool]$TrustServerCertificate
$builder['Pooling']=$false;$builder['Connect Timeout']=10
$builder['Application Name']='BMS first administrator setup'
$connection=[Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
try{
 $connection.Open();$command=$connection.CreateCommand();$command.CommandTimeout=30
 $command.CommandText=@'
SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
IF COALESCE(IS_SRVROLEMEMBER(N'sysadmin'),0)<>1 AND COALESCE(IS_MEMBER(N'db_owner'),0)<>1
 THROW 51010,N'DBA authority is required for first-administrator setup.',1;
IF DB_NAME() COLLATE Latin1_General_100_BIN2<>@database COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
  AND TRY_CONVERT(uniqueidentifier,value)=@installation)
 THROW 51011,N'Exact owned installation is required.',1;
IF (SELECT COUNT(*) FROM bms_web.schema_version)<>60 OR (SELECT MAX(version_no) FROM bms_web.schema_version)<>61
 THROW 51012,N'Installed schema 61 is required.',1;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @locked int;
 EXEC @locked=sys.sp_getapplock @Resource=N'BMS:FirstAdministrator:Bootstrap',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
 IF @locked<0 THROW 51013,N'First-administrator setup is busy.',1;
 DECLARE @existing uniqueidentifier=(SELECT TRY_CONVERT(uniqueidentifier,value) FROM sys.extended_properties WHERE class=0 AND name=N'BMS.FirstAdministratorId');
 IF @existing IS NOT NULL
 BEGIN
  IF NOT EXISTS(SELECT 1 FROM bms_web.Employees e
   JOIN bms_web.EmployeeActorMappings m ON m.EmployeeId=e.Id
   JOIN bms_web.EmployeeFunctionalRoles a ON a.EmployeeId=e.Id
   JOIN bms_web.FunctionalRoles r ON r.Id=a.FunctionalRoleId
   WHERE e.Id=@existing AND e.Code COLLATE Latin1_General_100_BIN2=@code COLLATE Latin1_General_100_BIN2
    AND e.Name=@name AND e.IsActive=1 AND m.ActorExternalId COLLATE Latin1_General_100_BIN2=@sid COLLATE Latin1_General_100_BIN2
    AND m.IsActive=1 AND a.IsActive=1 AND r.Code=N'BMS_Администратор' AND r.IsActive=1)
   THROW 51014,N'First administrator is already initialized with another identity or has changed.',1;
  COMMIT;
  SELECT N'ALREADY_INITIALIZED' AS Status,@existing AS EmployeeId;
  RETURN;
 END;
 IF EXISTS(SELECT 1 FROM bms_web.Employees WITH(UPDLOCK,HOLDLOCK))
  OR EXISTS(SELECT 1 FROM bms_web.EmployeeActorMappings WITH(UPDLOCK,HOLDLOCK))
  OR EXISTS(SELECT 1 FROM bms_web.EmployeeFunctionalRoles WITH(UPDLOCK,HOLDLOCK))
  -- SQL045 seeds the unassigned CFO_INITIATOR catalogue entry. Preserve it;
  -- it is not a grant and is the only role allowed before this bootstrap.
  OR EXISTS(SELECT 1 FROM bms_web.FunctionalRoles WITH(UPDLOCK,HOLDLOCK) WHERE Code<>N'CFO_INITIATOR' OR IsActive<>1)
  OR EXISTS(SELECT 1 FROM bms_web.ExpenseRequests)
  OR EXISTS(SELECT 1 FROM bms_web.Organizations)
  THROW 51015,N'Bootstrap requires a fresh installation with no employees, roles or business data.',1;
 DECLARE @employee uniqueidentifier=NEWID(),@role uniqueidentifier=NEWID();
 INSERT bms_web.Employees(Id,Code,Name,SourceDatabaseUserName,IsActive) VALUES(@employee,@code,@name,NULL,1);
 INSERT bms_web.FunctionalRoles(Id,Code,Name,IsActive) VALUES(@role,N'BMS_Администратор',N'Администратор BMS',1);
 INSERT bms_web.EmployeeFunctionalRoles(EmployeeId,FunctionalRoleId,IsActive) VALUES(@employee,@role,1);
 INSERT bms_web.EmployeeActorMappings(ActorExternalId,EmployeeId,IsActive) VALUES(@sid,@employee,1);
 DECLARE @marker nvarchar(36)=CONVERT(nvarchar(36),@employee);
 EXEC sys.sp_addextendedproperty @name=N'BMS.FirstAdministratorId',@value=@marker;
 DECLARE @after nvarchar(max)=(SELECT @employee AS employeeId,N'SYSTEM_ADMIN' AS assignedRole,@reason AS reason,N'WindowsSid' AS identityKind FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 INSERT bms_web.BusinessAudit(Id,OccurredAtUtc,ActorExternalId,ObjectType,ObjectId,Action,BeforeJson,AfterJson,CorrelationId)
 VALUES(NEWID(),SYSUTCDATETIME(),@operator,N'InstallationSetup',@marker,N'FirstAdministratorInitialized',NULL,@after,CONVERT(nvarchar(36),NEWID()));
 COMMIT;
 SELECT N'FIRST_ADMINISTRATOR_INITIALIZED' AS Status,@employee AS EmployeeId;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
'@
 foreach($p in @(
  @('database',[Data.SqlDbType]::NVarChar,128,$Database),
  @('installation',[Data.SqlDbType]::UniqueIdentifier,0,$InstallationId),
  @('sid',[Data.SqlDbType]::NVarChar,256,$sid.Value),
  @('operator',[Data.SqlDbType]::NVarChar,256,$operator),
  @('code',[Data.SqlDbType]::NVarChar,20,$EmployeeCode),
  @('name',[Data.SqlDbType]::NVarChar,150,$DisplayName),
  @('reason',[Data.SqlDbType]::NVarChar,1000,$Reason))){
  $parameter=$command.Parameters.Add('@'+$p[0],$p[1]);if($p[2] -gt 0){$parameter.Size=$p[2]};$parameter.Value=$p[3]
 }
 $reader=$command.ExecuteReader()
 try{
  if(!$reader.Read()){throw 'Bootstrap result is missing.'}
  [ordered]@{status=$reader.GetString(0);employeeId=$reader.GetGuid(1).ToString('D');role='SYSTEM_ADMIN';businessRolesGranted=0;database=$Database}|ConvertTo-Json
 }finally{$reader.Dispose()}
}finally{$connection.Dispose()}
