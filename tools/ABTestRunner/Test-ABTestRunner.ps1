<#
.SYNOPSIS
    Runs credential-free deployment-plan and local ingestion mock validation.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot 'Invoke-ABTest.ps1'
$workloadRunner = Join-Path $PSScriptRoot 'Invoke-IngestionWorkload.ps1'
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "fhir-ab-runner-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null

function Assert-Condition {
    param([bool] $Condition, [string] $Message)

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-FailsWith {
    param([scriptblock] $Operation, [string] $Pattern)

    $failure = $null
    try {
        & $Operation
    } catch {
        $failure = $_
    }
    Assert-Condition ($null -ne $failure) "Expected failure matching '$Pattern'."
    Assert-Condition ($failure.Exception.Message -match $Pattern) "Unexpected failure: $($failure.Exception.Message)"
}

function Start-LocalFhirMock {
    param([int] $Port, [string] $EventPath)

    return Start-ThreadJob -ScriptBlock {
        param($Port, $EventPath)

        $listener = [System.Net.HttpListener]::new()
        $listener.Prefixes.Add("http://127.0.0.1:$Port/")
        $listener.Start()
        $jobs = @{}
        $bundleTotals = @{}
        $nextJob = 0

        function Send-Json {
            param($Response, [int] $StatusCode, $Body)

            $bytes = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 10 -Compress))
            $Response.StatusCode = $StatusCode
            $Response.ContentType = 'application/fhir+json'
            $Response.ContentLength64 = $bytes.Length
            $Response.OutputStream.Write($bytes, 0, $bytes.Length)
            $Response.Close()
        }

        while ($listener.IsListening) {
            $context = $listener.GetContext()
            $request = $context.Request
            $response = $context.Response
            $segments = @($request.Url.AbsolutePath.Trim('/') -split '/')
            $mode = $segments[0]

            if ($mode -eq 'shutdown') {
                $response.StatusCode = 204
                $response.Close()
                $listener.Stop()
                break
            }

            if ($request.HttpMethod -eq 'POST' -and $request.ContentType -ne 'application/fhir+json') {
                Send-Json $response 415 @{ issue = @(@{ diagnostics = 'expected application/fhir+json' }) }
                continue
            }

            if ($request.HttpMethod -eq 'POST' -and $segments[-1] -eq '$import') {
                $reader = [IO.StreamReader]::new($request.InputStream, $request.ContentEncoding)
                $body = $reader.ReadToEnd()
                $reader.Dispose()
                $nextJob++
                $isWarmup = $body -match 'warmup'
                $jobs[$nextJob] = @{ Mode = $mode; Count = if ($isWarmup) { 2 } else { 3 } }
                Add-Content -Path $EventPath -Value "import:$mode`:$(if ($isWarmup) { 'warmup' } else { 'measured' })"
                $response.StatusCode = 202
                $response.Headers.Add('Content-Location', "jobs/$nextJob")
                $response.Headers.Add('Content-Location', "jobs/$nextJob")
                $response.Close()
                continue
            }

            if ($request.HttpMethod -eq 'GET' -and $segments.Count -ge 3 -and $segments[1] -eq 'jobs') {
                $job = $jobs[[int]$segments[2]]
                if ($job.Mode -eq 'import-error') {
                    Send-Json $response 200 @{ output = @(@{ count = $job.Count }); error = @(@{ type = 'OperationOutcome' }) }
                } else {
                    Send-Json $response 200 @{ output = @(@{ count = $job.Count }); error = @() }
                }
                continue
            }

            if ($request.HttpMethod -eq 'POST') {
                if ($mode -eq 'bundle-fail') {
                    Send-Json $response 503 @{ issue = @(@{ diagnostics = 'simulated backend failure' }) }
                    continue
                }
                $reader = [IO.StreamReader]::new($request.InputStream, $request.ContentEncoding)
                $bundle = $reader.ReadToEnd() | ConvertFrom-Json
                $reader.Dispose()
                $family = $bundle.entry[0].resource.name[0].family
                $familyKey = "$mode|$family"
                $bundleTotals[$familyKey] = [int]$bundleTotals[$familyKey] + @($bundle.entry).Count
                $entries = @($bundle.entry | ForEach-Object { @{ response = @{ status = '200 OK' } } })
                Send-Json $response 200 @{ resourceType = 'Bundle'; type = 'transaction-response'; entry = $entries }
                continue
            }

            if ($request.HttpMethod -eq 'GET' -and $request.Url.Query -and $segments[-1] -eq 'Patient') {
                if ($mode -eq 'probe-fail') {
                    Send-Json $response 200 @{ resourceType = 'Bundle'; total = 0; entry = @() }
                } elseif ($request.Url.Query -match 'family%3Aexact=|family:exact=') {
                    $token = [uri]::UnescapeDataString((($request.Url.Query.TrimStart('?') -split '&' |
                        Where-Object { $_ -match '^family(?::|%3A)exact=' } |
                        Select-Object -First 1) -split '=', 2)[1])
                    Send-Json $response 200 @{ resourceType = 'Bundle'; total = [int]$bundleTotals["$mode|$token"]; entry = @() }
                } else {
                    Send-Json $response 200 @{
                        resourceType = 'Bundle'
                        total = 1
                        entry = @(@{ resource = @{ resourceType = 'Patient'; id = 'indexed-result' } })
                    }
                }
                continue
            }

            if ($request.HttpMethod -eq 'GET' -and $segments -contains 'Patient') {
                Send-Json $response 200 @{ resourceType = 'Patient'; id = $segments[-1] }
                continue
            }

            Send-Json $response 404 @{ issue = @(@{ diagnostics = 'not found' }) }
        }
    } -ArgumentList $Port, $EventPath
}

