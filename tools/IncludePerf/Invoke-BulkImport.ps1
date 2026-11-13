<#
.SYNOPSIS
    Bulk-loads generated NDJSON into a FHIR server using the $import operation.

.DESCRIPTION
    Builds a FHIR Parameters resource listing every NDJSON blob from the generator's manifest, POSTs it to
    $import, then polls the returned Content-Location until the job completes.

    The server must be configured with:
      FhirServer__Operations__Import__Enabled=true
      FhirServer__Operations__Import__InitialImportMode=true   (required for mode=InitialLoad)
      FhirServer__Operations__IntegrationDataStore__StorageAccountUri=https://<account>.blob.core.windows.net/
      TaskHosting__Enabled=true

    Note that InitialImportMode makes InitialImportLockMiddleware reject every non-GET request except
    $import itself - including POST /connect/token. Run the import with security disabled, or acquire the
    token before enabling the flag.

.EXAMPLE
    ./Invoke-BulkImport.ps1 -Endpoint https://myfhir.azurecontainerapps.io `
        -ManifestPath C:\perfdata-large\manifest.json `
        -StorageAccount mystorage -Container ndjson
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Endpoint,

    [Parameter(Mandatory = $true)]
    [string] $ManifestPath,

    [Parameter(Mandatory = $true)]
    [string] $StorageAccount,

    [Parameter(Mandatory = $false)]
    [string] $Container = 'ndjson',

    [Parameter(Mandatory = $false)]
    [ValidateSet('InitialLoad', 'IncrementalLoad')]
    [string] $Mode = 'InitialLoad',

    [Parameter(Mandatory = $false)]
    [string] $AccessToken,

    [Parameter(Mandatory = $false)]
    [string] $ErrorContainerName,

    [Parameter(Mandatory = $false)]
    [int] $PollSeconds = 60,

    [Parameter(Mandatory = $false)]
    [int] $TimeoutHours = 12
)

$ErrorActionPreference = 'Stop'

$Endpoint = $Endpoint.TrimEnd('/')
$manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
$baseUri = "https://$StorageAccount.blob.core.windows.net/$Container"

$inputs = @()
foreach ($resourceType in $manifest.resourceTypes) {
    foreach ($file in $resourceType.files) {
        $inputs += @{
            name = 'input'
            part = @(
                @{ name = 'type'; valueString = $resourceType.type },
                @{ name = 'url';  valueUri    = "$baseUri/$file" }
            )
        }
    }
}

$parameters = @(
    @{ name = 'inputFormat'; valueString = 'application/fhir+ndjson' },
    @{ name = 'mode';        valueString = $Mode }
)

# Import jobs are de-duplicated by a hash of the request definition, so a failed job cannot simply be
# resubmitted. Supplying an error container both captures per-resource failures and varies the hash.
if ($ErrorContainerName) {
    $parameters += @{ name = 'errorContainerName'; valueString = $ErrorContainerName }
}

$body = @{
    resourceType = 'Parameters'
    parameter    = $parameters + $inputs
} | ConvertTo-Json -Depth 10 -Compress

Write-Host "Import request:" -ForegroundColor Cyan
Write-Host "  endpoint : $Endpoint"
Write-Host "  mode     : $Mode"
Write-Host "  files    : $($inputs.Count)"
Write-Host "  resources: $($manifest.totalResources)"
Write-Host ""

$headers = @{ 'Prefer' = 'respond-async'; 'Content-Type' = 'application/fhir+json' }
if ($AccessToken) { $headers['Authorization'] = "Bearer $AccessToken" }

$response = Invoke-WebRequest -Uri "$Endpoint/`$import" -Method Post -Headers $headers -Body $body -UseBasicParsing -TimeoutSec 300
$location = $response.Headers['Content-Location']
if ($location -is [array]) { $location = $location[0] }
if (-not $location) { throw "No Content-Location returned from `$import (status $($response.StatusCode))." }

Write-Host "Job accepted: $location" -ForegroundColor Green
Write-Host ""

$pollHeaders = @{}
if ($AccessToken) { $pollHeaders['Authorization'] = "Bearer $AccessToken" }

$stopwatch = [Diagnostics.Stopwatch]::StartNew()
$deadline = (Get-Date).AddHours($TimeoutHours)

while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds $PollSeconds

    try {
        $poll = Invoke-WebRequest -Uri $location -Headers $pollHeaders -UseBasicParsing -TimeoutSec 120
    }
    catch {
        Write-Host ("  [{0:hh\:mm\:ss}] poll error: {1}" -f $stopwatch.Elapsed, $_.Exception.Message) -ForegroundColor Yellow
        continue
    }

    if ($poll.StatusCode -eq 202) {
        $progress = $poll.Headers['X-Progress']
        if ($progress -is [array]) { $progress = $progress[0] }
        Write-Host ("  [{0:hh\:mm\:ss}] in progress {1}" -f $stopwatch.Elapsed, $progress)
        continue
    }

    if ($poll.StatusCode -eq 200) {
        $result = $poll.Content | ConvertFrom-Json
        $imported = ($result.output | Measure-Object -Property count -Sum).Sum
        $errors = ($result.error | Measure-Object -Property count -Sum).Sum

        Write-Host ""
        Write-Host ("COMPLETED in {0:hh\:mm\:ss}" -f $stopwatch.Elapsed) -ForegroundColor Green
        Write-Host "  imported: $imported"
        Write-Host "  errors  : $errors"

        if ($result.error -and $result.error.Count -gt 0) {
            Write-Host "  error details:" -ForegroundColor Yellow
            $result.error | ForEach-Object { Write-Host "    $($_.type) $($_.count) $($_.url)" }
        }

        return $result
    }

    throw "Unexpected status $($poll.StatusCode) from import status endpoint."
}

throw "Import did not complete within $TimeoutHours hours."
