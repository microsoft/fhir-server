<#
.SYNOPSIS
    Regenerates a test-shard manifest from recent per-test durations. Run manually; never from a pipeline.

.DESCRIPTION
    Reads the durations of the "[shadow]" E2E test runs of the latest -FromBuilds builds from the Azure DevOps
    test-results API (read-only GET requests), sums them per class (per method for -SplitClasses) in each build,
    takes the median across builds, and packs the units into -ShardCount shards, largest first, always into the
    least-loaded shard. The result is written as a manifest (see Get-TestShardFilter.ps1).

    The last shard is the catch-all `{}`, so its tests are the ones the manifest does not list. The class named by
    -CatchAllAnchor is therefore pinned to it, and units matching -KeepOutOfCatchAll (by default Import and BulkDelete
    tests) are never placed in it: History tests must not share a shard with import or bulk-delete tests, because those
    change the resources a history search sees.

    The Azure DevOps token comes from -Token, then $env:ADO_TOKEN, then `az account get-access-token`. It is only sent
    in the Authorization header and is never printed.

.PARAMETER ShardCount
    Number of shards, including the catch-all. At least 2.

.PARAMETER FromBuilds
    Number of recent builds (that have matching runs) whose durations are used.

.PARAMETER FromFile
    Offline input instead of the API: a JSON array of { buildId, automatedTestName, durationInMs }. For local testing.

.PARAMETER OutFile
    Manifest to write. Defaults to build/test-shards/<Suite>.json.

.PARAMETER DryRun
    Print the packing without writing the manifest.

.EXAMPLE
    pwsh build/scripts/Update-TestShards.ps1 -ShardCount 4 -FromBuilds 5 -DryRun
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateRange(2, 64)]
    [int] $ShardCount,

    [Parameter(Mandatory)]
    [ValidateRange(1, 100)]
    [int] $FromBuilds,

    [string] $Suite = 'sql-e2e',
    [string] $Organization = 'microsofthealthoss',
    [string] $Project = 'FhirServer',
    [int] $DefinitionId = 9,

    # Test run names look like "[shadow] <runName>"; only runs whose name also contains this text are used.
    [string] $RunNameContains = 'E2E',

    [string[]] $SplitClasses = @('Microsoft.Health.Fhir.Tests.E2E.Rest.Import.ImportTests', 'Microsoft.Health.Fhir.Tests.E2E.Rest.Reindex.ReindexTests'),
    [string] $CatchAllAnchor = 'Microsoft.Health.Fhir.Tests.E2E.Rest.HistoryTests',
    [string[]] $KeepOutOfCatchAll = @('\.Import\.', '\.BulkDeleteTests'),

    [string] $FromFile,
    [string] $OutFile,
    [string] $Token,

    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
if (-not $OutFile) {
    $OutFile = Join-Path $PSScriptRoot '..' 'test-shards' "$Suite.json"
}

function Get-AdoToken {
    if ($Token) { return $Token }
    if ($env:ADO_TOKEN) { return $env:ADO_TOKEN }
    # 499b84ac-... is the Azure DevOps resource id.
    $fromCli = & az account get-access-token --resource 499b84ac-1321-427f-aa17-267ca6975798 --query accessToken -o tsv
    if (-not $fromCli) { throw 'No Azure DevOps token: pass -Token, set ADO_TOKEN, or run az login.' }
    return $fromCli
}

function Invoke-Ado {
    param([string] $Path, [hashtable] $Headers)
    return Invoke-RestMethod -Uri "https://dev.azure.com/$Organization/$Project/_apis/$Path" -Headers $Headers -Method Get
}

# Returns { buildId; automatedTestName; durationInMs } rows for the latest builds that have matching shadow runs.
function Get-DurationRows {
    $headers = @{ Authorization = "Bearer $(Get-AdoToken)" }
    $builds = (Invoke-Ado "build/builds?definitions=$DefinitionId&statusFilter=completed&queryOrder=finishTimeDescending&`$top=100&api-version=7.1" $headers).value
    $rows = [System.Collections.Generic.List[object]]::new()
    $used = 0
    foreach ($build in $builds) {
        if ($used -ge $FromBuilds) { break }
        $runs = @((Invoke-Ado "test/runs?buildUri=vstfs:///Build/Build/$($build.id)&api-version=7.1" $headers).value |
            Where-Object { $_.name.StartsWith('[shadow]') -and $_.name.Contains($RunNameContains) })
        if ($runs.Count -eq 0) { continue }

        $used++
        foreach ($run in $runs) {
            $skip = 0
            do {
                $page = @((Invoke-Ado "test/runs/$($run.id)/results?`$top=1000&`$skip=$skip&api-version=7.1" $headers).value)
                foreach ($result in $page) {
                    $rows.Add([pscustomobject]@{ buildId = $build.id; automatedTestName = $result.automatedTestName; durationInMs = $result.durationInMs })
                }
                $skip += 1000
            } while ($page.Count -eq 1000)
        }
    }

    if ($used -eq 0) { throw "No builds of definition $DefinitionId have a [shadow] test run whose name contains '$RunNameContains'." }
    return $rows
}

