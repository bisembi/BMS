#requires -Version 7.0
[CmdletBinding()]
param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $RepositoryRoot).Path.TrimEnd('\')
$manifestPath=Join-Path $root 'source-manifest.json'
if(!(Test-Path -LiteralPath $manifestPath -PathType Leaf)){throw 'Source manifest is missing.'}
$manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
if($manifest.schemaVersion -ne 1 -or $manifest.snapshot -cne 'catalog485' -or @($manifest.files).Count -lt 100){throw 'Unexpected source manifest.'}
$expected=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($record in $manifest.files){
 $relative=[string]$record.path
 if($relative -notmatch '^[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)*$' -or @($relative.Split('/')|Where-Object {$_ -in '.','..'}).Count -gt 0 -or !$expected.Add($relative)){throw 'Unsafe or repeated manifest path.'}
 $path=[IO.Path]::GetFullPath((Join-Path $root $relative))
 if(!$path.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Manifest path escaped its root.'}
 if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing source file: $relative"}
 $item=Get-Item -LiteralPath $path
 if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Reparse source file: $relative"}
 if($item.Length -ne $record.bytes -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $record.sha256){throw "Source hash mismatch: $relative"}
}
$actual=0
foreach($item in Get-ChildItem -LiteralPath $root -Recurse -Force){
 $relative=$item.FullName.Substring($root.Length+1).Replace('\','/')
 if($relative -eq '.git' -or $relative.StartsWith('.git/')){continue}
 if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Reparse item in source package: $relative"}
 if($item.PSIsContainer -or $relative -eq 'source-manifest.json'){continue}
 if(!$expected.Contains($relative)){throw "Unexpected source file: $relative"}
 $actual++
}
if($actual -ne $expected.Count){throw 'Source file count mismatch.'}
[ordered]@{status='SOURCE_PACKAGE_HASHES_PASS';snapshot=$manifest.snapshot;verifiedFiles=$actual;publicationAuthorized=$manifest.publicationAuthorized;readyForProduction=$manifest.readyForProduction}|ConvertTo-Json
