[CmdletBinding()]
param(
    [Uri]$BaseUrl = 'https://localhost:44348',
    [string]$ClientId = 'globalAdminServicePrincipal',
    [string]$ClientSecret = 'globalAdminServicePrincipal',
    [ValidateRange(30, 3600)]
    [int]$TimeoutSeconds = 600,
    [switch]$Cleanup,
    [switch]$SkipCertificateCheck)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$normalizedBaseUrl = $BaseUrl.AbsoluteUri.TrimEnd('/')
$demoRoot = Split-Path -Parent $PSScriptRoot
$observationPath = Join-Path $demoRoot 'resources/reindex-proof/demo-reindex-proof-observation.json'
$searchParameterPath = Join-Path $demoRoot 'resources/reindex-proof/demo-reindex-proof-search-parameter.json'

function ConvertFrom-FhirResponse {
    param([Parameter(Mandatory)]$Response)

    $content = if ($Response.Content -is [byte[]]) {
        [Text.Encoding]::UTF8.GetString($Response.Content)
    }
    else {
        [string]$Response.Content
    }

    if ([string]::IsNullOrWhiteSpace($content)) {
        throw "FHIR response from $($Response.BaseResponse.RequestMessage.RequestUri) had no body."
    }

    return $content | ConvertFrom-Json -Depth 100
}

function Invoke-FhirRequest {
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][Uri]$RequestUri,
        [Parameter(Mandatory)][string]$AccessToken,
        [object]$Body,
        [switch]$AllowError)

    $request = @{
        Method = $Method
        Uri = $RequestUri
        Headers = @{
            Accept = 'application/fhir+json'
            Authorization = "Bearer $AccessToken"
        }
        SkipHttpErrorCheck = $true
    }

    if ($null -ne $Body) {
        $request.ContentType = 'application/fhir+json'
        $request.Body = $Body | ConvertTo-Json -Depth 100
    }

    if ($SkipCertificateCheck) {
        $request.SkipCertificateCheck = $true
    }

    $response = Invoke-WebRequest @request
    if (-not $AllowError -and ($response.StatusCode -lt 200 -or $response.StatusCode -ge 300)) {
        $errorContent = if ($response.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($response.Content) } else { [string]$response.Content }
        throw "FHIR $Method $RequestUri failed with HTTP $($response.StatusCode): $errorContent"
    }

    return $response
}

function Get-SemanticEvidence {
    param([Parameter(Mandatory)]$Entry)

    if (
        $null -eq $Entry.PSObject.Properties['search'] -or
        $null -eq $Entry.search.PSObject.Properties['extension']) {
        return
    }

    return @($Entry.search.extension) | Where-Object url -eq 'http://microsoft.com/fhir/StructureDefinition/semantic-search-evidence'
}

function Get-EvidenceSearchParameterCanonicals {
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Evidence)

    return @($Evidence | ForEach-Object {
        if ($null -ne $_.PSObject.Properties['extension']) {
            @($_.extension) |
                Where-Object url -eq 'searchParameter' |
                Select-Object -ExpandProperty valueUri
        }
    })
}

function Start-ObservationReindex {
    param([Parameter(Mandatory)][string]$AccessToken)

    $body = @{
        resourceType = 'Parameters'
        parameter = @(
            @{
                name = 'targetResourceTypes'
                valueString = 'Observation'
            },
            @{
                name = 'maximumNumberOfResourcesPerQuery'
                valueInteger = 100
            },
            @{
                name = 'maximumNumberOfResourcesPerWrite'
                valueInteger = 25
            })
    }

    $response = Invoke-FhirRequest -Method POST -RequestUri "$normalizedBaseUrl/`$reindex" -AccessToken $AccessToken -Body $body
    $contentLocation = [string]$response.Headers['Content-Location']
    if ([string]::IsNullOrWhiteSpace($contentLocation)) {
        throw 'System reindex response did not include Content-Location.'
    }

    return [Uri]::new([Uri]"$normalizedBaseUrl/", $contentLocation)
}

