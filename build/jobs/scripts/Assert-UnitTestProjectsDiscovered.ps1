<#
.SYNOPSIS
    Fails when the unit test leg's project glob matches fewer projects than expected.

.DESCRIPTION
    The unit test leg does not name its projects: it runs whatever '**/*UnitTests/*.csproj'
    matches. Every other leg guards against running a fraction of its tests by counting the tests
    a filter discovers, but that shape does not fit here, because this leg's failure is a whole
    project leaving the glob - renamed, moved, or a directory suffix quietly changed - rather than
    a filter selecting too little. Nothing notices: the remaining projects run, every one of them
    passes, and the leg reports success with an entire assembly's tests missing.

    --minimum-expected-tests cannot cover this either. It is applied per assembly, so it says
    nothing about an assembly that was never run, and it has to stay at 1 because the runner
    re-applies it to the much smaller pass that --retry-failed-tests starts.

    Each expected project is checked by path rather than by counting them, because a count only
    notices projects leaving while none arrive: renaming one project and adding another in the same
    change leaves the count where it was, and the renamed project stops being tested silently.

    Adding projects is fine; only losing one that is listed fails.

.PARAMETER SourcesDirectory
    The repository root to search.

.PARAMETER ManifestPath
    The file listing the repository-relative path of every project this leg is expected to run.
#>
#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $SourcesDirectory,

    [Parameter(Mandatory = $true)]
    [string] $ManifestPath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $SourcesDirectory)) {
    throw "Sources directory '$SourcesDirectory' does not exist."
}

if (-not (Test-Path -LiteralPath $ManifestPath)) {
    throw "The expected project list '$ManifestPath' does not exist, so there is nothing to check the leg's projects against."
}

$expected = @(
    Get-Content -LiteralPath $ManifestPath |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith('#') })

if ($expected.Count -eq 0) {
    throw "The expected project list '$ManifestPath' names no projects, so it would accept a leg that ran nothing."
}

$root = (Resolve-Path -LiteralPath $SourcesDirectory).Path.TrimEnd([System.IO.Path]::DirectorySeparatorChar)

# Mirrors the 'projects' pattern of the leg's test task: any .csproj in a directory whose name
# ends in UnitTests.
$matched = @(
    Get-ChildItem -LiteralPath $root -Recurse -Filter '*.csproj' -File |
        Where-Object { $_.Directory.Name -like '*UnitTests' } |
        ForEach-Object { $_.FullName.Substring($root.Length + 1).Replace([System.IO.Path]::DirectorySeparatorChar, '/') } |
        Sort-Object)

# Paths are compared without regard to case because the repository is developed on Windows and
# built on Linux, and a case-only difference is not a project leaving the leg.
$comparer = [System.StringComparer]::OrdinalIgnoreCase
$matchedSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$matched, $comparer)
$expectedSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$expected, $comparer)

Write-Host "Matched $($matched.Count) unit test project(s) against $($expected.Count) expected:"
foreach ($project in $matched) {
    Write-Host "  $project"
}

$missing = @($expected | Where-Object { -not $matchedSet.Contains($_) })

if ($missing.Count -gt 0) {
    $detail = ($missing | ForEach-Object { "  $_" }) -join [Environment]::NewLine
    throw @"
These projects are expected to run in the unit test leg but no longer match its project glob:
$detail

The leg runs whatever the glob matches, so it would have run without them and reported success. If
a project was renamed or moved on purpose, update '$ManifestPath' to say so.
"@
}

$unlisted = @($matched | Where-Object { -not $expectedSet.Contains($_) })

if ($unlisted.Count -gt 0) {
    # An unlisted project still runs. Naming it here is what keeps the list from drifting so far
    # behind that a later loss is hard to tell from a rename.
    Write-Host "These projects run in this leg but are not listed in '$ManifestPath':"
    foreach ($project in $unlisted) {
        Write-Host "  $project"
    }
}
