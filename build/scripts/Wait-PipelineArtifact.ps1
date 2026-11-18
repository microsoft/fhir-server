<#
.SYNOPSIS
    Waits until a pipeline artifact of the current run has been published.

.DESCRIPTION
    Lets a job start, and claim its agent, before the job that builds its test binaries has finished, then wait for
    the artifact the build publishes. Polls until the artifact exists.

    Fails fast instead of waiting out the timeout when waiting cannot succeed:
      - the job that publishes the artifact finished without success (failed, canceled, skipped);
      - the build API refuses the job's access token (HTTP 401/403, a permission problem) or answers with another
        client error (4xx other than 429).
    Transient errors (HTTP 5xx, 429, network failures) are retried until the timeout.

    The default GetArtifact and GetBuildJobResult ask the Azure DevOps build API about the running build with
    SYSTEM_ACCESSTOKEN (map $(System.AccessToken) to it in the step's env:); the token is never printed.

    Throws when the artifact is not published within TimeoutMinutes. For an artifact named tier1-<version>-tests the
    messages name the job Tier1Build_<version>, which publishes it.

.PARAMETER ArtifactName
    Name of the pipeline artifact, for example tier1-R4-tests.

.PARAMETER TimeoutMinutes
    How long to wait for the artifact.

.PARAMETER PollSeconds
    Pause between polls.

.PARAMETER JobName
    Name of the job (in its stage) that publishes the artifact. Defaults to Tier1Build_<version> for an artifact named
    tier1-<version>-tests; with neither, the job is not checked.

.PARAMETER GetArtifact
    Scriptblock called with the artifact name; returns $true when the artifact exists. Replaced by tests so they need
    no network.

.PARAMETER GetBuildJobResult
    Scriptblock called with the job name; returns the job's result (succeeded, succeededWithIssues, failed, canceled,
    skipped) once it has completed, and $null while it is unfinished or unknown. Replaced by tests.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ArtifactName,

    [double] $TimeoutMinutes = 20,

    [int] $PollSeconds = 10,

    [string] $JobName = $(if ($ArtifactName -match '^tier1-(.+)-tests$') { "Tier1Build_$($Matches[1])" }),

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
            if ((Get-HttpStatusCode $_.Exception) -eq 404) { return $false }
            throw
        }
    },

    [scriptblock] $GetBuildJobResult = {
        param([string] $Name)

        $uri = '{0}{1}/_apis/build/builds/{2}/timeline?api-version=7.1' -f `
            $env:SYSTEM_COLLECTIONURI, $env:SYSTEM_TEAMPROJECT, $env:BUILD_BUILDID
        $timeline = Invoke-RestMethod -Uri $uri -Headers @{ Authorization = "Bearer $env:SYSTEM_ACCESSTOKEN" } -ErrorAction Stop

        # A job record's name is its display name; its identifier is <stage>.<job>.<configuration>.
        $job = @($timeline.records | Where-Object { $_.type -eq 'Job' -and $_.identifier -like "*.$Name.*" }) | Select-Object -First 1
        if ($job -and $job.state -eq 'completed') { return $job.result }
    }
)

$ErrorActionPreference = 'Stop'

# The HTTP status of a failed web request, or $null for a failure without a response (network, timeout).
function Get-HttpStatusCode {
    param([Exception] $Exception)

    if ($Exception.Response -and $Exception.Response.StatusCode) { return [int] $Exception.Response.StatusCode }
    if ($Exception -is [System.Net.Http.HttpRequestException] -and $Exception.StatusCode) { return [int] $Exception.StatusCode }
    return $null
}

# Runs a query of the build API. Returns its result, or $null after logging a transient failure; throws for a failure
# that waiting longer cannot fix.
$lastError = $null
function Invoke-BuildApi {
    param([scriptblock] $Query, [string] $Argument)

    try {
        return & $Query $Argument
    }
    catch {
        $status = Get-HttpStatusCode $_.Exception
        $message = $_.Exception.Message
        if ($status -in 401, 403) {
            throw "Permission problem reading this build's artifacts: the build API refused the job's access token (HTTP $status); check the build service's 'View builds' permission. $message"
        }
        if ($status -and $status -ne 429 -and $status -lt 500) {
            throw "The build API rejected the request (HTTP $status): $message"
        }

        # Logged once per distinct error, so a long outage does not flood the log.
        if ($message -ne $script:lastError) {
            $script:lastError = $message
            Write-Host "Could not query the build API: $message; retrying."
        }
        return $null
    }
}

$started = [datetime]::UtcNow
$deadline = $started.AddMinutes($TimeoutMinutes)
do {
    if (Invoke-BuildApi $GetArtifact $ArtifactName) {
        $waited = [math]::Round(([datetime]::UtcNow - $started).TotalSeconds)
        Write-Host "Artifact $ArtifactName is published (waited ${waited}s)."
        return
    }

    # Checked after the artifact: a job that published and then failed a later step still delivered.
    if ($JobName) {
        $result = Invoke-BuildApi $GetBuildJobResult $JobName
        if ($result -and $result -notin 'succeeded', 'succeededWithIssues') {
            throw "$JobName $result, so artifact $ArtifactName will not be published; see the $JobName job."
        }
    }

    Start-Sleep -Seconds $PollSeconds
} while ([datetime]::UtcNow -lt $deadline)

$job = if ($JobName) { $JobName } else { 'build' }
throw "Artifact $ArtifactName was not published within $TimeoutMinutes minutes; check the $job job."
