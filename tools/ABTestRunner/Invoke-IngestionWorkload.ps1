<#
.SYNOPSIS
    Runs deterministic bundle or $import ingestion workloads against two FHIR endpoints.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [uri] $ControlUrl,

    [Parameter(Mandatory = $true)]
    [uri] $TreatmentUrl,

    [Parameter(Mandatory = $true)]
    [ValidateSet('Bundle', 'Import')]
    [string] $Workload,

    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory,

    [Parameter(Mandatory = $true)]
    [string] $ComparisonLabel,

    [Parameter(Mandatory = $true)]
    [string] $ControlLabel,

    [Parameter(Mandatory = $true)]
    [string] $TreatmentLabel,

    [ValidateRange(0, 20)]
    [int] $WarmupIterations = 1,

    [ValidateRange(1, 20)]
    [int] $MeasuredIterations = 3,

    [ValidateRange(1, 100000)]
    [int] $BundleCount = 100,

    [ValidateRange(1, 500)]
    [int] $BundleSize = 100,

    [ValidateRange(1, 128)]
    [int] $Concurrency = 4,

    [uri[]] $ImportInputUrl,

    [string[]] $ImportResourceType = @('Patient'),

    [long] $ImportExpectedResourceCount,

    [ValidateRange(1, 360)]
    [int] $ImportTimeoutMinutes = 120,

    [ValidateRange(0.1, 60)]
    [double] $ImportPollIntervalSeconds = 1,

    [switch] $Parallel
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

function Get-Percentile {
    param(
        [double[]] $Values,
        [ValidateRange(0, 100)]
        [int] $Percentile
    )

    if ($Values.Count -eq 0) {
        return 0
    }

    $sorted = @($Values | Sort-Object)
    $index = [Math]::Max(0, [Math]::Ceiling(($Percentile / 100.0) * $sorted.Count) - 1)
    return [double]$sorted[$index]
}

function ConvertFrom-ResponseJson {
    param([object] $Content)

    $json = if ($Content -is [byte[]]) {
        [System.Text.Encoding]::UTF8.GetString($Content)
    } else {
        [string]$Content
    }

    return $json | ConvertFrom-Json
}

function Get-CorpusFamilyToken {
    param([int] $Iteration)

    return 'ABPerfIteration{0:D4}' -f $Iteration
}

function New-TransactionBundleJson {
    param(
        [int] $BundleIndex,
        [int] $ResourceCount,
        [int] $Iteration
    )

    $entries = for ($resourceIndex = 0; $resourceIndex -lt $ResourceCount; $resourceIndex++) {
        $id = 'ab-{0:D2}-{1:D6}' -f $Iteration, (($BundleIndex * $ResourceCount) + $resourceIndex)
        [ordered]@{
            fullUrl = "urn:uuid:$id"
            resource = [ordered]@{
                resourceType = 'Patient'
                id = $id
                active = $true
                name = @([ordered]@{
                    family = Get-CorpusFamilyToken -Iteration $Iteration
                    given = @("Patient$resourceIndex")
                })
            }
            request = [ordered]@{ method = 'PUT'; url = "Patient/$id" }
        }
    }

    return ([ordered]@{ resourceType = 'Bundle'; type = 'transaction'; entry = @($entries) } |
        ConvertTo-Json -Depth 8 -Compress)
}

