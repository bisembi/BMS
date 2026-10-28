# Pure coverage classification. No I/O, SQL, AD, secrets or SMTP.
function Get-WeeklyCoverage425 {
 param([Parameter(Mandatory)]$Source,[Parameter(Mandatory)]$Resolved)
 Set-StrictMode -Version Latest
 $healthy=$Resolved.directoryHealthy -eq $true
 $candidates=@($Source.batches|ForEach-Object {([Guid]$_.employeeId).ToString('D')})
 $recipientBlocked=@($Resolved.recipientExclusions|Where-Object {([Guid]$_.employeeId).ToString('D') -in $candidates})
 $heldOwners=@($Source.sourceEvidence.excludedOwners|Where-Object {$_.reason -cne 'INACTIVE_OWNER_ROLE_OR_ASSIGNMENT'})
 $heldRequests=@($Source.sourceEvidence.excludedRequests|Where-Object {
  $reason=[string]$_.reason
  if($reason -cin @('EXCLUDED_OPERATION_Tax','EXCLUDED_OPERATION_PayrollAdvance')){$false}
  else{@($reason.Split('+')|Where-Object {$_ -cnotin @('EXCLUDE_GOODS','EXCLUDE_TAX_SOCIAL')}).Count -gt 0}
 })
 $reasons=@{}
 foreach($e in @($Resolved.recipientExclusions)+@($heldRequests)+@($heldOwners)){
  $reason=if([string]$e.reason -cmatch '^[A-Za-z0-9_+]{1,120}$'){[string]$e.reason}else{'UNKNOWN_REASON'}
  if(!$reasons.ContainsKey($reason)){$reasons[$reason]=0};$reasons[$reason]++
 }
 $resolvedIds=@($Resolved.batches|ForEach-Object {([Guid]$_.employeeId).ToString('D')})
 $missing=@($candidates|Where-Object {$_ -notin $resolvedIds}).Count
 $unexpected=@($resolvedIds|Where-Object {$_ -notin $candidates}).Count
 $metrics=[pscustomobject]@{sourceSelectedOwnerCount=$candidates.Count;sourceEligibleRequestCount=[int]$Source.sourceEvidence.eligibleRequestCount;recipientBlockedCount=$recipientBlocked.Count;recipientExclusionCountAll=@($Resolved.recipientExclusions).Count;sourceHeldOwnerCount=$heldOwners.Count;sourceHeldRequestCount=$heldRequests.Count;sourceUnexpectedResolvedOwnerCount=$unexpected;sourceCoverageMissing=($missing+$unexpected+$heldOwners.Count+$heldRequests.Count);sourceReasonCounts=$reasons;sourceCompleteGlobalRead=[bool]$Source.sourceEvidence.completeGlobalRead}
 [pscustomobject]@{AdResolution=$healthy;Blocked=(!$healthy -or $metrics.sourceCoverageMissing -ne 0 -or !$metrics.sourceCompleteGlobalRead);Metrics=$metrics}
}
