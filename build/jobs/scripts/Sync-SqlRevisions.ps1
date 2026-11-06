<#
.SYNOPSIS
Converges, resolves or settles the labeled revisions of one SQL container app.

.DESCRIPTION
Converge without -Image creates one build-and-attempt-scoped labeled revision per catalog profile
and returns the captured bindings plus the runtime matrix. Converge with -Image deploys a new
Standard64 baseline from the routed revision. Resolve reads one selected revision without writing.
Settle moves the baseline to a zero-minimum successor and retires this build's test revisions.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Converge', 'Resolve', 'Settle')][string]$Goal,
    [Parameter(Mandatory)][string]$AppResourceId,
    [Parameter(Mandatory)][securestring]$AccessToken,
    [string]$CatalogPath,
    [string]$BuildId,
    [string]$Attempt,
    [string]$Image,
    [System.Collections.IDictionary]$Environment,
    [string[]]$RemoveEnvironment,
    [int]$MinReplicas,
    [string]$RevisionProfile,
    [string]$Binding,
    [switch]$Expect
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'RevisionProfiles.psm1')

$goalParameters = @{}
foreach ($name in @('Goal', 'AppResourceId', 'AccessToken', 'BuildId', 'Attempt', 'Image', 'Environment', 'RemoveEnvironment', 'MinReplicas', 'RevisionProfile', 'Expect')) {
    if ($PSBoundParameters.ContainsKey($name)) {
        $goalParameters[$name] = $PSBoundParameters[$name]
    }
}

if ($PSBoundParameters.ContainsKey('CatalogPath')) {
    $goalParameters['Catalog'] = Get-RevisionCatalog -Path $CatalogPath
}

if ($PSBoundParameters.ContainsKey('Binding')) {
    $goalParameters['Binding'] = ConvertFrom-Json -InputObject $Binding -AsHashtable
}

Invoke-RevisionGoal @goalParameters