function Invoke-BundleIteration {
    param(
        [uri] $Endpoint,
        [int] $Iteration,
        [int] $RequestCount,
        [int] $ResourcesPerBundle,
        [int] $ThrottleLimit
    )

    $bundleBodies = @(
        for ($bundleIndex = 0; $bundleIndex -lt $RequestCount; $bundleIndex++) {
            New-TransactionBundleJson -BundleIndex $bundleIndex -ResourceCount $ResourcesPerBundle -Iteration $Iteration
        }
    )
    $iterationStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $requestResults = @(0..($RequestCount - 1) | ForEach-Object -Parallel {
        $bodies = $using:bundleBodies
        $target = $using:Endpoint
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            $response = Invoke-WebRequest `
                -Uri $target `
                -Method Post `
                -ContentType 'application/fhir+json' `
                -Headers @{ Accept = 'application/fhir+json' } `
                -Body $bodies[$_] `
                -SkipHttpErrorCheck
            $stopwatch.Stop()
            $successfulResources = 0
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
                $responseJson = if ($response.Content -is [byte[]]) {
                    [System.Text.Encoding]::UTF8.GetString($response.Content)
                } else {
                    [string]$response.Content
                }
                $responseBundle = $responseJson | ConvertFrom-Json
                $successfulResources = @(
                    $responseBundle.entry | Where-Object { $_.response.status -match '^2' }
                ).Count
            }
            [pscustomobject]@{
                StatusCode = [int]$response.StatusCode
                LatencyMs = $stopwatch.Elapsed.TotalMilliseconds
                SuccessfulResources = $successfulResources
                Failed = ($response.StatusCode -lt 200 -or $response.StatusCode -ge 300)
            }
        } catch {
            $stopwatch.Stop()
            [pscustomobject]@{
                StatusCode = 0
                LatencyMs = $stopwatch.Elapsed.TotalMilliseconds
                SuccessfulResources = 0
                Failed = $true
            }
        }
    } -ThrottleLimit $ThrottleLimit)
    $iterationStopwatch.Stop()

    $failedRequests = @($requestResults | Where-Object Failed).Count
    $successfulResources = ($requestResults | Measure-Object SuccessfulResources -Sum).Sum
    $expectedResources = $RequestCount * $ResourcesPerBundle
    if ($failedRequests -gt 0 -or $successfulResources -ne $expectedResources) {
        throw "Bundle iteration failed: requests=$failedRequests, resources=$successfulResources/$expectedResources."
    }

    $probeId = 'ab-{0:D2}-{1:D6}' -f $Iteration, 0
    $directProbe = Invoke-WebRequest -Uri ([uri]::new($Endpoint, "Patient/$probeId")) -SkipHttpErrorCheck
    $familyToken = Get-CorpusFamilyToken -Iteration $Iteration
    $searchProbe = Invoke-RestMethod -Uri ([uri]::new($Endpoint, "Patient?family:exact=$familyToken&_summary=count"))
    if ($directProbe.StatusCode -ne 200 -or $searchProbe.total -ne $expectedResources) {
        throw "Bundle correctness probe failed for iteration $Iteration (search total $($searchProbe.total)/$expectedResources)."
    }

    $latencies = [double[]]@($requestResults.LatencyMs)
    [pscustomobject]@{
        elapsedSeconds = $iterationStopwatch.Elapsed.TotalSeconds
        successfulResources = [long]$successfulResources
        failures = $failedRequests
        resourcesPerSecond = $successfulResources / $iterationStopwatch.Elapsed.TotalSeconds
        p50Milliseconds = Get-Percentile -Values $latencies -Percentile 50
        p95Milliseconds = Get-Percentile -Values $latencies -Percentile 95
        p99Milliseconds = Get-Percentile -Values $latencies -Percentile 99
    }
}

function New-ImportParametersJson {
    param(
        [uri[]] $InputUrl,
        [string[]] $ResourceType
    )

    $parameters = [System.Collections.Generic.List[object]]::new()
    $parameters.Add([ordered]@{ name = 'inputFormat'; valueString = 'application/fhir+ndjson' })
    $parameters.Add([ordered]@{ name = 'inputSource'; valueUri = 'https://other-server.example.org' })
    for ($index = 0; $index -lt $InputUrl.Count; $index++) {
        $parameters.Add([ordered]@{
            name = 'input'
            part = @(
                [ordered]@{ name = 'type'; valueString = $ResourceType[$index] },
                [ordered]@{ name = 'url'; valueUri = $InputUrl[$index].AbsoluteUri }
            )
        })
    }
    $parameters.Add([ordered]@{
        name = 'storageDetail'
        part = @([ordered]@{ name = 'type'; valueString = 'azure-blob' })
    })
    $parameters.Add([ordered]@{ name = 'mode'; valueString = 'IncrementalLoad' })

    return ([ordered]@{ resourceType = 'Parameters'; parameter = $parameters } |
        ConvertTo-Json -Depth 8 -Compress)
}

