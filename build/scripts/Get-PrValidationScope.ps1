<#
.SYNOPSIS
    Classifies a PR validation run as lean (Tier 1 only) or full (the deployed test matrix).

.DESCRIPTION
    Rules, first match wins (docs/arch/adr-2611-tiered-pr-validation.md, iteration 1):
      1. A manual run with ManualOverride lean or full takes it.
      2. A run that is not a pull request build is full.
      3. A fork PR is lean, with a warning: the full matrix needs the deployed environments and their secrets.
      4. A PR whose draft state is unknown (the GitHub API could not be read) is full.
      5. A PR that is ready for review is full.
      6. A draft PR labeled Full-Test-Matrix (any case) is full.
      7. Any other draft PR is lean.
    Unknown inputs fail closed to full, except for a fork PR, which never runs the full matrix.
    Returns [pscustomobject] @{ Scope = 'lean' | 'full'; Reasons = string[]; Warning = string or '' }.

.PARAMETER BuildReason
    Build.Reason of the run.

.PARAMETER ManualOverride
    tier1Scope pipeline parameter; honored only when BuildReason is Manual.

.PARAMETER IsDraft
    The PR's draft state; $null when it could not be read.

.PARAMETER LabelsUnavailableReason
    Why the draft state and labels could not be read; reported in the reason.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $BuildReason,

    [ValidateSet('auto', 'lean', 'full')]
    [string] $ManualOverride = 'auto',

    [bool] $IsFork,

    [Nullable[bool]] $IsDraft,

    [string[]] $Labels = @(),

    [string] $LabelsUnavailableReason = ''
)

$ErrorActionPreference = 'Stop'

function New-Scope([string] $Scope, [string] $Reason, [string] $Warning = '') {
    [pscustomobject]@{ Scope = $Scope; Reasons = @($Reason); Warning = $Warning }
}

if ($BuildReason -eq 'Manual' -and $ManualOverride -ne 'auto') {
    return New-Scope $ManualOverride "override: tier1Scope=$ManualOverride (manual run)"
}

if ($ManualOverride -ne 'auto') {
    Write-Host "tier1Scope=$ManualOverride ignored: only manual runs take an override."
}

if ($BuildReason -ne 'PullRequest') {
    return New-Scope 'full' "not a pull request (Build.Reason=$BuildReason)"
}

if ($IsFork) {
    return New-Scope 'lean' 'fork PR: Tier 1 only' 'Fork PR: the full test matrix does not run for fork PRs. A maintainer can push the reviewed SHA to a branch in microsoft/fhir-server to run it.'
}

if ($null -eq $IsDraft) {
    $detail = if ($LabelsUnavailableReason) { ": $LabelsUnavailableReason" } else { '' }
    return New-Scope 'full' "fail-closed: draft state unknown$detail"
}

if (-not $IsDraft) {
    return New-Scope 'full' 'ready for review'
}

# -contains is case-insensitive.
if ($Labels -contains 'Full-Test-Matrix') {
    return New-Scope 'full' 'draft labeled Full-Test-Matrix'
}

New-Scope 'lean' 'draft PR'
