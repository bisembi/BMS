[CmdletBinding()]
param(
 [Parameter(Mandatory)][ValidatePattern('^Bms_[A-Za-z0-9_]{1,80}$')][string]$Database,
 [Parameter(Mandatory)][ValidateScript({Test-Path -LiteralPath $_ -PathType Container})][string]$DataDirectory,
 [ValidateRange(32,1048576)][int]$DataMaxSizeMb=512,
 [ValidateRange(16,1048576)][int]$LogMaxSizeMb=256,
 [string]$Server='lpc:.\BMSLOCAL',
 [switch]$TrustServerCertificate
)
# First installation only. No drop/restore and no automatic upgrade of an
# existing database. Use a dedicated SQL instance and a DBA-controlled path.
$ErrorActionPreference='Stop'
$schemaRoot=Join-Path (Split-Path $PSScriptRoot -Parent) 'database'
$manifest=Get-Content -LiteralPath (Join-Path $schemaRoot 'migration-manifest.json') -Raw|ConvertFrom-Json
if($manifest.scriptCount -ne 60 -or $manifest.lastVersion -ne 61){throw 'Unexpected schema manifest.'}
foreach($script in @($manifest.scripts)+@($manifest.installationExtensions)){
 $path=Join-Path $schemaRoot $script.fileName
 if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $script.sha256){throw "Migration hash mismatch: $($script.fileName)"}
}
$directory=(Resolve-Path -LiteralPath $DataDirectory).Path
$dataFile=Join-Path $directory ($Database+'.mdf')
$logFile=Join-Path $directory ($Database+'_log.ldf')
if((Test-Path -LiteralPath $dataFile) -or (Test-Path -LiteralPath $logFile)){throw 'Database files already exist. Nothing was overwritten.'}
$builder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$builder['Data Source']=$Server
$builder['Initial Catalog']='master'
$builder['Integrated Security']=$true
$builder['Encrypt']=$true
$builder['TrustServerCertificate']=[bool]$TrustServerCertificate
$builder['Pooling']=$false
$builder['Connect Timeout']=10
$builder['Application Name']='BMS first installation'
$connection=[System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
$installation=[guid]::NewGuid().ToString('D')
$completed=[Collections.Generic.List[string]]::new()
function Invoke-InstallSql([string]$Sql){
 $command=$connection.CreateCommand();$command.CommandTimeout=120;$command.CommandText=$Sql
 try{[void]$command.ExecuteNonQuery()}finally{$command.Dispose()}
}
function Invoke-InstallFile([string]$Path){
 foreach($batch in [regex]::Split([IO.File]::ReadAllText($Path),'(?im)^\s*GO\s*(?:--[^\r\n]*)?\r?$')){
  if(![string]::IsNullOrWhiteSpace($batch)){Invoke-InstallSql $batch}
 }
}
try{
 $connection.Open()
 $command=$connection.CreateCommand();$command.CommandText='SELECT DB_ID(@name)';[void]$command.Parameters.AddWithValue('@name',$Database)
 try{$exists=$command.ExecuteScalar()}finally{$command.Dispose()}
 if($exists -isnot [DBNull] -and $null -ne $exists){throw 'Database already exists. Installer refuses to modify it.'}
 $sqlData=$dataFile.Replace("'","''");$sqlLog=$logFile.Replace("'","''")
 Invoke-InstallSql "CREATE DATABASE [$Database] ON PRIMARY(NAME=N'BmsData',FILENAME=N'$sqlData',SIZE=32MB,MAXSIZE=$($DataMaxSizeMb)MB,FILEGROWTH=16MB) LOG ON(NAME=N'BmsLog',FILENAME=N'$sqlLog',SIZE=16MB,MAXSIZE=$($LogMaxSizeMb)MB,FILEGROWTH=16MB);"
 $connection.ChangeDatabase($Database)
 Invoke-InstallSql "EXEC sys.sp_addextendedproperty @name=N'BMS.PublicInstallationId',@value=N'$installation'; EXEC sys.sp_set_session_context @key=N'BMS.TargetDatabase',@value=N'$Database',@read_only=1; EXEC sys.sp_set_session_context @key=N'BMS.InstallationId',@value=N'$installation',@read_only=1; CREATE ROLE bms_current_runtime AUTHORIZATION dbo;"
 foreach($script in $manifest.scripts){
  Invoke-InstallFile (Join-Path $schemaRoot $script.fileName)
  $completed.Add($script.fileName)
 }
 Invoke-InstallSql 'BEGIN TRANSACTION;'
 try{
  foreach($script in $manifest.installationExtensions){
   Invoke-InstallFile (Join-Path $schemaRoot $script.fileName)
   $completed.Add($script.fileName)
  }
  Invoke-InstallSql 'COMMIT;'
 }catch{Invoke-InstallSql 'IF @@TRANCOUNT>0 ROLLBACK;';throw}
 # No login or corporate account is created. DBA assigns the dedicated runtime
 # principal after reviewing permissions. Never run the API as this installer.
 Invoke-InstallSql 'GRANT CONNECT TO bms_current_runtime; GRANT SELECT,INSERT,UPDATE,DELETE ON SCHEMA::bms_web TO bms_current_runtime; GRANT VIEW DEFINITION ON SCHEMA::bms_web TO bms_current_runtime; DENY INSERT,UPDATE,DELETE ON OBJECT::bms_web.schema_version TO bms_current_runtime;'
 $command=$connection.CreateCommand();$command.CommandText='SELECT COUNT(*) AS VersionCount,MAX(version_no) AS LastVersion FROM bms_web.schema_version;'
 try{$reader=$command.ExecuteReader();[void]$reader.Read();$versionCount=$reader.GetInt32(0);$lastVersion=$reader.GetInt32(1);$reader.Close()}finally{$command.Dispose()}
 if($versionCount -ne 60 -or $lastVersion -ne 61){throw 'Installed schema did not match manifest.'}
 [pscustomobject]@{status='CORE_SCHEMA_INSTALLED_NOT_CONFIGURED';database=$Database;installationId=$installation;versionCount=$versionCount;lastVersion=$lastVersion;completedScripts=$completed.ToArray();requires='Replica extensions, authentication, directories and runtime permissions must be configured before activation.'}|ConvertTo-Json -Depth 10
}catch{
 # Preserve the new owned database for inspection. Never clean up by dropping it.
 Write-Warning "Installation incomplete. Preserve owned database '$Database' for inspection; last successful scripts: $($completed.Count)."
 throw
}finally{$connection.Dispose()}