function Get-Median {
    param([double[]] $Values)
    $sorted = $Values | Sort-Object
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

$rows = if ($FromFile) { @(Get-Content -Path $FromFile -Raw | ConvertFrom-Json) } else { Get-DurationRows }

# Unit = class, or class.method for classes that are split across shards. Sum per build, then median across builds.
$perBuild = @{}
foreach ($row in $rows) {
    $name = [string] $row.automatedTestName
    $dot = $name.LastIndexOf('.')
    if ($dot -lt 1) { continue }
    $class = $name.Substring(0, $dot)
    $unit = if ($SplitClasses -contains $class) { $name } else { $class }
    $perBuild["$unit|$($row.buildId)"] = [double] $perBuild["$unit|$($row.buildId)"] + [double] $row.durationInMs
}

$byUnit = @{}
foreach ($key in $perBuild.Keys) {
    $unit = $key.Substring(0, $key.LastIndexOf('|'))
    if (-not $byUnit.ContainsKey($unit)) { $byUnit[$unit] = [System.Collections.Generic.List[double]]::new() }
    $byUnit[$unit].Add($perBuild[$key])
}

$units = @($byUnit.Keys | ForEach-Object { [pscustomobject]@{ Name = $_; Ms = (Get-Median $byUnit[$_].ToArray()) } })
if ($units.Count -eq 0) { throw 'No test durations were found.' }

# Pack largest first; ties by name keep the result deterministic.
$lastBin = $ShardCount - 1
$bins = @(0..$lastBin | ForEach-Object { [pscustomobject]@{ Load = 0.0; Units = [System.Collections.Generic.List[object]]::new() } })
$anchor = $units | Where-Object { $_.Name -eq $CatchAllAnchor }
if ($anchor) {
    $bins[$lastBin].Units.Add($anchor)
    $bins[$lastBin].Load += $anchor.Ms
}

foreach ($unit in ($units | Where-Object { $_.Name -ne $CatchAllAnchor } | Sort-Object @{ Expression = 'Ms'; Descending = $true }, Name)) {
    $keepOut = @($KeepOutOfCatchAll | Where-Object { $unit.Name -match $_ }).Count -gt 0
    $candidates = if ($keepOut) { 0..($lastBin - 1) } else { 0..$lastBin }
    $target = $candidates | Sort-Object { $bins[$_].Load }, { $_ } | Select-Object -First 1
    $bins[$target].Units.Add($unit)
    $bins[$target].Load += $unit.Ms
}

$shards = for ($i = 0; $i -lt $lastBin; $i++) {
    $names = @($bins[$i].Units | ForEach-Object Name | Sort-Object)
    $classes = @($names | Where-Object { $SplitClasses -notcontains ($_.Substring(0, [Math]::Max($_.LastIndexOf('.'), 0))) })
    $methods = @($names | Where-Object { $classes -notcontains $_ })
    $shard = [ordered]@{}
    if ($classes.Count -gt 0) { $shard.classes = $classes }
    if ($methods.Count -gt 0) { $shard.methods = $methods }
    $shard
}

$manifest = [ordered]@{ suite = $Suite; shards = @($shards) + @([ordered]@{}) }
for ($i = 0; $i -le $lastBin; $i++) {
    $suffix = if ($i -eq $lastBin) { ' (catch-all)' } else { '' }
    Write-Host ("Shard {0}{1}: {2,7:N0} s, {3} unit(s)" -f $i, $suffix, ($bins[$i].Load / 1000), $bins[$i].Units.Count)
}

if ($DryRun) {
    Write-Host "Dry run: not writing $OutFile"
}
else {
    ($manifest | ConvertTo-Json -Depth 6) + "`n" | Set-Content -Path $OutFile -NoNewline
    Write-Host "Wrote $OutFile"
}