function Wait-ReindexJob {
    param(
        [Parameter(Mandatory)][Uri]$JobUri,
        [Parameter(Mandatory)][string]$AccessToken)

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastStatus = 'Unknown'

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $response = Invoke-FhirRequest -Method GET -RequestUri $JobUri -AccessToken $AccessToken
        $job = ConvertFrom-FhirResponse $response
        $statusParameter = @($job.parameter) | Where-Object name -eq 'status' | Select-Object -First 1
        if ($null -ne $statusParameter) {
            $lastStatus = [string]$statusParameter.valueString
        }

        if ($lastStatus -eq 'Completed') {
            return $job
        }

        if ($lastStatus -in @('Failed', 'Canceled')) {
            $failure = @($job.parameter) | Where-Object name -eq 'failureDetails' | Select-Object -First 1
            $failureDetails = if ($null -ne $failure) { [string]$failure.valueString } else { 'No failure details were returned.' }
            throw "Reindex job reached $lastStatus. $failureDetails"
        }

        Start-Sleep -Seconds 1
    }

    throw "Reindex job did not complete within $TimeoutSeconds seconds. Last status: $lastStatus"
}

$tokenRequest = @{
    Method = 'POST'
    Uri = "$normalizedBaseUrl/connect/token"
    ContentType = 'application/x-www-form-urlencoded'
    Body = @{
        grant_type = 'client_credentials'
        client_id = $ClientId
        client_secret = $ClientSecret
        scope = 'fhir-api'
    }
}

if ($SkipCertificateCheck) {
    $tokenRequest.SkipCertificateCheck = $true
}

Write-Host 'Obtaining a demo access token.'
$accessToken = (Invoke-RestMethod @tokenRequest).access_token
if ([string]::IsNullOrWhiteSpace($accessToken)) {
    throw 'Token response did not include access_token.'
}

$observation = Get-Content -Raw $observationPath | ConvertFrom-Json -Depth 100
$observationId = [string]$observation.id
$observationResponse = Invoke-FhirRequest -Method PUT -RequestUri "$normalizedBaseUrl/Observation/$observationId" -AccessToken $accessToken -Body $observation
$storedObservation = ConvertFrom-FhirResponse $observationResponse
$baselineVersion = [string]$storedObservation.meta.versionId
if ([string]::IsNullOrWhiteSpace($baselineVersion)) {
    throw 'Stored proof Observation did not include meta.versionId.'
}

Write-Host "Created or refreshed Observation/$observationId at version $baselineVersion before defining the proof SearchParameter."

