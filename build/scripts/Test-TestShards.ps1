<#
.SYNOPSIS
    Checks that a test-shard manifest still partitions a test application's tests exactly.

.DESCRIPTION
    Lists the tests of a Microsoft.Testing.Platform application with --list-tests, once with only the common
    trait filters and once per shard (Get-TestShardFilter.ps1 arguments plus the same trait filters), then checks:
      * union equality: the shards together list exactly the tests of the unfiltered run (as a multiset);
      * pairwise disjointness: no test is listed by two shards;
      * no shard is empty.

    Never throws for a lint finding or for an unusable manifest or test application; those become entries in
    Problems. With -Pipeline each problem is also written as a ##vso[task.logissue type=warning] message and the
    output variable ShardLintFailed is set (true on any problem, otherwise false), so a lint job warns but never fails.

.PARAMETER TestExe
    Path of the test application: a .dll (run with dotnet) or an executable.

.PARAMETER Manifest
    Path of the shard manifest JSON file.

.PARAMETER TraitArgs
    Trait filter arguments applied to every listing, for example --filter-trait DataStore=SqlServer.

.PARAMETER Dotnet
    dotnet host used to run a .dll test application.

.PARAMETER Label
    Text added to every problem and warning, for example the FHIR version.

.PARAMETER Pipeline
    Emit Azure Pipelines logging commands.

.OUTPUTS
    PSCustomObject: Passed, UnfilteredCount, Shards (Index, Count), Problems.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $TestExe,

    [Parameter(Mandatory)]
    [string] $Manifest,

    [string[]] $TraitArgs = @(),

    [string] $Dotnet = 'dotnet',

    [string] $Label = '',

    [switch] $Pipeline
)

$ErrorActionPreference = 'Stop'
$getFilter = Join-Path $PSScriptRoot 'Get-TestShardFilter.ps1'

# Returns the listed test display names. --list-tests exits with 8 ("zero tests ran") when a filter matches nothing.
function Get-ListedTests {
    param([string[]] $FilterArgs)

    [string[]] $command = if ($TestExe -like '*.dll') { $Dotnet, $TestExe } else { $TestExe }
    $output = & $command[0] @($command | Select-Object -Skip 1) '--list-tests' @FilterArgs 2>&1 | ForEach-Object { "$_" }
    $exitCode = $LASTEXITCODE
    if ($exitCode -notin 0, 8) {
        throw "'$($command -join ' ') --list-tests $($FilterArgs -join ' ')' exited with code $exitCode."
    }

    # Test lines are indented two spaces; the discovery summary prints its own indented duration line.
    return @($output | Where-Object { $_ -match '^  \S' -and $_ -notmatch '^  duration:' })
}

$problems = [System.Collections.Generic.List[string]]::new()
$shardResults = [System.Collections.Generic.List[object]]::new()
$unfiltered = @()

try {
    $shardCount = @((Get-Content -Path $Manifest -Raw | ConvertFrom-Json).shards).Count
    $unfiltered = Get-ListedTests $TraitArgs
    if ($unfiltered.Count -eq 0) {
        $problems.Add('The unfiltered run lists no tests, so the shards cannot be checked.')
    }

    $listings = @()
    for ($index = 0; $index -lt $shardCount; $index++) {
        $shardArgs = @(& $getFilter -Manifest $Manifest -ShardIndex $index)
        $tests = Get-ListedTests ($shardArgs + $TraitArgs)
        $listings += , $tests
        $shardResults.Add([pscustomobject]@{ Index = $index; Count = $tests.Count })
        if ($tests.Count -eq 0) {
            $problems.Add("Shard $index lists no tests.")
        }
    }

    for ($a = 0; $a -lt $listings.Count; $a++) {
        for ($b = $a + 1; $b -lt $listings.Count; $b++) {
            $overlap = @($listings[$a] | Where-Object { $listings[$b] -contains $_ } | Select-Object -Unique)
            if ($overlap.Count -gt 0) {
                $problems.Add("Shards $a and $b both list $($overlap.Count) test(s), for example: $($overlap[0].Trim())")
            }
        }
    }

    $union = @($listings | ForEach-Object { $_ })
    if ($union.Count -ne $unfiltered.Count -or @(Compare-Object ($union | Sort-Object) ($unfiltered | Sort-Object)).Count -gt 0) {
        $difference = @(Compare-Object ($union | Sort-Object) ($unfiltered | Sort-Object))
        $missing = @($difference | Where-Object SideIndicator -eq '=>')
        $extra = @($difference | Where-Object SideIndicator -eq '<=')
        $example = @($missing + $extra | Select-Object -First 1 -ExpandProperty InputObject)
        $problems.Add("The shards list $($union.Count) test(s) but the unfiltered run lists $($unfiltered.Count) ($($missing.Count) missing from shards, $($extra.Count) extra). Example: $($example -join '')".TrimEnd())
    }
}
catch {
    $problems.Add("Shard lint could not complete: $($_.Exception.Message)")
}

$result = [pscustomobject]@{
    Passed          = $problems.Count -eq 0
    UnfilteredCount = $unfiltered.Count
    Shards          = $shardResults.ToArray()
    Problems        = $problems.ToArray()
}

if ($Pipeline) {
    $prefix = if ($Label) { "Shard lint ($Label): " } else { 'Shard lint: ' }
    foreach ($problem in $problems) {
        Write-Host "##vso[task.logissue type=warning]$prefix$problem"
    }

    Write-Host "##vso[task.setvariable variable=ShardLintFailed;isOutput=true]$((-not $result.Passed).ToString().ToLowerInvariant())"
}

return $result
