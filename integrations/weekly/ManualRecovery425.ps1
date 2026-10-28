# Library only. Never sends, resets markers, removes state, or starts a task.
function Approve-WeeklyPreSendRetry425 {
 [CmdletBinding()]
 param([Parameter(Mandatory)][ValidatePattern('^\d{4}-\d{2}-\d{2}$')][string]$WeekMonday,
  [Parameter(Mandatory)][Guid]$EmployeeId,[Parameter(Mandatory)][ValidatePattern('^[A-F0-9]{64}$')][string]$ExpectedOutcomeSha256,
  [Parameter(Mandatory)][ValidateSet('BMS425_MANUAL_RETRY_DEFINITE_PRESEND_ONLY')][string]$Approval)
 Set-StrictMode -Version Latest;$ErrorActionPreference='Stop'
 $root='D:\BMS\artifacts\bms-weekly-primary425-20261025\runner425'
 $identity=[Security.Principal.WindowsIdentity]::GetCurrent();$principal=[Security.Principal.WindowsPrincipal]::new($identity)
 if($env:COMPUTERNAME -cne 'BMS' -or !$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -or $EmployeeId -eq [Guid]::Empty){throw 'N425_MANUAL_ADMIN_REQUIRED'}
 $now=[DateTimeOffset]::UtcNow;$local=$now.ToOffset([TimeSpan]::FromHours(5))
 if($local.DayOfWeek -ne [DayOfWeek]::Monday -or $local.Hour -lt 9 -or $local.Hour -ge 18 -or $local.ToString('yyyy-MM-dd') -cne $WeekMonday){throw 'N425_MANUAL_SAME_MONDAY_WINDOW_REQUIRED'}
 $directory=$root+'\state\weeks\'+$WeekMonday+'\'+$EmployeeId.ToString('N')
 $outcome=$directory+'\attempt-001.result.json';$prepared=$directory+'\prepared.json'
 foreach($path in @($root,$directory,$outcome,$prepared)){
  $node=Get-Item -LiteralPath $path;while($node){if($node.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'N425_MANUAL_REPARSE'};$node=if($node -is [IO.FileInfo]){$node.Directory}else{$node.Parent}}
  $a=Get-Acl -LiteralPath $path;$rules=@($a.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]));if($a.GetOwner([Security.Principal.SecurityIdentifier]).Value -cnotin @('S-1-5-18','S-1-5-32-544') -or $rules.Count -ne 2 -or @($rules|Where-Object {$_.AccessControlType -ne 'Allow' -or $_.IdentityReference.Value -cnotin @('S-1-5-18','S-1-5-32-544')}).Count -ne 0){throw 'N425_MANUAL_PRIVATE_ACL'}
 }
 if(!(Test-Path -LiteralPath ($directory+'\attempt-001.marker')) -or (Test-Path -LiteralPath ($directory+'\attempt-002.marker')) -or (Test-Path -LiteralPath ($directory+'\manual-002.authorization.json'))){throw 'N425_MANUAL_ATTEMPT_STATE'}
 if((Get-FileHash -LiteralPath $outcome).Hash -cne $ExpectedOutcomeSha256){throw 'N425_MANUAL_EXPECTED_RECEIPT_DRIFT'}
 $prior=Get-Content -LiteralPath $outcome -Raw -Encoding UTF8|ConvertFrom-Json
 $preparedHash=(Get-FileHash -LiteralPath $prepared).Hash
 if($prior.state -cne 'DefinitePreSendFailure' -or $prior.sendStarted -ne $false -or $prior.attemptNumber -ne 1 -or $prior.preparedSha256 -cne $preparedHash){throw 'N425_MANUAL_UNCERTAIN_OR_ACCEPTED_FORBIDDEN'}
 $record=@{attemptNumber=2;weekMonday=$WeekMonday;employeeId=$EmployeeId.ToString('D');priorOutcomeSha256=$ExpectedOutcomeSha256;preparedSha256=$preparedHash;approvedAtUtc=$now.ToString('o');approvedBySid=$identity.User.Value;approval=$Approval}
 $bytes=[Text.UTF8Encoding]::new($false,$true).GetBytes(($record|ConvertTo-Json -Compress));$path=$directory+'\manual-002.authorization.json'
 $f=[IO.FileStream]::new($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None);try{$f.Write($bytes,0,$bytes.Length);$f.Flush($true)}finally{$f.Dispose()}
 [pscustomobject]@{Status='MANUAL_SECOND_ATTEMPT_AUTHORIZATION_ONLY';AuthorizationSha256=(Get-FileHash -LiteralPath $path).Hash;SenderInvoked=$false;MarkersRemoved=0}
}