$uniqueSuffix = '{0}-{1}' -f [DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss'), [Guid]::NewGuid().ToString('N').Substring(0, 8)
$searchParameter = Get-Content -Raw $searchParameterPath | ConvertFrom-Json -Depth 100
$searchParameter.url = "https://example.org/fhir/SearchParameter/demo-reindex-proof-$uniqueSuffix"
$searchParameter.name = "DemoVectorReindexProof$($uniqueSuffix.Replace('-', ''))"
$searchParameter.code = "demo-reindex-proof-$uniqueSuffix"

$searchParameterResponse = Invoke-FhirRequest -Method POST -RequestUri "$normalizedBaseUrl/SearchParameter" -AccessToken $accessToken -Body $searchParameter
$storedSearchParameter = ConvertFrom-FhirResponse $searchParameterResponse
$searchParameterId = [string]$storedSearchParameter.id
$searchCode = [string]$storedSearchParameter.code
$searchCanonical = [string]$storedSearchParameter.url
if ([string]::IsNullOrWhiteSpace($searchParameterId) -or [string]::IsNullOrWhiteSpace($searchCode)) {
    throw 'Created proof SearchParameter did not include id and code.'
}

Write-Host "Created SearchParameter/$searchParameterId with code $searchCode after the Observation write."

$queryText = [string]$observation.note[0].text
$encodedQuery = [Uri]::EscapeDataString($queryText)
$proofQuery = "$normalizedBaseUrl/Observation?_id=$observationId&$searchCode=$encodedQuery&_count=10"
$beforeResponse = Invoke-FhirRequest -Method GET -RequestUri $proofQuery -AccessToken $accessToken -AllowError
if ($beforeResponse.StatusCode -ge 200 -and $beforeResponse.StatusCode -lt 300) {
    $beforeResult = ConvertFrom-FhirResponse $beforeResponse
    $beforeEntries = if ($null -ne $beforeResult.PSObject.Properties['entry']) { @($beforeResult.entry) } else { @() }
    $matchedBeforeReindex = $beforeEntries | Where-Object { $_.resource.id -eq $observationId } | Select-Object -First 1
    if ($null -ne $matchedBeforeReindex) {
        $beforeEvidence = @(Get-SemanticEvidence -Entry $matchedBeforeReindex)
        $beforeCanonicals = Get-EvidenceSearchParameterCanonicals -Evidence $beforeEvidence
        if ($searchCanonical -in $beforeCanonicals) {
            throw "Observation/$observationId had semantic evidence for $searchCode before reindex. The proof is not isolated."
        }
    }
}

Write-Host 'Confirmed that the preexisting Observation has no semantic evidence for the new search code.'
$jobUri = Start-ObservationReindex -AccessToken $accessToken
Write-Host "Started vector-aware system reindex: $jobUri"
$completedJob = Wait-ReindexJob -JobUri $jobUri -AccessToken $accessToken
$reindexedCount = @($completedJob.parameter | Where-Object name -eq 'resourcesSuccessfullyReindexed' | Select-Object -First 1).valueDecimal
Write-Host "Reindex completed. Resources successfully reindexed: $reindexedCount"

$afterResponse = Invoke-FhirRequest -Method GET -RequestUri $proofQuery -AccessToken $accessToken
$afterResult = ConvertFrom-FhirResponse $afterResponse
$afterEntries = if ($null -ne $afterResult.PSObject.Properties['entry']) { @($afterResult.entry) } else { @() }
$matchedEntry = $afterEntries | Where-Object { $_.resource.id -eq $observationId } | Select-Object -First 1
if ($null -eq $matchedEntry) {
    throw "Observation/$observationId was not returned through $searchCode after reindex."
}

$score = [string]$matchedEntry.search.score
$evidence = @(Get-SemanticEvidence -Entry $matchedEntry)
$evidenceCanonicals = Get-EvidenceSearchParameterCanonicals -Evidence $evidence
if ([string]::IsNullOrWhiteSpace($score) -or $evidence.Count -eq 0 -or $searchCanonical -notin $evidenceCanonicals) {
    throw "Observation/$observationId did not include score and semantic evidence for $searchCanonical."
}

$currentResponse = Invoke-FhirRequest -Method GET -RequestUri "$normalizedBaseUrl/Observation/$observationId" -AccessToken $accessToken
$currentObservation = ConvertFrom-FhirResponse $currentResponse
$currentVersion = [string]$currentObservation.meta.versionId
if ($currentVersion -ne $baselineVersion) {
    throw "Observation version changed during reindex. Before: $baselineVersion; after: $currentVersion."
}

Write-Host "PASS: system reindex backfilled $searchCanonical; Observation/$observationId matched with score $score and remained at version $currentVersion." -ForegroundColor Green

if ($Cleanup) {
    Write-Host 'Cleaning up the proof Observation and SearchParameter.'
    $null = Invoke-FhirRequest -Method DELETE -RequestUri "$normalizedBaseUrl/Observation/$observationId" -AccessToken $accessToken
    $null = Invoke-FhirRequest -Method DELETE -RequestUri "$normalizedBaseUrl/SearchParameter/$searchParameterId`?hardDelete=true" -AccessToken $accessToken
    $cleanupJobUri = Start-ObservationReindex -AccessToken $accessToken
    $null = Wait-ReindexJob -JobUri $cleanupJobUri -AccessToken $accessToken
    Write-Host 'Cleanup reindex completed.'
}