[CmdletBinding()]
param([ValidateSet('DryRun','Run','ManualResume')][string]$Mode='DryRun')
Set-StrictMode -Version Latest;$ErrorActionPreference='Stop'
$runner425Root='D:\BMS\artifacts\bms-weekly-primary425-20261025\runner425'
$release425='D:\BMS\releases\catalog485-20261028'
$runtime425='D:\BMS\runtimes\attachments-10.0.13-20261014-220819'
$connection425=$null;$process425=$null;$complete425=$null;$config425=$null;$sql425=$false;$ad425=$false;$read425Errors=0
$run425Id=[Guid]::NewGuid().ToString('N');$run425Started=[DateTimeOffset]::UtcNow
$coverage425=$null;$coverage425Blocked=$false
function Assert-Private425([string]$Path,[bool]$Protected=$false){
 $item=Get-Item -LiteralPath $Path;$node=$item
 while($node){if($node.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'N425_REPARSE'};$node=if($node -is [IO.FileInfo]){$node.Directory}else{$node.Parent}}
 $acl=Get-Acl -LiteralPath $Path;$rules=@($acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]));$owner=$acl.GetOwner([Security.Principal.SecurityIdentifier]).Value
 if($owner -cnotin @('S-1-5-18','S-1-5-32-544') -or $rules.Count -ne 2 -or ($Protected -and !$acl.AreAccessRulesProtected)){throw 'N425_PRIVATE_ACL_REQUIRED'}
 foreach($r in $rules){if($r.AccessControlType -ne 'Allow' -or $r.IdentityReference.Value -cnotin @('S-1-5-18','S-1-5-32-544') -or ($r.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -ne [Security.AccessControl.FileSystemRights]::FullControl){throw 'N425_PRIVATE_ACL_SCOPE'}}
 $node=if($item -is [IO.FileInfo]){$item.Directory}else{$item.Parent}
 $rights=[Security.AccessControl.FileSystemRights]::Delete -bor [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor [Security.AccessControl.FileSystemRights]::ChangePermissions -bor [Security.AccessControl.FileSystemRights]::TakeOwnership
 while($node){foreach($r in (Get-Acl -LiteralPath $node.FullName).GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])){if($r.AccessControlType -eq 'Allow' -and ($r.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -eq 0 -and $r.IdentityReference.Value -cnotin @('S-1-5-18','S-1-5-32-544') -and ($r.FileSystemRights -band $rights) -ne 0){throw 'N425_ANCESTOR_REPLACEMENT_RIGHTS'}};$node=$node.Parent}
}
function Assert-ActiveRuntime425 {
 if($env:COMPUTERNAME -cne 'BMS' -or (Get-TimeZone).Id -cne 'Qyzylorda Standard Time' -or (Get-TimeZone).BaseUtcOffset -ne [TimeSpan]::FromHours(5)){throw 'N425_MACHINE_TIMEZONE_DRIFT'}
 $svc=Get-CimInstance Win32_Service -Filter "Name='BmsWebCurrent'"
 if($svc.State -cne 'Running' -or $svc.ProcessId -le 0 -or $svc.StartName -cne 'NT SERVICE\BmsWebCurrent' -or $svc.PathName -cne ($release425+'\service\Bms.Api.exe')){throw 'N425_SERVICE_DRIFT'}
 if((Get-FileHash -LiteralPath ($release425+'\manifest.json')).Hash -cne '1943688D3A6E6C33C42CC186862633D2FA0A349CF373A3266D3CA8CB02B22724'){throw 'N425_RELEASE_DRIFT'}
 $releaseManifest=Get-Content -LiteralPath ($release425+'\manifest.json') -Raw -Encoding UTF8|ConvertFrom-Json
 $modules=@((Get-Process -Id $svc.ProcessId).Modules)
 foreach($name in @('Bms.Api.dll','Bms.Application.dll','Bms.Domain.dll','Bms.Infrastructure.dll')){$m=@($modules|Where-Object ModuleName -CEQ $name);$f=@($releaseManifest.Files|Where-Object {$_.Path.Replace('\','/') -CEQ ('service/'+$name)});if($m.Count -ne 1 -or $f.Count -ne 1 -or $m[0].FileName -cne ($release425+'\service\'+$name) -or (Get-FileHash -LiteralPath $m[0].FileName).Hash -cne $f[0].Sha256){throw 'N425_MODULE_DRIFT'}}
 $core=@($modules|Where-Object ModuleName -CEQ 'coreclr.dll')
 if($core.Count -ne 1 -or $core[0].FileName -cne ($runtime425+'\shared\Microsoft.NETCore.App\10.0.13\coreclr.dll') -or (Get-FileHash -LiteralPath ($runtime425+'\dotnet.exe')).Hash -cne 'ABCE6623A6729059CF39BB0F728C4A7804F625895099C88C17E6E704661E09EB'){throw 'N425_DOTNET_DRIFT'}
 return [int]$svc.ProcessId
}
function Test-Window425 {
 $local=[DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(5))
 return $local.DayOfWeek -eq [DayOfWeek]::Monday -and $local.Hour -ge 9 -and $local.Hour -lt 18
}
function Write-Child425([object]$Value){
 $json=$Value|ConvertTo-Json -Depth 32 -Compress
 $bytes=[Text.UTF8Encoding]::new($false,$true).GetBytes($json+"`n")
 if($bytes.Length -gt 16777216){throw 'N425_INPUT_BOUND'}
 $process425.StandardInput.BaseStream.Write($bytes,0,$bytes.Length);$process425.StandardInput.BaseStream.Flush();[Array]::Clear($bytes,0,$bytes.Length)
}
function Write-Receipt425([object]$Value){
 $runs=$runner425Root+'\state\runs';if(!(Test-Path -LiteralPath $runs)){[void][IO.Directory]::CreateDirectory($runs)};Assert-Private425 $runs
 $receiptId=[Guid]::NewGuid().ToString('N');$path=Join-Path $runs ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+$run425Id+'-'+$receiptId+'.json')
 $bytes=[Text.UTF8Encoding]::new($false,$true).GetBytes(($Value|ConvertTo-Json -Depth 16))
 $f=[IO.FileStream]::new($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None);try{$f.Write($bytes,0,$bytes.Length);$f.Flush($true)}finally{$f.Dispose()}
 if($Mode -ceq 'DryRun'){$latest=Join-Path $runs 'latest-dry-run.json';$temp=Join-Path $runs ('latest-'+$run425Id+'-'+$receiptId+'.tmp');[IO.File]::WriteAllBytes($temp,$bytes);if(Test-Path -LiteralPath $latest){[IO.File]::Replace($temp,$latest,[NullString]::Value)}else{[IO.File]::Move($temp,$latest)}}
 return $path
}
try {
 if([IO.Path]::GetFullPath($PSScriptRoot) -cne $runner425Root){throw 'N425_OWNED_PATH_REQUIRED'}
 Assert-Private425 $runner425Root $true;Assert-Private425 (Split-Path -Parent $runner425Root) $true
 $configPath=$runner425Root+'\runner-config.json';Assert-Private425 $configPath
 if((Get-Item -LiteralPath $configPath).Length -gt 16384){throw 'N425_CONFIG_BOUND'}
 $config425=Get-Content -LiteralPath $configPath -Raw -Encoding UTF8|ConvertFrom-Json
 if($config425.schemaVersion -ne 1 -or $config425.approval -cne 'BMS425_USER_APPROVED_WEEKLY_CFO_PRIMARY_REMINDERS_MON0900_UTC5'){throw 'N425_CONFIG_APPROVAL'}
 $manifestPath=$runner425Root+'\package-manifest425.json';Assert-Private425 $manifestPath
 if((Get-FileHash -LiteralPath $manifestPath).Hash -cne $config425.packageManifestSha256){throw 'N425_PACKAGE_MANIFEST_DRIFT'}
 $manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8|ConvertFrom-Json
 if($manifest.Files.Count -lt 38 -or $manifest.Files.Count -gt 100){throw 'N425_PACKAGE_BOUND'}
 foreach($f in $manifest.Files){if($f.Path -match '\.\.|:' -or [IO.Path]::IsPathRooted($f.Path)){throw 'N425_PACKAGE_PATH'};$path=[IO.Path]::GetFullPath((Join-Path $runner425Root $f.Path));if(!$path.StartsWith($runner425Root+'\',[StringComparison]::Ordinal)){throw 'N425_PACKAGE_ESCAPE'};Assert-Private425 $path;if((Get-FileHash -LiteralPath $path).Hash -cne $f.Sha256){throw 'N425_PACKAGE_FILE_DRIFT'}}
 $system425=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value -ceq 'S-1-5-18'
 $observed425Pid=Assert-ActiveRuntime425
 if($Mode -cne 'DryRun' -and !(Test-Window425)){$complete425=[pscustomobject]@{status='SKIPPED_WINDOW';mode=$Mode;systemIdentity=$system425;smtpAttempts=0;financialWrites=0;atUtc=[DateTimeOffset]::UtcNow.ToString('o')};$receipt425=Write-Receipt425 $complete425;$complete425|ConvertTo-Json -Depth 6;exit 0}
 if($Mode -cne 'DryRun' -and (!$config425.enabled -or !$system425)){throw 'N425_ACTIVATION_OR_SYSTEM_REQUIRED'}
 $initial=$runner425Root+'\state\initialization.json';Assert-Private425 $initial
 $state=Get-Content -LiteralPath $initial -Raw -Encoding UTF8|ConvertFrom-Json
 if($state.schemaVersion -ne 1 -or $state.ledgerId -cne $config425.ledgerId){throw 'N425_LEDGER_INITIALIZATION_REQUIRED'}
 $sqlConnection425='Server=lpc:.;Database=DataBI;Integrated Security=True;Encrypt=False;Pooling=False;Connect Timeout=10;Application Name=BMS425-WeeklyPrimary'
 $connection425=[Data.SqlClient.SqlConnection]::new($sqlConnection425);$connection425.Open()
 $cmd=$connection425.CreateCommand();$cmd.CommandTimeout=10;$cmd.CommandText="SET NOCOUNT ON; DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'BMS425:weekly-primary:server',@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0; SELECT @r;"
 try{$lock425=[int]$cmd.ExecuteScalar()}finally{$cmd.Dispose()}
 if($lock425 -lt 0){$complete425=[pscustomobject]@{status='SKIPPED_CONCURRENT_SQL_LOCK';mode=$Mode;smtpAttempts=0;financialWrites=0};$receipt425=Write-Receipt425 $complete425;$complete425|ConvertTo-Json;exit 0}
 Import-Module ($runner425Root+'\source425\PrimarySource425.psm1') -Force -ErrorAction Stop
 $source425=Get-BmsWeeklyPrimary425Source -SqlConnectionString $sqlConnection425 -AsOfUtc ([DateTimeOffset]::UtcNow) -SourceReadMode StoredProcedure
 $sql425=$true
 $resolved425=Resolve-BmsWeeklyPrimary425Recipients -Source $source425 -LdapServer $config425.ldapServer -CorporateDomain 'example.invalid'
 . ($runner425Root+'\Coverage425.ps1')
 $coverageResult425=Get-WeeklyCoverage425 -Source $source425 -Resolved $resolved425
 $ad425=$coverageResult425.AdResolution;$coverage425=$coverageResult425.Metrics;$coverage425Blocked=$coverageResult425.Blocked
 if($Mode -ceq 'DryRun' -and $coverage425Blocked){throw 'N425_DRYRUN_SOURCE_OR_DIRECTORY_COVERAGE_BLOCKED'}
 $batches425=@($resolved425.batches);if($batches425.Count -gt 300){throw 'N425_OWNER_BOUND'}
 $row425Count=0;$owner425Map=@{}
 foreach($b in $batches425){$key=([Guid]$b.employeeId).ToString('D');if($owner425Map.ContainsKey($key) -or @($b.rows).Count -gt 200){throw 'N425_OWNER_DUPLICATE_OR_BOUND'};$owner425Map[$key]=$b;$row425Count+=@($b.rows).Count}
 if($row425Count -gt 5000){throw 'N425_GLOBAL_ROWS_BOUND'}
 if((Assert-ActiveRuntime425) -ne $observed425Pid){throw 'N425_PROCESS_CHANGED'}
 $input425=[ordered]@{mode=$Mode;asOfUtc=$resolved425.revalidatedAtUtc;weekMonday=$resolved425.weekMonday;batches=$batches425;sqlLockHeld=$true;sourceChecksPassed=$true;runtimeServicePid=$observed425Pid;sourceEvidence=$resolved425.sourceEvidence}
 $process425=[Diagnostics.Process]::new();$process425.StartInfo=[Diagnostics.ProcessStartInfo]::new();$process425.StartInfo.FileName=$runtime425+'\dotnet.exe';$process425.StartInfo.Arguments='"'+$runner425Root+'\Bms.WeeklyPrimary425.dll"';$process425.StartInfo.WorkingDirectory=$runner425Root;$process425.StartInfo.UseShellExecute=$false;$process425.StartInfo.CreateNoWindow=$true;$process425.StartInfo.RedirectStandardInput=$true;$process425.StartInfo.RedirectStandardOutput=$true;$process425.StartInfo.RedirectStandardError=$true;$process425.StartInfo.StandardOutputEncoding=[Text.UTF8Encoding]::new($false,$true)
 [void]$process425.Start();$stderr425=$process425.StandardError.ReadToEndAsync();Write-Child425 $input425
 $clock425=[Diagnostics.Stopwatch]::StartNew();$callbacks425=0
 while($null -eq $complete425){
  if($clock425.Elapsed.TotalSeconds -gt 1200){throw 'N425_RUN_TIMEOUT_NO_RETRY'}
  $line425Task=$process425.StandardOutput.ReadLineAsync();if(!$line425Task.Wait(45000)){throw 'N425_PROTOCOL_TIMEOUT_NO_RETRY'}
  $line425=$line425Task.Result;if($null -eq $line425 -or $line425.Length -gt 65536){throw 'N425_PROTOCOL_OUTPUT_BOUND'}
  $event425=$line425|ConvertFrom-Json
  if($event425.kind -ceq 'complete'){$complete425=$event425;break}
  if($event425.kind -cne 'revalidate' -or $Mode -ceq 'DryRun'){throw 'N425_PROTOCOL_KIND'}
  $callbacks425++;if($callbacks425 -gt 2*$batches425.Count){throw 'N425_REVALIDATION_COUNT_BOUND'}
  $owner425Key=([Guid]$event425.employeeId).ToString('D');if(!$owner425Map.ContainsKey($owner425Key)){throw 'N425_REVALIDATION_OWNER_OUTSIDE_BATCH'}
  try{
   if((Assert-ActiveRuntime425) -ne $observed425Pid){throw 'N425_PROCESS_CHANGED'}
   $fresh425=Get-BmsWeeklyPrimary425Revalidation -InitialBatch $owner425Map[$owner425Key] -SqlConnectionString $sqlConnection425 -AsOfUtc ([DateTimeOffset]::UtcNow) -LdapServer $config425.ldapServer -CorporateDomain 'example.invalid' -SourceReadMode StoredProcedure
   $response425=@{kind='revalidation';employeeId=$owner425Key;eligible=[bool]$fresh425.eligible;sourceReadStartedAtUtc=$fresh425.sourceReadStartedAtUtc;sourceReadCompletedAtUtc=$fresh425.sourceReadCompletedAtUtc;revalidatedAtUtc=$fresh425.revalidatedAtUtc;batch=$fresh425.batch}
  }catch{$read425Errors++;$time425=[DateTimeOffset]::UtcNow.ToString('o');$response425=@{kind='revalidation';employeeId=$owner425Key;eligible=$false;sourceReadStartedAtUtc=$time425;sourceReadCompletedAtUtc=$time425;revalidatedAtUtc=$time425;batch=$null}}
  Write-Child425 $response425
 }
 $process425.StandardInput.Close();if(!$process425.WaitForExit(10000)){throw 'N425_CHILD_EXIT_TIMEOUT_NO_RETRY'}
 if($stderr425.GetAwaiter().GetResult().Length -gt 0){throw 'N425_CHILD_STDERR_NO_RETRY'}
 if((Assert-ActiveRuntime425) -ne $observed425Pid){throw 'N425_PROCESS_CHANGED'}
 $complete425|Add-Member -NotePropertyName sqlAccess -NotePropertyValue $sql425
 $complete425|Add-Member -NotePropertyName adResolution -NotePropertyValue $ad425
 $complete425|Add-Member -NotePropertyName revalidationErrors -NotePropertyValue $read425Errors
 $complete425|Add-Member -NotePropertyName selectedOwnerCount -NotePropertyValue $batches425.Count
 $complete425|Add-Member -NotePropertyName selectedRowCount -NotePropertyValue $row425Count
 $complete425|Add-Member -NotePropertyName sourceModuleSha256 -NotePropertyValue (Get-FileHash -LiteralPath ($runner425Root+'\source425\PrimarySource425.psm1')).Hash
 foreach($property in $coverage425.PSObject.Properties){$complete425|Add-Member -NotePropertyName $property.Name -NotePropertyValue $property.Value}
 if($Mode -ceq 'DryRun'){$complete425|Add-Member -NotePropertyName smtpAttempts -NotePropertyValue 0}
 $child425Exit=$process425.ExitCode
 if($coverage425Blocked){$complete425.status='COMPLETED_WITH_SOURCE_OR_DIRECTORY_BLOCKS_NO_AUTO_RETRY';if($child425Exit -eq 0){$child425Exit=2}}
 $complete425|Add-Member -NotePropertyName childExitCode -NotePropertyValue $child425Exit
 $receipt425=Write-Receipt425 $complete425
 $complete425|ConvertTo-Json -Depth 16
 exit $child425Exit
}catch{
 $safe425=if($_.Exception.Message -match '(N425_[A-Z0-9_]+|P425_[A-Z0-9_]+|SOURCE425_[A-Z0-9_]+|DIRECTORY425_[A-Z0-9_]+)'){$matches[1]}else{'N425_UNAVAILABLE_NO_RETRY'}
 if($process425 -and !$process425.HasExited){$process425.Kill();[void]$process425.WaitForExit(5000)}
 $failure425=[pscustomobject]@{status='FAILED_NO_AUTOMATIC_RETRY';mode=$Mode;code=$safe425;sqlAccess=$sql425;adResolution=$ad425;outcomeMayBeUncertain=($Mode -cne 'DryRun');retryAllowed=$false;financialWrites=0;atUtc=[DateTimeOffset]::UtcNow.ToString('o')}
 if($null -ne $coverage425){foreach($property in $coverage425.PSObject.Properties){$failure425|Add-Member -NotePropertyName $property.Name -NotePropertyValue $property.Value}}
 try{if(Test-Path -LiteralPath ($runner425Root+'\state\initialization.json')){$receipt425=Write-Receipt425 $failure425}}catch{}
 $failure425|ConvertTo-Json -Depth 6;exit 2
}finally{if($process425){$process425.Dispose()};if($connection425){$connection425.Dispose()}}
