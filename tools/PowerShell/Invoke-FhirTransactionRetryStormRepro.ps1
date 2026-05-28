<#
.SYNOPSIS
Replays duplicate concurrent FHIR transaction bundles with the same traceparent header.

.DESCRIPTION
This is a diagnostic harness for WI 188321 Theory 1. It intentionally sends the
same transaction bundle many times in parallel so a SQL-backed FHIR server can be
observed for concurrent-write conflicts, completed SqlTransaction failures, and
HTTP 500 retry-storm behavior.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [uri]$Endpoint,

    [string]$BearerToken,

    [int]$RequestCount = 100,

    [int]$Concurrency = 20,

    [string]$ScenarioId = "theory1-$([Guid]::NewGuid().ToString('N'))",

    [ValidateSet("Parallel", "Sequential")]
    [string]$BundleProcessingLogic = "Parallel",

    [string]$TraceId = $(([Guid]::NewGuid().ToString("N")).ToLowerInvariant()),

    [string]$ParentId = $(([Guid]::NewGuid().ToString("N")).Substring(0, 16).ToLowerInvariant()),

    [int]$TimeoutSeconds = 120,

    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($RequestCount -lt 1) {
    throw "RequestCount must be at least 1."
}

if ($Concurrency -lt 1) {
    throw "Concurrency must be at least 1."
}

if ($TraceId -notmatch "^[0-9a-fA-F]{32}$") {
    throw "TraceId must be 32 hexadecimal characters."
}

if ($ParentId -notmatch "^[0-9a-fA-F]{16}$") {
    throw "ParentId must be 16 hexadecimal characters."
}

$traceParent = "00-$($TraceId.ToLowerInvariant())-$($ParentId.ToLowerInvariant())-01"
$identifierSystem = "https://microsoft.github.io/fhir-server/repros/wi-188321"
$patientIdentifier = "$ScenarioId-patient"
$observationIdentifier = "$ScenarioId-observation"

$bundle = @{
    resourceType = "Bundle"
    type = "transaction"
    entry = @(
        @{
            resource = @{
                resourceType = "Patient"
                active = $true
                identifier = @(
                    @{
                        system = $identifierSystem
                        value = $patientIdentifier
                    }
                )
            }
            request = @{
                method = "POST"
                url = "Patient?identifier=$identifierSystem|$patientIdentifier"
            }
        },
        @{
            resource = @{
                resourceType = "Observation"
                status = "final"
                identifier = @(
                    @{
                        system = $identifierSystem
                        value = $observationIdentifier
                    }
                )
                code = @{
                    coding = @(
                        @{
                            system = "http://loinc.org"
                            code = "8310-5"
                            display = "Body temperature"
                        }
                    )
                }
                subject = @{
                    identifier = @{
                        system = $identifierSystem
                        value = $patientIdentifier
                    }
                }
                valueQuantity = @{
                    value = 37
                    unit = "C"
                    system = "http://unitsofmeasure.org"
                    code = "Cel"
                }
            }
            request = @{
                method = "POST"
                url = "Observation?identifier=$identifierSystem|$observationIdentifier"
            }
        }
    )
}

$body = $bundle | ConvertTo-Json -Depth 20 -Compress

Write-Host "Endpoint: $Endpoint"
Write-Host "ScenarioId: $ScenarioId"
Write-Host "traceparent: $traceParent"
Write-Host "Requests: $RequestCount, Concurrency: $Concurrency, BundleProcessingLogic: $BundleProcessingLogic"

$jobs = for ($i = 0; $i -lt $RequestCount; $i++) {
    $attempt = $i + 1
    Start-ThreadJob -ThrottleLimit $Concurrency -ArgumentList @(
        $attempt,
        $Endpoint.AbsoluteUri,
        $body,
        $traceParent,
        $BundleProcessingLogic,
        $BearerToken,
        $TimeoutSeconds
    ) -ScriptBlock {
        param(
            [int]$Attempt,
            [string]$EndpointUri,
            [string]$Body,
            [string]$TraceParent,
            [string]$ProcessingLogic,
            [string]$Token,
            [int]$HttpTimeoutSeconds
        )

        $started = [DateTimeOffset]::UtcNow
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $client = [System.Net.Http.HttpClient]::new()
        $client.Timeout = [TimeSpan]::FromSeconds($HttpTimeoutSeconds)
        $request = $null
        $response = $null

        try {
            $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, $EndpointUri)
            $request.Headers.TryAddWithoutValidation("traceparent", $TraceParent) | Out-Null
            $request.Headers.TryAddWithoutValidation("x-bundle-processing-logic", $ProcessingLogic) | Out-Null
            $request.Headers.TryAddWithoutValidation("User-Agent", "fhir-server-theory1-repro/1.0") | Out-Null

            if (-not [string]::IsNullOrWhiteSpace($Token)) {
                $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $Token)
            }

            $request.Content = [System.Net.Http.StringContent]::new($Body, [System.Text.Encoding]::UTF8, "application/fhir+json")

            $response = $client.SendAsync($request).GetAwaiter().GetResult()
            $responseBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

            [pscustomobject]@{
                Attempt = $Attempt
                StartedUtc = $started
                ElapsedMs = $stopwatch.ElapsedMilliseconds
                StatusCode = [int]$response.StatusCode
                ReasonPhrase = $response.ReasonPhrase
                HasCompletedSqlTransactionMessage = $responseBody -match "SqlTransaction has completed"
                HasConcurrentResourceMessage = $responseBody -match "Resource has been recently updated or added"
                BodyPreview = if ($responseBody.Length -gt 1000) { $responseBody.Substring(0, 1000) } else { $responseBody }
            }
        }
        catch {
            [pscustomobject]@{
                Attempt = $Attempt
                StartedUtc = $started
                ElapsedMs = $stopwatch.ElapsedMilliseconds
                StatusCode = $null
                ReasonPhrase = "ClientException"
                HasCompletedSqlTransactionMessage = $_.Exception.ToString() -match "SqlTransaction has completed"
                HasConcurrentResourceMessage = $_.Exception.ToString() -match "Resource has been recently updated or added"
                BodyPreview = $_.Exception.ToString()
            }
        }
        finally {
            $stopwatch.Stop()
            if ($null -ne $response) {
                $response.Dispose()
            }

            if ($null -ne $request) {
                $request.Dispose()
            }

            $client.Dispose()
        }
    }
}

$null = Wait-Job -Job $jobs
$orderedResults = Receive-Job -Job $jobs | Sort-Object Attempt
Remove-Job -Job $jobs

$summary = $orderedResults |
    Group-Object StatusCode, ReasonPhrase |
    Sort-Object Count -Descending |
    Select-Object Count, Name

Write-Host ""
Write-Host "Status summary:"
foreach ($item in $summary) {
    Write-Host ("  {0,5}  {1}" -f $item.Count, $item.Name)
}

$completedTransactionCount = @($orderedResults | Where-Object { $_.HasCompletedSqlTransactionMessage }).Count
$concurrentResourceCount = @($orderedResults | Where-Object { $_.HasConcurrentResourceMessage }).Count
Write-Host "Responses containing completed SqlTransaction message: $completedTransactionCount"
Write-Host "Responses containing concurrent resource message: $concurrentResourceCount"

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $orderedResults | ConvertTo-Json -Depth 5 | Set-Content -Path $OutputPath -Encoding UTF8
    Write-Host "Wrote result details to $OutputPath"
}
