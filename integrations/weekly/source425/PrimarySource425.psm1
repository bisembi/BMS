Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
function Get-Hash425([string]$Text){$h=[Security.Cryptography.SHA256]::Create();try{([BitConverter]::ToString($h.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)))).Replace('-','')}finally{$h.Dispose()}}
function Id425($Value){$id=[guid]::Parse([string]$Value);if($id -eq [guid]::Empty){throw 'SOURCE425_EMPTY_ID'};$id.ToString('D')}
function Text425($Value,[int]$Max=400){$v=[string]$Value;if([string]::IsNullOrWhiteSpace($v) -or $v.Length -gt $Max -or $v -match '[\x00-\x1f\x7f\u2028\u2029]'){throw 'SOURCE425_INVALID_DISPLAY_FIELD'};$v}
function Test-Runtime425([string]$ManifestHash){
 $expected='D:\BMS\releases\catalog485-20261028\service\Bms.Api.exe';$s=Get-CimInstance Win32_Service -Filter "Name='BmsWebCurrent'"
 if($env:COMPUTERNAME -cne 'BMS' -or $s.State -cne 'Running' -or $s.PathName -cne $expected -or $s.StartName -cne 'NT SERVICE\BmsWebCurrent' -or $s.ProcessId -le 0){throw 'SOURCE425_RUNTIME_DRIFT'}
 $pidValue=[int]$s.ProcessId;if((Get-Process -Id $pidValue).Path -cne $expected -or (Get-FileHash -LiteralPath 'D:\BMS\releases\catalog485-20261028\manifest.json').Hash -cne $ManifestHash){throw 'SOURCE425_RELEASE_DRIFT'};$pidValue
}
function Get-Party425($Head){
 switch -CaseSensitive ($Head.OperationKind){
  'SupplierNull' {return Text425 $Head.CounterpartyName 220}
  'BusinessTrip' {$decoded=$Head.OperationEmployeeNamesJson|ConvertFrom-Json;$names=@($decoded);if($names.Count -lt 1 -or $names.Count -gt 40){throw 'SOURCE425_TRIP_NAMES'};foreach($n in $names){if($n -isnot [string]){throw 'SOURCE425_TRIP_NAMES'};[void](Text425 $n 150)};return Text425 ($names -join ', ') 6400}
  'Accountable' {return Text425 $Head.OperationEmployeeName 150}
  'Payroll' {
   if($Head.OperationPayrollMode -ceq 'Employee'){return Text425 $Head.OperationEmployeeName 150}
   if($Head.OperationPayrollMode -ceq 'Register'){$register=Text425 $Head.OperationRegisterNumber 100;return ('Оплата зарплаты — по ведомости № '+$register)}
   throw 'SOURCE425_PAYROLL_MODE'
  }
  default {throw 'SOURCE425_UNKNOWN_OPERATION'}
 }
}
function Convert-Source425($Tables,$Policy,[DateTimeOffset]$AsOfUtc,[string]$SqlHash,[string]$PolicyHash,[DateTimeOffset]$Started,[DateTimeOffset]$Completed,[int]$PidValue){
 if($Tables.Count -ne 6 -or $Tables[0].Rows.Count -ne 1){throw 'SOURCE425_TABLE_SHAPE'}
 $coverage=$Tables[0].Rows[0];if($coverage.MachineName -cne 'BMS' -or $coverage.DatabaseName -cne 'DataBI' -or $coverage.SchemaVersion -ne 61 -or $coverage.SelectorVersion -cne 'owner-primary-closure425-r1'){throw 'SOURCE425_RESULT_DATABASE_GUARD'};$lines=@($Tables[1].Rows);$assignments=@($Tables[2].Rows);$facts=@($Tables[3].Rows);$rounding=@($Tables[4].Rows)
 if([long]$coverage.PaidRequestCount -gt 5000 -or [long]$coverage.PaidLineCount -gt 5000 -or $assignments.Count -gt 5000){throw 'SOURCE425_GLOBAL_BOUND'}
 if(@($lines|ForEach-Object RequestId|Sort-Object -Unique).Count -ne [long]$coverage.PaidRequestCount -or @($lines|Where-Object LineId|Select-Object -ExpandProperty LineId|Sort-Object -Unique).Count -ne [long]$coverage.PaidLineCount){throw 'SOURCE425_COMPLETENESS_GAP'}
 $anchors=@($Policy.articles|Where-Object classification -ne 'INCLUDE');if($Tables[5].Rows.Count -ne $anchors.Count){throw 'SOURCE425_POLICY_ANCHOR_GAP'}
 foreach($a in $Tables[5].Rows){if(!$a.ActualId -or $a.Id -cne $a.ActualId -or $a.Code -cne $a.ActualCode -or $a.Name -cne $a.ActualName -or $a.ActualIsGroup){throw 'SOURCE425_POLICY_ANCHOR_DRIFT'}}
 $ownerExcluded=[Collections.Generic.List[object]]::new();$owners=[Collections.Generic.List[object]]::new();$requestExcluded=[Collections.Generic.List[object]]::new();$flagged=[Collections.Generic.List[object]]::new()
 foreach($g in ($assignments|Group-Object EmployeeId)){
  $good=@($g.Group|Where-Object {$_.EmployeeActive -and $_.CenterActive -and !$_.IsGroup -and $_.AssignmentActive -and $_.HasActiveOwnerRole})
  if($good.Count -eq 0){$ownerExcluded.Add([pscustomobject]@{employeeId=$g.Name;reason='INACTIVE_OWNER_ROLE_OR_ASSIGNMENT'});continue}
  if(@($good|Where-Object {!$_.MappingActive -or $_.EmployeeMappingCount -ne 1 -or $_.ActorMappingCount -ne 1}).Count -gt 0 -or @($good.ActorExternalId|Sort-Object -Unique).Count -ne 1){$ownerExcluded.Add([pscustomobject]@{employeeId=$g.Name;reason='AMBIGUOUS_OR_INACTIVE_OWNER_MAPPING'});continue}
  try{$id=Id425 $g.Name;$name=Text425 $good[0].EmployeeName 140;$sid=[Security.Principal.SecurityIdentifier]::new([string]$good[0].ActorExternalId).Value;if($sid -cne [string]$good[0].ActorExternalId){throw 'SOURCE425_SID_FORMAT'}}catch{$ownerExcluded.Add([pscustomobject]@{employeeId=$g.Name;reason='INVALID_OWNER_IDENTITY'});continue}
  $owners.Add([pscustomobject]@{employeeId=$id;ownerDisplayName=$name;actorExternalId=$sid;costCenterIds=@($good.CostCenterId|Sort-Object -Unique)})
 }
 if($owners.Count -gt 300){throw 'SOURCE425_OWNER_BOUND'}
 $items=[Collections.Generic.List[object]]::new();$localDate=$AsOfUtc.ToOffset([TimeSpan]::FromHours(5)).ToString('yyyy-MM-dd')
 foreach($g in ($lines|Group-Object RequestId)){
  $head=$g.Group[0];$rid=Id425 $g.Name;$reason=$null;$number=[string]$head.RequestNumber
  if(@($g.Group|Where-Object {!$_.LineId}).Count -gt 0){$reason='NO_LINES'}
  elseif(@($g.Group.LineId|Sort-Object -Unique).Count -ne $g.Count){$reason='AMBIGUOUS_INITIATOR_MAPPING'}
  elseif($head.OperationKind -cin @('Tax','PayrollAdvance')){$reason='EXCLUDED_OPERATION_'+$head.OperationKind}
  elseif($head.OperationKind -cnotin @('SupplierNull','Payroll','Accountable','BusinessTrip')){$reason='UNKNOWN_OPERATION'}
  elseif(@($g.Group|Where-Object {$_.ArticleClassification -like 'EXCLUDE_*'}).Count -gt 0){$reason=(@($g.Group.ArticleClassification|Where-Object {$_ -like 'EXCLUDE_*'}|Sort-Object -Unique) -join '+')}
  elseif(@($g.Group|Where-Object {$_.ArticleClassification -like 'HOLD_*' -or $_.ArticleIsGroup}).Count -gt 0){$reason='ARTICLE_POLICY_REVIEW'}
  elseif(!$head.FullyPaidAtUtc){$reason='FULL_PAYMENT_TIME_MISSING'}
  elseif($head.InitiatorActorMatchCount -ne 1 -or !$head.InitiatorEmployeeId -or [string]::IsNullOrWhiteSpace($head.InitiatorName)){$reason='INITIATOR_DISPLAY_UNRESOLVED'}
  if($reason){$requestExcluded.Add([pscustomobject]@{requestId=$rid;number=$number;reason=$reason});continue}
  try{
   $number=Text425 $number 70;$currency=Text425 $head.CurrencyCode 3;if($currency -cnotmatch '^[A-Z]{3}$'){throw 'SOURCE425_CURRENCY'}
   $native=[decimal]0;$kzt=[decimal]0;$remainingAccrual=$false
   foreach($l in $g.Group){if($null -eq $l.AmountKzt -or [decimal]$l.Amount -le 0 -or [decimal]$l.AmountKzt -le 0 -or [decimal]$l.ConfirmedClosedAmount -lt 0 -or [decimal]$l.ConfirmedClosedAmountKzt -lt 0 -or [decimal]$l.ConfirmedClosedAmount -gt [decimal]$l.Amount -or [decimal]$l.ConfirmedClosedAmountKzt -gt [decimal]$l.AmountKzt){throw 'SOURCE425_AMOUNT_STATE'};$native+=[decimal]$l.Amount;$kzt+=[decimal]$l.AmountKzt
    if($l.AccrualSnapshotFormsManagerialAccrual -eq $true){$rn=[decimal]$l.Amount-[decimal]$l.ConfirmedClosedAmount;$rk=[decimal]$l.AmountKzt-[decimal]$l.ConfirmedClosedAmountKzt;if(($rn -eq 0) -ne ($rk -eq 0)){throw 'SOURCE425_CLOSING_BALANCE'};if($rn -gt 0){$remainingAccrual=$true}}
    elseif([decimal]$l.ConfirmedClosedAmount -ne 0 -or [decimal]$l.ConfirmedClosedAmountKzt -ne 0){throw 'SOURCE425_NONACCRUAL_CLOSING'}
   }
   if([decimal]::Round($native,2) -ne $native){throw 'SOURCE425_AMOUNT_PRECISION'}
   $unrecorded=!$head.ClosedPartially -and !$head.LatestClosingDocumentsReceivedDate -and [decimal]$head.ConfirmedClosingControlAmount -eq 0 -and @($g.Group|Where-Object {$_.ConfirmedClosedAmount -ne 0 -or $_.ConfirmedClosedAmountKzt -ne 0}).Count -eq 0
   $partial=$head.ClosedPartially -and !!$head.LatestClosingDocumentsReceivedDate -and [decimal]$head.ConfirmedClosingControlAmount -gt 0 -and $remainingAccrual
   if(!$unrecorded -and !$partial){throw 'SOURCE425_CLOSURE_REVIEW'}
   $paidAt=[DateTimeOffset]::Parse([string]$head.FullyPaidAtUtc);$paymentDate=$paidAt.ToOffset([TimeSpan]::FromHours(5)).ToString('yyyy-MM-dd');if($paymentDate -cgt $localDate){throw 'SOURCE425_FUTURE_PAYMENT'}
   $eventRows=@($facts|Where-Object RequestId -eq $rid);if($eventRows.Count -eq 0 -or @($eventRows.EventId|Sort-Object -Unique).Count -ne $eventRows.Count -or @($eventRows|Where-Object {$_.CurrencyCode -cne $currency -or $_.Amount -le 0 -or $_.AmountKzt -le 0}).Count -gt 0){throw 'SOURCE425_PAYMENT_LEDGER'}
   $cash=[decimal]0;$cashKzt=[decimal]0;$reversed=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
   foreach($event in $eventRows){if($event.IsReversal){$original=@($eventRows|Where-Object {$_.EventId -eq $event.ReversedEventId -and !$_.IsReversal});if($original.Count -ne 1 -or !$reversed.Add([string]$event.ReversedEventId) -or $original[0].Amount -ne $event.Amount -or $original[0].AmountKzt -ne $event.AmountKzt){throw 'SOURCE425_PAYMENT_REVERSAL'};$cash-=[decimal]$event.Amount;$cashKzt-=[decimal]$event.AmountKzt}else{$cash+=[decimal]$event.Amount;$cashKzt+=[decimal]$event.AmountKzt}}
   $waivers=@($rounding|Where-Object {$_.RequestId -eq $rid -and $_.RoundingAmount -gt 0 -and $_.ReversalCount -eq 0});$waived=[decimal]0
   if($waivers.Count -gt 1){throw 'SOURCE425_ROUNDING_AMBIGUOUS'}
   if($waivers.Count -eq 1){$w=$waivers[0];$we=@($eventRows|Where-Object {$_.EventId -eq $w.EventId -and !$_.IsReversal});if($currency -cne 'KZT' -or $w.ApprovalCount -ne 1 -or $w.RoundingAmount -ge 1 -or $w.ObligationAmount -ne $native -or $w.CashAmount -ne $cash -or $cash -ne $cashKzt -or $we.Count -ne 1 -or $reversed.Contains([string]$w.EventId) -or $w.CurrencyId -ne $we[0].CurrencyId -or [DateTimeOffset]::Parse([string]$w.EffectiveAtUtc) -ne [DateTimeOffset]::Parse([string]$we[0].PaidAt) -or [DateTimeOffset]::Parse([string]$w.EffectiveAtUtc) -gt [DateTimeOffset]::Parse([string]$w.ApprovedAtUtc)){throw 'SOURCE425_ROUNDING_INVALID'};$waived=[decimal]$w.RoundingAmount;if($cash+$waived -ne $native){throw 'SOURCE425_ROUNDING_COVERAGE'}}
   if($cash+$waived -lt $native){throw 'SOURCE425_NOT_FULLY_PAID'}
   $applicable=@($owners|Where-Object {$o=$_;$head.DefaultCostCenterId -in $o.costCenterIds -and @($g.Group|Where-Object {$_.ExplicitLineCostCenterId -and $_.ExplicitLineCostCenterId -notin $o.costCenterIds}).Count -eq 0})
   if($applicable.Count -eq 0){throw 'SOURCE425_NO_OWNER_WITH_FULL_SCOPE'}
   $articles=@(foreach($ag in ($g.Group|Sort-Object SortOrder,LineId|Group-Object CashFlowItemId)){$names=@($ag.Group.CashFlowName|Sort-Object -Unique);if($names.Count -ne 1){throw 'SOURCE425_ARTICLE_NAME_CONFLICT'};[pscustomobject]@{id=(Id425 $ag.Name);name=(Text425 $names[0] 400)}})
   $row=[pscustomobject]@{documentId=$rid;documentKey=$rid;number=$number;paymentDate=$paymentDate;party=(Get-Party425 $head);requestAmount=$native.ToString('0.00',[Globalization.CultureInfo]::InvariantCulture);currency=$currency;amountKind='requestAmount';closureState=if($partial){'partially-recorded'}else{'not-recorded'};initiatorId=(Id425 $head.InitiatorEmployeeId);initiatorName=(Text425 $head.InitiatorName 140);costCenterOwnerIds=@($applicable.employeeId|Sort-Object -Unique);cashFlowArticles=$articles;verifiedLink=[pscustomobject]@{verified=$true;url=('https://bms.example.invalid/?expenseRequest='+$rid);evidence='Fresh BMS SQL request identity; source421 frontend/src/utils/taskMailLink.ts:2 and App.tsx:297; navigation only, no browser-click or access bypass.'}}
   $items.Add([pscustomobject]@{row=$row;lineCount=$g.Count;cash=$cash;rounding=$waived;requestAmount=$native;currency=$currency})
   foreach($flag in @($g.Group.ArticleFlag|Where-Object {$_}|Sort-Object -Unique)){$flagged.Add([pscustomobject]@{requestId=$rid;number=$number;flag=$flag})}
  }catch{$code='SOURCE425_REQUEST_UNAVAILABLE';if($_.Exception.Message -match '(SOURCE425_[A-Z0-9_]+)'){$code=$matches[1]};$requestExcluded.Add([pscustomobject]@{requestId=$rid;number=$number;reason=$code})}
 }
 $batches=@(foreach($owner in $owners){$rows=@($items|Where-Object {$owner.employeeId -in $_.row.costCenterOwnerIds}|ForEach-Object row|Sort-Object documentId);if($rows.Count -gt 200){throw 'SOURCE425_PER_OWNER_BOUND'};if($rows.Count -gt 0){[pscustomobject]@{employeeId=$owner.employeeId;ownerDisplayName=$owner.ownerDisplayName;asOfDate=$localDate;rows=$rows}}})
 $totals=@(foreach($cg in ($items|Group-Object currency)){$sum=[decimal]0;$cash=[decimal]0;$round=[decimal]0;foreach($i in $cg.Group){$sum+=$i.requestAmount;$cash+=$i.cash;$round+=$i.rounding};[pscustomobject]@{currency=$cg.Name;requestAmount=$sum.ToString('0.00',[Globalization.CultureInfo]::InvariantCulture);bankCash=$cash.ToString('0.00',[Globalization.CultureInfo]::InvariantCulture);approvedRounding=$round.ToString('0.00',[Globalization.CultureInfo]::InvariantCulture)}})
 $local=$AsOfUtc.ToOffset([TimeSpan]::FromHours(5));$days=([int]$local.DayOfWeek+6)%7;$monday=$local.AddDays(-$days).ToString('yyyy-MM-dd');$eligibleLines=0;foreach($item in $items){$eligibleLines+=$item.lineCount}
 $holds=@($requestExcluded|Where-Object {$_.reason -notlike 'EXCLUDE_*' -and $_.reason -notlike 'EXCLUDED_OPERATION_*'})
 $holdCounts=@($holds|Group-Object reason|ForEach-Object{[pscustomobject]@{reason=$_.Name;count=$_.Count}})
 [pscustomobject]@{schemaVersion=1;asOfUtc=$AsOfUtc.ToUniversalTime().ToString('o');asOfDate=$localDate;weekMonday=$monday;sourceReadStartedAtUtc=$Started.ToString('o');sourceReadCompletedAtUtc=$Completed.ToString('o');owners=$owners.ToArray();batches=$batches;sourceEvidence=[pscustomobject]@{policyVersion=$Policy.policyVersion;sqlSha256=$SqlHash;policySha256=$PolicyHash;servicePid=$PidValue;readOnly=$true;sqlElapsedMilliseconds=[math]::Round(($Completed-$Started).TotalMilliseconds);paidRequestCount=[long]$coverage.PaidRequestCount;paidLineCount=[long]$coverage.PaidLineCount;activeOwnerAssignmentCount=[long]$coverage.ActiveOwnerAssignmentCount;ownerCount=$owners.Count;eligibleRequestCount=$items.Count;eligibleLineCount=$eligibleLines;nonemptyOwnerBatchCount=$batches.Count;selectedOwnerCount=$batches.Count;selectedRequestCount=$items.Count;holdRequestCount=$holds.Count;holdReasonCounts=$holdCounts;totals=$totals;excludedOwners=$ownerExcluded.ToArray();excludedRequests=$requestExcluded.ToArray();flaggedRequests=$flagged.ToArray();completeGlobalRead=$true;initiatorsAreRecipients=$false;sourcePolicy='owner-only-paid-unclosed-exclude-goods-tax-advance425-v1'}}
}
function Get-BmsWeeklyPrimary425Source {
 [CmdletBinding()]param([Parameter(Mandatory)][string]$SqlConnectionString,[DateTimeOffset]$AsOfUtc=[DateTimeOffset]::UtcNow,[ValidateSet('DirectQuery','StoredProcedure')][string]$SourceReadMode='DirectQuery',[string]$ExpectedManifestSha256='1943688D3A6E6C33C42CC186862633D2FA0A349CF373A3266D3CA8CB02B22724',[string]$SqlText,[string]$ArticlePolicyJson)
 if(!$SqlText){$SqlText=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Select-WeeklyPrimary425.sql') -Raw -Encoding UTF8};if(!$ArticlePolicyJson){$ArticlePolicyJson=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ArticlePolicy425.json') -Raw -Encoding UTF8}
 if($SqlText -match '(?im)\b(INSERT|UPDATE|DELETE|MERGE|TRUNCATE|DROP|ALTER|CREATE|EXEC|EXECUTE|GRANT|REVOKE|DENY|KILL|BACKUP|RESTORE|DBCC)\b'){throw 'SOURCE425_NON_READ_SQL'}
 $policy=$ArticlePolicyJson|ConvertFrom-Json;if($policy.schemaVersion -ne 1 -or $policy.maxOwners -ne 300 -or $policy.maxRows -ne 5000 -or $policy.maxRowsPerOwner -ne 200 -or @($policy.articles.id|Sort-Object -Unique).Count -ne $policy.articles.Count){throw 'SOURCE425_POLICY_INVALID'}
 $builder=[Data.SqlClient.SqlConnectionStringBuilder]::new($SqlConnectionString);if(!$builder.IntegratedSecurity -or $builder.InitialCatalog -cne 'DataBI' -or $builder.DataSource -cnotin @('lpc:.','.','(local)')){throw 'SOURCE425_SQL_TARGET_INVALID'}
 $pidValue=Test-Runtime425 $ExpectedManifestSha256;$connection=[Data.SqlClient.SqlConnection]::new($SqlConnectionString);$transaction=$null;$started=[DateTimeOffset]::UtcNow
 try{
  $connection.Open()
  if($SourceReadMode -ceq 'DirectQuery'){$g=$connection.CreateCommand();$g.CommandTimeout=10;$g.CommandText="SELECT CONVERT(nvarchar(100),SERVERPROPERTY('MachineName')),DB_NAME(),(SELECT MAX(version_no) FROM bms_web.schema_version);";$r=$g.ExecuteReader();try{if(!$r.Read() -or $r.GetString(0) -cne 'BMS' -or $r.GetString(1) -cne 'DataBI' -or [int]$r.GetValue(2) -ne 61){throw 'SOURCE425_DATABASE_GUARD'}}finally{$r.Dispose();$g.Dispose()}}
  $transaction=$connection.BeginTransaction([Data.IsolationLevel]::Serializable);$command=$connection.CreateCommand();$command.Transaction=$transaction;$command.CommandTimeout=20
  if($SourceReadMode -ceq 'StoredProcedure'){$command.CommandType=[Data.CommandType]::StoredProcedure;$command.CommandText='bms_web.ReadWeeklyPrimary425'}else{$command.CommandText='SET LOCK_TIMEOUT 3000; SET NOCOUNT ON; '+$SqlText}
  [void]$command.Parameters.Add('@ArticlePolicyJson',[Data.SqlDbType]::NVarChar,-1);$command.Parameters['@ArticlePolicyJson'].Value=$ArticlePolicyJson;[void]$command.Parameters.Add('@AsOfUtc',[Data.SqlDbType]::DateTimeOffset);$command.Parameters['@AsOfUtc'].Value=$AsOfUtc.ToUniversalTime()
  $tables=[Collections.Generic.List[object]]::new();$reader=$command.ExecuteReader()
  try{do{$rows=[Collections.Generic.List[object]]::new();while($reader.Read()){if($rows.Count -ge 10000){throw 'SOURCE425_RESULT_BOUND'};$row=[ordered]@{};for($i=0;$i -lt $reader.FieldCount;$i++){$v=if($reader.IsDBNull($i)){$null}else{$reader.GetValue($i)};if($v -is [DateTimeOffset] -or $v -is [DateTime]){$v=$v.ToString('o')};if($v -is [guid]){$v=$v.ToString('D')};$row[$reader.GetName($i)]=$v};$rows.Add([pscustomobject]$row)};$tables.Add([pscustomobject]@{Rows=$rows.ToArray()})}while($reader.NextResult())}finally{$reader.Dispose();$command.Dispose()}
  $transaction.Commit();$transaction.Dispose();$transaction=$null;$connection.Close();$completed=[DateTimeOffset]::UtcNow
  if((Test-Runtime425 $ExpectedManifestSha256) -ne $pidValue){throw 'SOURCE425_PROCESS_CHANGED'}
  Convert-Source425 $tables.ToArray() $policy $AsOfUtc (Get-Hash425 $SqlText) (Get-Hash425 $ArticlePolicyJson) $started $completed $pidValue
 }catch{$err=$_.Exception;while($err.InnerException -and $err -isnot [Data.SqlClient.SqlException]){$err=$err.InnerException};if($err -is [Data.SqlClient.SqlException]){throw ('SOURCE425_SQL_'+$err.Number)};if($err.Message -match '(SOURCE425_[A-Z0-9_]+)'){throw $matches[1]};throw 'SOURCE425_FETCH_UNAVAILABLE'}finally{if($transaction){try{$transaction.Rollback()}catch{};$transaction.Dispose()};$connection.Dispose()}
}
function Assert-DirectoryMail425([string]$Address,[int]$UserAccountControl,[int]$MailCount,[int]$UniqueUserCount,[string]$CorporateDomain){
 if(($UserAccountControl -band 2) -ne 0){throw 'DIRECTORY425_DISABLED'}
 if($MailCount -ne 1){throw 'DIRECTORY425_REQUIRED_ATTRIBUTE'}
 if($UniqueUserCount -ne 1){throw 'DIRECTORY425_MAIL_NOT_UNIQUE'}
 if($Address.Length -gt 254 -or $Address -match '[\x00-\x1f\x7f]'){throw 'DIRECTORY425_INVALID_MAIL'};$parsed=[Net.Mail.MailAddress]::new($Address)
 if($parsed.Address -cne $Address -or $parsed.DisplayName -cne '' -or !$Address.EndsWith('@'+$CorporateDomain,[StringComparison]::OrdinalIgnoreCase)){throw 'DIRECTORY425_NOT_CORPORATE_MAIL'}
}
function Resolve-BmsWeeklyPrimary425Recipients {
 [CmdletBinding()]param([Parameter(Mandatory)]$Source,[string]$LdapServer='directory.example.invalid',[string]$CorporateDomain='example.invalid')
 if($Source.schemaVersion -ne 1 -or $Source.owners.Count -gt 300 -or $Source.sourceEvidence.completeGlobalRead -ne $true -or $CorporateDomain -cne 'example.invalid' -or $LdapServer -cne 'directory.example.invalid'){throw 'SOURCE425_DIRECTORY_CONFIG'}
 Add-Type -AssemblyName System.DirectoryServices.Protocols;$ldap=$null;$contacts=[Collections.Generic.List[object]]::new();$excluded=[Collections.Generic.List[object]]::new();$batches=[Collections.Generic.List[object]]::new();$bindSucceeded=$false;$rootDseSucceeded=$false
 try{
  $ldap=[DirectoryServices.Protocols.LdapConnection]::new([DirectoryServices.Protocols.LdapDirectoryIdentifier]::new($LdapServer,389,$true,$false));$ldap.AuthType=[DirectoryServices.Protocols.AuthType]::Negotiate;$ldap.Timeout=[TimeSpan]::FromSeconds(5);$ldap.SessionOptions.ProtocolVersion=3;$ldap.SessionOptions.Signing=$true;$ldap.SessionOptions.Sealing=$true;$ldap.Bind();$bindSucceeded=$true
  $baseResponse=$ldap.SendRequest([DirectoryServices.Protocols.SearchRequest]::new($null,'(objectClass=*)',[DirectoryServices.Protocols.SearchScope]::Base,[string[]]@('defaultNamingContext')));$base=[string]$baseResponse.Entries[0].Attributes['defaultNamingContext'][0];if([string]::IsNullOrWhiteSpace($base)){throw 'DIRECTORY425_ROOTDSE'};$rootDseSucceeded=$true
  foreach($owner in $Source.owners){
   try{
    $sid=[Security.Principal.SecurityIdentifier]::new([string]$owner.actorExternalId);$sidBytes=[byte[]]::new($sid.BinaryLength);$sid.GetBinaryForm($sidBytes,0);$sidFilter=($sidBytes|ForEach-Object {'\'+$_.ToString('X2')}) -join ''
    $req=[DirectoryServices.Protocols.SearchRequest]::new($base,'(&(objectCategory=person)(objectClass=user)(objectSid='+$sidFilter+'))',[DirectoryServices.Protocols.SearchScope]::Subtree,[string[]]@('objectSid','mail','userAccountControl'));$req.SizeLimit=3;$response=$ldap.SendRequest($req)
    if($response.Entries.Count -ne 1 -or $req.Attributes.Count -ne 3){throw 'DIRECTORY425_SID_NOT_UNIQUE'};$entry=$response.Entries[0]
    foreach($attr in @('objectSid','mail','userAccountControl')){if(!$entry.Attributes.Contains($attr) -or $entry.Attributes[$attr].Count -ne 1){throw 'DIRECTORY425_REQUIRED_ATTRIBUTE'}}
    if([Security.Principal.SecurityIdentifier]::new([byte[]]$entry.Attributes['objectSid'][0],0).Value -cne $owner.actorExternalId){throw 'DIRECTORY425_SID_MISMATCH'}
    $address=[string]$entry.Attributes['mail'][0];Assert-DirectoryMail425 $address ([int]$entry.Attributes['userAccountControl'][0]) $entry.Attributes['mail'].Count 1 $CorporateDomain
    $escaped=$address.Replace('\','\5c').Replace('*','\2a').Replace('(','\28').Replace(')','\29');$uniqueReq=[DirectoryServices.Protocols.SearchRequest]::new($base,'(&(objectCategory=person)(objectClass=user)(mail='+$escaped+'))',[DirectoryServices.Protocols.SearchScope]::Subtree,[string[]]@('objectSid'));$uniqueReq.SizeLimit=3;$unique=$ldap.SendRequest($uniqueReq)
    Assert-DirectoryMail425 $address ([int]$entry.Attributes['userAccountControl'][0]) $entry.Attributes['mail'].Count $unique.Entries.Count $CorporateDomain
    $contacts.Add([pscustomobject]@{employeeId=$owner.employeeId;ownerDisplayName=$owner.ownerDisplayName;recipient=$address;actorHash=(Get-Hash425 $owner.actorExternalId);recipientHash=(Get-Hash425 $address.ToLowerInvariant());verifiedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')})
   }catch{$reason='DIRECTORY425_LOOKUP_UNAVAILABLE';if($_.Exception.Message -match '(DIRECTORY425_[A-Z0-9_]+)'){$reason=$matches[1]};$excluded.Add([pscustomobject]@{employeeId=$owner.employeeId;reason=$reason})}
  }
 }catch{foreach($owner in $Source.owners){if($owner.employeeId -notin @($excluded|ForEach-Object employeeId)){$excluded.Add([pscustomobject]@{employeeId=$owner.employeeId;reason='DIRECTORY425_UNAVAILABLE'})}};$contacts.Clear()}finally{if($ldap){$ldap.Dispose()}}
 Complete-Directory425 $Source $contacts $excluded $bindSucceeded $rootDseSucceeded
}
function Complete-Directory425($Source,$contacts,$excluded,[bool]$bindSucceeded,[bool]$rootDseSucceeded){
 $batches=[Collections.Generic.List[object]]::new()
 $duplicates=@($contacts|Group-Object recipientHash|Where-Object Count -gt 1)
 foreach($d in $duplicates){foreach($c in $d.Group){$excluded.Add([pscustomobject]@{employeeId=$c.employeeId;reason='DIRECTORY425_SHARED_DESTINATION'})}}
 foreach($batch in $Source.batches){if($batch.employeeId -in @($excluded|ForEach-Object employeeId)){continue};$contact=@($contacts|Where-Object employeeId -eq $batch.employeeId);if($contact.Count -ne 1){$excluded.Add([pscustomobject]@{employeeId=$batch.employeeId;reason='DIRECTORY425_NO_CONTACT'});continue};$batches.Add([pscustomobject]@{employeeId=$batch.employeeId;recipient=$contact[0].recipient;ownerDisplayName=$batch.ownerDisplayName;asOfDate=$batch.asOfDate;rows=$batch.rows})}
 $blockedSelected=@($Source.batches|Where-Object {$_.employeeId -in @($excluded|ForEach-Object employeeId)}).Count
 [pscustomobject]@{schemaVersion=1;asOfUtc=$Source.asOfUtc;weekMonday=$Source.weekMonday;sourceReadStartedAtUtc=$Source.sourceReadStartedAtUtc;sourceReadCompletedAtUtc=$Source.sourceReadCompletedAtUtc;revalidatedAtUtc=[DateTimeOffset]::UtcNow.ToString('o');sourceEvidence=$Source.sourceEvidence;batches=$batches.ToArray();contacts=$contacts.ToArray();recipientExclusions=$excluded.ToArray();directoryHealthy=($bindSucceeded -and $rootDseSucceeded);lookupOwnerCount=$Source.owners.Count;resolvedOwnerCount=$contacts.Count;directoryHealth=[pscustomobject]@{bindSucceeded=$bindSucceeded;rootDseSucceeded=$rootDseSucceeded;sqlSelectedOwnerCount=$Source.batches.Count;resolvedSelectedOwnerCount=$batches.Count;blockedSelectedOwnerCount=$blockedSelected;ownerCount=$Source.owners.Count;contactCount=$contacts.Count;exclusionCount=$excluded.Count;reasonCounts=@($excluded|Group-Object reason|ForEach-Object{[pscustomobject]@{reason=$_.Name;count=$_.Count}});completeCoverage=($bindSucceeded -and $rootDseSucceeded -and $contacts.Count -eq $Source.owners.Count -and $excluded.Count -eq 0);healthyForSelectedOwners=($bindSucceeded -and $rootDseSucceeded -and $blockedSelected -eq 0 -and $batches.Count -eq $Source.batches.Count)}}
}
function Get-BmsWeeklyPrimary425Revalidation {
 [CmdletBinding()]param([Parameter(Mandatory)]$InitialBatch,[Parameter(Mandatory)][string]$SqlConnectionString,[DateTimeOffset]$AsOfUtc=[DateTimeOffset]::UtcNow,[ValidateSet('DirectQuery','StoredProcedure')][string]$SourceReadMode='DirectQuery',[string]$LdapServer='directory.example.invalid',[string]$CorporateDomain='example.invalid',[string]$ExpectedManifestSha256='1943688D3A6E6C33C42CC186862633D2FA0A349CF373A3266D3CA8CB02B22724')
 $started=[DateTimeOffset]::UtcNow;$source=Get-BmsWeeklyPrimary425Source -SqlConnectionString $SqlConnectionString -AsOfUtc $AsOfUtc -SourceReadMode $SourceReadMode -ExpectedManifestSha256 $ExpectedManifestSha256;$resolved=Resolve-BmsWeeklyPrimary425Recipients -Source $source -LdapServer $LdapServer -CorporateDomain $CorporateDomain
 $batch=@($resolved.batches|Where-Object employeeId -eq $InitialBatch.employeeId);$same=$false;$reason='OWNER_NO_LONGER_ELIGIBLE'
 if($batch.Count -eq 1){$same=($batch[0]|ConvertTo-Json -Depth 12 -Compress) -ceq ($InitialBatch|ConvertTo-Json -Depth 12 -Compress);$reason=if($same){'SAME'}else{'BATCH_CHANGED'}}
 [pscustomobject]@{eligible=($batch.Count -eq 1);same=$same;diffReason=$reason;batch=if($batch.Count -eq 1){$batch[0]}else{$null};asOfUtc=$resolved.asOfUtc;sourceReadStartedAtUtc=$resolved.sourceReadStartedAtUtc;sourceReadCompletedAtUtc=$resolved.sourceReadCompletedAtUtc;revalidatedAtUtc=$resolved.revalidatedAtUtc;durationMilliseconds=[math]::Round(([DateTimeOffset]::UtcNow-$started).TotalMilliseconds);sourceEvidence=$source.sourceEvidence;recipientExclusions=$resolved.recipientExclusions}
}
Export-ModuleMember -Function Get-BmsWeeklyPrimary425Source,Resolve-BmsWeeklyPrimary425Recipients,Get-BmsWeeklyPrimary425Revalidation
