<#
.SYNOPSIS
    Tells whether a pipeline run deployed the stages whose resources the PR cleanup pipeline deletes.

.DESCRIPTION
    The PR cleanup pipeline is triggered by every completed "PR Build & Deploy" run, but only runs that deployed
    (the legacy validation mode) own the Azure resources it deletes. A tier1 run has none of those stages, yet its
    build number, which the cleanup uses as the image tag and Key Vault name, is shared with a concurrent legacy
    run of the same commit, so cleaning up after it deletes that run's image.

    Returns $true only when every stage in StageNames exists in the run's timeline and succeeded (or succeeded with
    issues). Returns $false otherwise, so the caller skips cleanup.

    Fails closed: throws when the timeline cannot be read, so nothing is deleted and the error is visible. The
    scheduled sweeper of old PR resources is the backstop for a skipped cleanup.

    The default GetTimeline reads the run's timeline from the Azure DevOps build API with SYSTEM_ACCESSTOKEN (map
    $(System.AccessToken) to it in the step's env:); the token is never printed. Stages are matched on the
    timeline's identifier, which is the stage name in the YAML; the name is its display name.

.PARAMETER RunId
    Id of the triggering run.

.PARAMETER StageNames
    Stage names (identifiers) that must all have succeeded.

.PARAMETER GetTimeline
    Scriptblock called with the run id; returns the timeline object (with a records array). Replaced by tests so
    they need no network.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [int] $RunId,

    [Parameter(Mandatory)]
    [string[]] $StageNames,

    [scriptblock] $GetTimeline = {
        param([int] $Id)

        $uri = '{0}{1}/_apis/build/builds/{2}/timeline?api-version=7.1' -f `
            $env:SYSTEM_COLLECTIONURI, $env:SYSTEM_TEAMPROJECT, $Id
        Invoke-RestMethod -Uri $uri -Headers @{ Authorization = "Bearer $env:SYSTEM_ACCESSTOKEN" } -ErrorAction Stop
    }
)

$ErrorActionPreference = 'Stop'

try {
    $timeline = & $GetTimeline $RunId
    if ($null -eq $timeline -or $null -eq $timeline.PSObject.Properties['records']) {
        throw 'the response has no records'
    }
}
catch {
    throw "Could not read the timeline of run ${RunId}: $($_.Exception.Message)"
}

$succeeded = @{}
foreach ($record in @($timeline.records)) {
    if ($record.type -eq 'Stage' -and $record.result -in 'succeeded', 'succeededWithIssues') {
        $succeeded[$record.identifier] = $true
    }
}

$missing = @($StageNames | Where-Object { -not $succeeded.ContainsKey($_) })
if ($missing.Count -gt 0) {
    Write-Host "Run $RunId did not deploy or did not succeed in: $($missing -join ', ')"
}

$missing.Count -eq 0
