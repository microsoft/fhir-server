<#
.SYNOPSIS
    Replays the PR scope classifier (Get-PrValidationScope.ps1) over the first-parent history of a branch.

.DESCRIPTION
    Manual tool, not run by Pester. Each first-parent commit of -Branch since -Since is treated as one merged PR
    (squash merges): its changed files are `git diff --name-only <commit>^1 <commit>`. Labels are not known for
    past PRs, so the full-matrix label never counts; the target branch is main. Prints the scope distribution and the
    paths that most often made a commit full.

.EXAMPLE
    git fetch origin main
    pwsh build/scripts/tests/Replay-PrValidationScope.ps1 -Since 2026-07-01
#>
[CmdletBinding()]
param(
    [string] $Branch = 'origin/main',
    [string] $Since = '2026-07-01',
    [int] $Top = 5
)

$ErrorActionPreference = 'Stop'
$classifier = Join-Path $PSScriptRoot '..' 'Get-PrValidationScope.ps1'

$commits = @(git log --first-parent --since=$Since --format='%H %s' $Branch)
if ($LASTEXITCODE -ne 0) { throw "git log failed for $Branch" }

$results = foreach ($line in $commits) {
    $sha, $subject = $line -split ' ', 2
    $files = @(git diff --name-only "$sha^1" $sha)
    if ($LASTEXITCODE -ne 0) { throw "git diff failed for $sha" }

    $result = & $classifier -ChangedFiles $files -TargetBranch 'main'
    [pscustomobject]@{ Sha = $sha.Substring(0, 9); Subject = $subject; Scope = $result.Scope; Reasons = $result.Reasons }
}

$total = @($results).Count
Write-Host "Commits: $total (first-parent of $Branch since $Since)"
foreach ($scope in 'lean', 'shared', 'full') {
    $count = @($results | Where-Object Scope -eq $scope).Count
    Write-Host ('{0,-7} {1,4}  {2,5:P1}' -f $scope, $count, ($(if ($total) { $count / $total } else { 0 })))
}

# The rule that triggers full, per commit (a commit counts once per rule).
Write-Host "`nTop $Top full triggers (commits per rule):"
$results | Where-Object Scope -eq 'full' |
    ForEach-Object { $_.Reasons | ForEach-Object { ($_ -split ':', 2)[0] } | Sort-Object -Unique } |
    Group-Object | Sort-Object Count -Descending | Select-Object -First $Top |
    ForEach-Object { Write-Host ('{0,4}  {1}' -f $_.Count, $_.Name) }

Write-Host "`nTop $Top full-triggering paths (commits per path):"
$results | Where-Object Scope -eq 'full' |
    ForEach-Object { $_.Reasons | ForEach-Object { ($_ -split ': ', 2)[-1] } | Sort-Object -Unique } |
    Group-Object | Sort-Object Count -Descending | Select-Object -First $Top |
    ForEach-Object { Write-Host ('{0,4}  {1}' -f $_.Count, $_.Name) }

$results