function Resolve-ResponseHeaderUri {
    param(
        [object] $Value,
        [uri] $BaseUri
    )

    $headerValue = @($Value) | Select-Object -First 1
    if ($null -eq $headerValue -or [string]::IsNullOrWhiteSpace([string]$headerValue)) {
        return $null
    }

    $uri = [uri]$headerValue
    if ($uri.IsAbsoluteUri) {
        return $uri
    }

    return [uri]::new($BaseUri, $uri)
}

function Invoke-ImportIteration {
    param(
        [uri] $Endpoint,
        [uri[]] $InputUrl,
        [string[]] $ResourceType,
        [long] $ExpectedResourceCount,
        [int] $TimeoutMinutes,
        [double] $PollIntervalSeconds
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $submitResponse = Invoke-WebRequest `
        -Uri ([uri]::new($Endpoint, '$import')) `
        -Method Post `
        -ContentType 'application/fhir+json' `
        -Headers @{ Accept = 'application/fhir+json'; Prefer = 'respond-async' } `
        -Body (New-ImportParametersJson -InputUrl $InputUrl -ResourceType $ResourceType) `
        -SkipHttpErrorCheck
    if ($submitResponse.StatusCode -ne 202) {
        throw "Import submission failed with HTTP $($submitResponse.StatusCode)."
    }

    $statusUrl = Resolve-ResponseHeaderUri -Value $submitResponse.Headers.Location -BaseUri $Endpoint
    if (-not $statusUrl) {
        $statusUrl = Resolve-ResponseHeaderUri -Value $submitResponse.Headers['Content-Location'] -BaseUri $Endpoint
    }
    if (-not $statusUrl) {
        throw 'Import response did not include a status URL.'
    }

    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    do {
        $statusResponse = Invoke-WebRequest -Uri $statusUrl -Headers @{ Accept = 'application/json' } -SkipHttpErrorCheck
        if ($statusResponse.StatusCode -ne 202) {
            break
        }

        $remainingMilliseconds = ($deadline - (Get-Date)).TotalMilliseconds
        if ($remainingMilliseconds -le 0) {
            break
        }

        $sleepMilliseconds = [Math]::Min($PollIntervalSeconds * 1000, $remainingMilliseconds)
        Start-Sleep -Milliseconds ([Math]::Max(1, [Math]::Ceiling($sleepMilliseconds)))
    } while ((Get-Date) -lt $deadline)
    $stopwatch.Stop()

    if ($statusResponse.StatusCode -eq 202) {
        throw "Import did not complete within $TimeoutMinutes minutes."
    }
    if ($statusResponse.StatusCode -ne 200) {
        throw "Import job failed with HTTP $($statusResponse.StatusCode)."
    }

    $job = ConvertFrom-ResponseJson -Content $statusResponse.Content
    $importedResources = ($job.output | Measure-Object count -Sum).Sum
    $failedResources = ($job.error | Measure-Object count -Sum).Sum
    if ($failedResources -gt 0 -or $importedResources -ne $ExpectedResourceCount) {
        throw "Import result failed correctness gate: failures=$failedResources, resources=$importedResources/$ExpectedResourceCount."
    }

    foreach ($type in ($ResourceType | Sort-Object -Unique)) {
        $searchProbe = Invoke-RestMethod -Uri ([uri]::new($Endpoint, "$type`?_count=1"))
        if ($searchProbe.total -lt 1 -or
            $searchProbe.entry[0].resource.resourceType -ne $type) {
            throw "Import correctness search returned no representative $type resource."
        }
    }

    [pscustomobject]@{
        elapsedSeconds = $stopwatch.Elapsed.TotalSeconds
        successfulResources = [long]$importedResources
        failures = [long]$failedResources
        resourcesPerSecond = $importedResources / $stopwatch.Elapsed.TotalSeconds
        p50Milliseconds = $null
        p95Milliseconds = $null
        p99Milliseconds = $null
    }
}

function Invoke-SideWorkload {
    param(
        [string] $Side,
        [string] $SideLabel,
        [uri] $Endpoint,
        [string] $WorkloadName,
        [int] $Warmups,
        [int] $Measurements,
        [int] $RequestCount,
        [int] $ResourcesPerBundle,
        [int] $ThrottleLimit,
        [uri[]] $InputUrl,
        [string[]] $ResourceType,
        [long] $ExpectedResourceCount,
        [int] $TimeoutMinutes,
        [double] $PollIntervalSeconds
    )

    Write-Host "► Running $Side $WorkloadName workload against $Endpoint"
    $rows = [System.Collections.Generic.List[object]]::new()
    for ($iteration = -$Warmups; $iteration -lt $Measurements; $iteration++) {
        $isWarmup = $iteration -lt 0
        $corpusIteration = if ($isWarmup) { 100 + $iteration + $Warmups } else { $iteration }
        if ($isWarmup -and $WorkloadName -eq 'Import') {
            $warmupResponse = Invoke-WebRequest -Uri ([uri]::new($Endpoint, 'metadata')) -SkipHttpErrorCheck
            if ($warmupResponse.StatusCode -ne 200) {
                throw "Import warm-up metadata request failed with HTTP $($warmupResponse.StatusCode)."
            }
            continue
        }
        $result = if ($WorkloadName -eq 'Bundle') {
            Invoke-BundleIteration `
                -Endpoint $Endpoint `
                -Iteration $corpusIteration `
                -RequestCount $RequestCount `
                -ResourcesPerBundle $ResourcesPerBundle `
                -ThrottleLimit $ThrottleLimit
        } else {
            Invoke-ImportIteration `
                -Endpoint $Endpoint `
                -InputUrl $InputUrl `
                -ResourceType $ResourceType `
                -ExpectedResourceCount $ExpectedResourceCount `
                -TimeoutMinutes $TimeoutMinutes `
                -PollIntervalSeconds $PollIntervalSeconds
        }

        if (-not $isWarmup) {
            $rows.Add([pscustomobject][ordered]@{
                side = $Side
                label = $SideLabel
                iteration = $iteration + 1
                workload = $WorkloadName
                elapsedSeconds = $result.elapsedSeconds
                successfulResources = $result.successfulResources
                failures = $result.failures
                resourcesPerSecond = $result.resourcesPerSecond
                p50Milliseconds = $result.p50Milliseconds
                p95Milliseconds = $result.p95Milliseconds
                p99Milliseconds = $result.p99Milliseconds
            })
        }
    }

    return $rows
}

$sideArguments = @(
    @('control', $ControlLabel, $ControlUrl),
    @('treatment', $TreatmentLabel, $TreatmentUrl)
)

if ($Parallel) {
    $functionNames = @(
        'Get-Percentile',
        'ConvertFrom-ResponseJson',
        'Get-CorpusFamilyToken',
        'New-TransactionBundleJson',
        'Invoke-BundleIteration',
        'New-ImportParametersJson',
        'Resolve-ResponseHeaderUri',
        'Invoke-ImportIteration',
        'Invoke-SideWorkload'
    )
    $definitions = ($functionNames | ForEach-Object {
        "function $_ { $((Get-Item ""function:$_"").Definition) }"
    }) -join [Environment]::NewLine
    $jobs = foreach ($side in $sideArguments) {
        Start-ThreadJob -InitializationScript ([scriptblock]::Create($definitions)) -ScriptBlock {
            param($Arguments)
            Invoke-SideWorkload @Arguments
        } -ArgumentList @{
            Side = $side[0]; SideLabel = $side[1]; Endpoint = $side[2]; WorkloadName = $Workload
            Warmups = $WarmupIterations; Measurements = $MeasuredIterations
            RequestCount = $BundleCount; ResourcesPerBundle = $BundleSize; ThrottleLimit = $Concurrency
            InputUrl = $ImportInputUrl; ResourceType = $ImportResourceType
            ExpectedResourceCount = $ImportExpectedResourceCount; TimeoutMinutes = $ImportTimeoutMinutes
            PollIntervalSeconds = $ImportPollIntervalSeconds
        }
    }
    $results = @($jobs | Receive-Job -Wait -AutoRemoveJob)
} else {
    $results = foreach ($side in $sideArguments) {
        Invoke-SideWorkload `
            -Side $side[0] `
            -SideLabel $side[1] `
            -Endpoint $side[2] `
            -WorkloadName $Workload `
            -Warmups $WarmupIterations `
            -Measurements $MeasuredIterations `
            -RequestCount $BundleCount `
            -ResourcesPerBundle $BundleSize `
            -ThrottleLimit $Concurrency `
            -InputUrl $ImportInputUrl `
            -ResourceType $ImportResourceType `
            -ExpectedResourceCount $ImportExpectedResourceCount `
            -TimeoutMinutes $ImportTimeoutMinutes `
            -PollIntervalSeconds $ImportPollIntervalSeconds
    }
}

$controlRows = @($results | Where-Object side -eq 'control')
$treatmentRows = @($results | Where-Object side -eq 'treatment')
if (($controlRows | Measure-Object successfulResources -Sum).Sum -ne
    ($treatmentRows | Measure-Object successfulResources -Sum).Sum) {
    throw 'Control and treatment successful resource counts differ.'
}

$jsonPath = Join-Path $OutputDirectory 'ingestion-results.json'
$csvPath = Join-Path $OutputDirectory 'ingestion-results.csv'
$reportPath = Join-Path $OutputDirectory 'ingestion-comparison.md'
$results | ConvertTo-Json -Depth 5 | Set-Content -Path $jsonPath -Encoding utf8
$results | Export-Csv -Path $csvPath -NoTypeInformation -Encoding utf8

$controlThroughput = ($controlRows | Measure-Object resourcesPerSecond -Average).Average
$treatmentThroughput = ($treatmentRows | Measure-Object resourcesPerSecond -Average).Average
$delta = if ($controlThroughput -eq 0) { 0 } else {
    (($treatmentThroughput - $controlThroughput) / $controlThroughput) * 100
}
$aggregateRows = [System.Collections.Generic.List[string]]::new()
$aggregateRows.Add("| Mean resources/sec | $([Math]::Round($controlThroughput, 2)) | $([Math]::Round($treatmentThroughput, 2)) | $([Math]::Round($delta, 2))% |")
if ($Workload -eq 'Bundle') {
    foreach ($percentile in @('p50Milliseconds', 'p95Milliseconds', 'p99Milliseconds')) {
        $controlLatency = ($controlRows | Measure-Object $percentile -Average).Average
        $treatmentLatency = ($treatmentRows | Measure-Object $percentile -Average).Average
        $latencyDelta = if ($controlLatency -eq 0) { 0 } else {
            (($treatmentLatency - $controlLatency) / $controlLatency) * 100
        }
        $label = $percentile.Replace('Milliseconds', ' latency (ms)')
        $aggregateRows.Add("| Mean $label | $([Math]::Round($controlLatency, 2)) | $([Math]::Round($treatmentLatency, 2)) | $([Math]::Round($latencyDelta, 2))% |")
    }
}
$reportRows = $results | ForEach-Object {
    "| $($_.label) | $($_.iteration) | $([Math]::Round($_.successfulResources, 0)) | $([Math]::Round($_.elapsedSeconds, 2)) | $([Math]::Round($_.resourcesPerSecond, 2)) | $(if ($null -eq $_.p50Milliseconds) { 'n/a' } else { [Math]::Round($_.p50Milliseconds, 2) }) | $(if ($null -eq $_.p95Milliseconds) { 'n/a' } else { [Math]::Round($_.p95Milliseconds, 2) }) | $(if ($null -eq $_.p99Milliseconds) { 'n/a' } else { [Math]::Round($_.p99Milliseconds, 2) }) |"
}
@"
# Ingestion A/B Comparison

**Experiment:** $ComparisonLabel

**Workload:** $Workload

**Execution:** $(if ($Parallel) { 'parallel (opt-in)' } else { 'sequential' })

**Control:** $ControlLabel

**Treatment:** $TreatmentLabel

| Side | Iteration | Resources | Seconds | Resources/sec | p50 ms | p95 ms | p99 ms |
|---|---:|---:|---:|---:|---:|---:|---:|
$($reportRows -join [Environment]::NewLine)

## Aggregate

| Metric | Control | Treatment | Delta |
|---|---:|---:|---:|
$($aggregateRows -join [Environment]::NewLine)

All request/job failure and resource-count correctness gates passed.
"@ | Set-Content -Path $reportPath -Encoding utf8

Write-Host "Ingestion JSON:  $jsonPath"
Write-Host "Ingestion CSV:   $csvPath"
Write-Host "Comparison:      $reportPath"
