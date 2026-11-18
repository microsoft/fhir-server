<#
.SYNOPSIS
    Waits until a pipeline artifact of the current run has been published.

.DESCRIPTION
    Lets a job start, and claim its agent, before the job that builds its test binaries has finished, then wait for
    the artifact the build publishes. Polls until the artifact exists, retrying when a poll fails (a transient service
    error must not fail the leg; only the timeout does).

    The default GetArtifact asks the Azure DevOps build API for the artifact of the running build, with
    SYSTEM_ACCESSTOKEN (map $(System.AccessToken) to it in the step's env:); the token is never printed.

    Throws when the artifact is not published within TimeoutMinutes. For an artifact named tier1-<version>-tests the
    message names the job Tier1Build_<version>, which publishes it.

.PARAMETER ArtifactName
    Name of the pipeline artifact, for example tier1-R4-tests.

.PARAMETER TimeoutMinutes
    How long to wait for the artifact.

.PARAMETER PollSeconds
    Pause between polls.

.PARAMETER GetArtifact
    Scriptblock called with the artifact name; returns $true when the artifact exists. Replaced by tests so they need
    no network.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ArtifactName,

    [double] $TimeoutMinutes = 20,

    [int] $PollSeconds = 10,

    [scriptblock] $GetArtifact = {
        param([string] $Name)

        $uri = '{0}{1}/_apis/build/builds/{2}/artifacts?artifactName={3}&api-version=7.1' -f `
            $env:SYSTEM_COLLECTIONURI, $env:SYSTEM_TEAMPROJECT, $env:BUILD_BUILDID, [uri]::EscapeDataString($Name)
        try {
            $null = Invoke-RestMethod -Uri $uri -Headers @{ Authorization = "Bearer $env:SYSTEM_ACCESSTOKEN" } -ErrorAction Stop
            return $true
        }
        catch {
            # The API answers 404 until the artifact is published.
            if ($_.Exception.Response -and [int] $_.Exception.Response.StatusCode -eq 404) { return $false }
            throw
        }
    }
)

$ErrorActionPreference = 'Stop'

$deadline = [datetime]::UtcNow.AddMinutes($TimeoutMinutes)
$started = [datetime]::UtcNow
$lastError = $null
do {
    try {
        if (& $GetArtifact $ArtifactName) {
            $waited = [math]::Round(([datetime]::UtcNow - $started).TotalSeconds)
            Write-Host "Artifact $ArtifactName is published (waited ${waited}s)."
            return
        }
    }
    catch {
        # Logged once per distinct error, so a long outage does not flood the log.
        if ($_.Exception.Message -ne $lastError) {
            $lastError = $_.Exception.Message
            Write-Host "Could not query artifact ${ArtifactName}: $lastError; retrying."
        }
    }

    Start-Sleep -Seconds $PollSeconds
} while ([datetime]::UtcNow -lt $deadline)

$job = if ($ArtifactName -match '^tier1-(.+)-tests$') { "Tier1Build_$($Matches[1])" } else { 'build' }
throw "Artifact $ArtifactName was not published within $TimeoutMinutes minutes; check the $job job."
