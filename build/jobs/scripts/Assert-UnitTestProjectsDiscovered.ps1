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

    Adding projects is fine; only losing them fails.

.PARAMETER SourcesDirectory
    The repository root to search.

.PARAMETER MinimumExpectedProjects
    The floor the match count must clear.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $SourcesDirectory,

    [Parameter(Mandatory = $true)]
    [int] $MinimumExpectedProjects
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $SourcesDirectory)) {
    throw "Sources directory '$SourcesDirectory' does not exist."
}

# Mirrors the 'projects' pattern of the leg's test task: any .csproj in a directory whose name
# ends in UnitTests.
$projects = Get-ChildItem -LiteralPath $SourcesDirectory -Recurse -Filter '*.csproj' -File |
    Where-Object { $_.Directory.Name -like '*UnitTests' } |
    Sort-Object -Property FullName

Write-Host "Matched $($projects.Count) unit test project(s) (floor $MinimumExpectedProjects):"
foreach ($project in $projects) {
    Write-Host "  $($project.Directory.Name)"
}

if ($projects.Count -lt $MinimumExpectedProjects) {
    throw "The unit test project glob matched $($projects.Count) project(s), below this leg's floor of $MinimumExpectedProjects. The leg would have run a fraction of its unit tests and reported success."
}
