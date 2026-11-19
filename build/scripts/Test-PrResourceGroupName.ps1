<#
.SYNOPSIS
    Tells whether a resource group belongs to a PR: <root>-<pr>, or <root>-<pr>-<suffix> (the per-build groups).

.DESCRIPTION
    Used by the cleanup of a PR's old resource groups (build/templates/steps/delete-resource-groups.yml). The previous
    prefix pattern, <root>-<pr>*, also selected the groups of every PR whose number starts with the same digits (PR 6
    deleted the groups of PRs 60-69 and 600-699); the delimiter after the number excludes them. Names compare
    case-insensitively, as Azure resource group names do.

.PARAMETER Name
    The resource group name.

.PARAMETER ResourceGroupRoot
    The root of PR resource group names (resourceGroupRoot, msh-fhir-pr).

.PARAMETER PrNumber
    The PR number (prName). Throws when it is not a number: a wildcard or other text would widen the match to other
    PRs' groups.

.OUTPUTS
    [bool]
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Name,
    [Parameter(Mandatory)] [string] $ResourceGroupRoot,
    [Parameter(Mandatory)] [string] $PrNumber
)

if ($PrNumber -notmatch '^\d+$') {
    throw "PR number '$PrNumber' is not a number."
}

$prefix = "$ResourceGroupRoot-$PrNumber"
$Name -eq $prefix -or $Name.StartsWith("$prefix-", [StringComparison]::OrdinalIgnoreCase)