try {
    $commonDryRun = @{
        Subscription = 'credential-free'
        ResourceGroupPrefix = 'dryrun'
        DryRun = $true
    }

    $bundlePlanPath = Join-Path $temporaryRoot 'bundle-plan.json'
    & $runner @commonDryRun -ComparisonMode SameImageProvider -Workload Bundle -PlanOutputPath $bundlePlanPath -ValidateCleanupOnFailure | Out-Null
    $bundlePlan = Get-Content $bundlePlanPath -Raw | ConvertFrom-Json
    Assert-Condition ($bundlePlan.control.image -eq $bundlePlan.treatment.image) 'Same-image Bundle plan used different images.'
    Assert-Condition ($bundlePlan.treatment.providers.FhirPath -eq 'Ignixa') 'Bundle plan did not isolate the treatment FHIRPath provider.'

    $importPlanPath = Join-Path $temporaryRoot 'import-plan.json'
    $importDryRun = @{
        ComparisonMode = 'SameImageProvider'
        Workload = 'Import'
        Subscription = 'credential-free'
        ResourceGroupPrefix = 'dryrun'
        ImportInputUrl = 'https://fhirperftest.blob.core.windows.net/input/patients.ndjson'
        ImportResourceType = 'Patient'
        ImportSearchProbe = 'Patient?identifier=known-patient'
        ImportExpectedResourceCount = 3
        ImportStorageAccountUri = 'https://fhirperftest.blob.core.windows.net/'
        ImportStorageAccountResourceId = '/subscriptions/mock/resourceGroups/mock/providers/Microsoft.Storage/storageAccounts/fhirperftest'
        DryRun = $true
        PlanOutputPath = $importPlanPath
    }
    & $runner @importDryRun | Out-Null
    $importPlan = Get-Content $importPlanPath -Raw | ConvertFrom-Json
    foreach ($side in @($importPlan.control, $importPlan.treatment)) {
        Assert-Condition ($side.deploymentCommands.create -match '(?:^| )--system-assigned(?: |$)') 'Container App plan omitted --system-assigned.'
        Assert-Condition ($side.deploymentCommands.importStorageRoleAssignment -match 'Storage Blob Data Contributor') 'Per-app import storage role plan is missing.'
        Assert-Condition ($side.deploymentCommands.systemPrincipalLookup -match 'identity\.principalId') 'System principal lookup is missing.'
    }
    $metadata = Get-Content (Join-Path $importPlan.outputDirectory 'run-metadata.json') -Raw
    $metadataObject = $metadata | ConvertFrom-Json
    Assert-Condition ($metadataObject.schemaVersion -eq 1 -and $metadataObject.images.control -and
        $metadataObject.providers.treatment -and $metadataObject.workload -eq 'Import') 'Run metadata omitted stable provenance fields.'
    Assert-Condition ($metadata -notmatch '(?i)[?&](sig|token|key|se)=') 'Run metadata contains a SAS query parameter.'

    $invalidHost = $importDryRun.Clone()
    $invalidHost.ImportInputUrl = 'https://different.blob.core.windows.net/input/patients.ndjson'
    $invalidHost.Remove('PlanOutputPath')
    Assert-FailsWith { & $runner @invalidHost } 'scheme and host must match'
    $missingWarmup = $importDryRun.Clone()
    $missingWarmup.WarmupIterations = 1
    $missingWarmup.Remove('PlanOutputPath')
    Assert-FailsWith { & $runner @missingWarmup } 'ImportWarmupExpectedResourceCount'
    $typeOnlyProbe = $importDryRun.Clone()
    $typeOnlyProbe.ImportSearchProbe = 'Patient?_count=1'
    $typeOnlyProbe.Remove('PlanOutputPath')
    Assert-FailsWith { & $runner @typeOnlyProbe } '_count-only probes are not allowed'

    $legacyPlanPath = Join-Path $temporaryRoot 'legacy-plan.json'
    & $runner @commonDryRun -PlanOutputPath $legacyPlanPath | Out-Null
    $legacyPlan = Get-Content $legacyPlanPath -Raw | ConvertFrom-Json
    Assert-Condition ($legacyPlan.comparisonMode -eq 'BaselineImageBranch' -and $legacyPlan.workload -eq 'E2E' -and
        $legacyPlan.control.image -ne $legacyPlan.treatment.image -and
        $legacyPlan.reporting.e2eCsvLabels.control -eq 'Baseline' -and
        $legacyPlan.reporting.e2eCsvLabels.treatment -eq 'Branch') 'Legacy E2E defaults or stable labels changed.'

    $tcp = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $tcp.Start()
    $port = ([Net.IPEndPoint]$tcp.LocalEndpoint).Port
    $tcp.Stop()
    $eventPath = Join-Path $temporaryRoot 'mock-events.txt'
    $mockJob = Start-LocalFhirMock -Port $port -EventPath $eventPath
    Start-Sleep -Milliseconds 500

    $bundleOutput = Join-Path $temporaryRoot 'bundle-success'
    & $workloadRunner `
        -ControlUrl "http://127.0.0.1:$port/control/" `
        -TreatmentUrl "http://127.0.0.1:$port/treatment/" `
        -Workload Bundle `
        -OutputDirectory $bundleOutput `
        -ComparisonLabel mock `
        -ControlLabel 'control description' `
        -TreatmentLabel 'treatment description' `
        -WarmupIterations 0 `
        -MeasuredIterations 1 `
        -BundleCount 2 `
        -BundleSize 2 `
        -Concurrency 2 | Out-Null
    Assert-Condition ((Get-Content (Join-Path $bundleOutput 'ingestion-results.json') -Raw | ConvertFrom-Json).Count -eq 2) 'Bundle mock did not produce both result rows.'
    Assert-FailsWith {
        & $workloadRunner `
            -ControlUrl "http://127.0.0.1:$port/bundle-fail/" `
            -TreatmentUrl "http://127.0.0.1:$port/treatment/" `
            -Workload Bundle `
            -OutputDirectory (Join-Path $temporaryRoot 'bundle-failure') `
            -ComparisonLabel mock -ControlLabel control -TreatmentLabel treatment `
            -WarmupIterations 0 -MeasuredIterations 1 -BundleCount 1 -BundleSize 1 -Concurrency 1
    } 'representative error: HTTP 503,.*simulated backend failure'

    $importArguments = @{
        ControlUrl = "http://127.0.0.1:$port/control/"
        TreatmentUrl = "http://127.0.0.1:$port/treatment/"
        Workload = 'Import'
        OutputDirectory = (Join-Path $temporaryRoot 'import-success')
        ComparisonLabel = 'mock'
        ControlLabel = 'control description'
        TreatmentLabel = 'treatment description'
        WarmupIterations = 1
        MeasuredIterations = 1
        ImportInputUrl = 'https://fhirperftest.blob.core.windows.net/input/measured.ndjson'
        ImportResourceType = 'Patient'
        ImportSearchProbe = 'Patient?identifier=measured'
        ImportExpectedResourceCount = 3
        ImportWarmupInputUrl = 'https://fhirperftest.blob.core.windows.net/input/warmup.ndjson'
        ImportWarmupResourceType = 'Patient'
        ImportWarmupSearchProbe = 'Patient?identifier=warmup'
        ImportWarmupExpectedResourceCount = 2
        ImportTimeoutMinutes = 1
        ImportPollIntervalSeconds = 0.1
    }
    & $workloadRunner @importArguments | Out-Null
    $events = @(Get-Content $eventPath)
    Assert-Condition (@($events | Where-Object { $_ -match ':warmup$' }).Count -eq 2 -and
        @($events | Where-Object { $_ -match ':measured$' }).Count -eq 2) 'Real warm-up and measured imports did not execute on both sides.'

    $errorImport = $importArguments.Clone()
    $errorImport.ControlUrl = "http://127.0.0.1:$port/import-error/"
    $errorImport.OutputDirectory = Join-Path $temporaryRoot 'import-error'
    $errorImport.WarmupIterations = 0
    Assert-FailsWith { & $workloadRunner @errorImport } 'errorEntries=1, failures=0'
    $failedProbe = $importArguments.Clone()
    $failedProbe.ControlUrl = "http://127.0.0.1:$port/probe-fail/"
    $failedProbe.OutputDirectory = Join-Path $temporaryRoot 'probe-error'
    $failedProbe.WarmupIterations = 0
    Assert-FailsWith { & $workloadRunner @failedProbe } 'indexed correctness probe failed.*no Patient result'

    Write-Host 'Credential-free deployment plans and local Bundle/Import mocks passed.' -ForegroundColor Green
} finally {
    if ($mockJob) {
        Invoke-WebRequest -Uri "http://127.0.0.1:$port/shutdown/" -SkipHttpErrorCheck -TimeoutSec 2 -ErrorAction SilentlyContinue | Out-Null
        Stop-Job -Job $mockJob -ErrorAction SilentlyContinue
        Remove-Job -Job $mockJob -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -Path $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
}
