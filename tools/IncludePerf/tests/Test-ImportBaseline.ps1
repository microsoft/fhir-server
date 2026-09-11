[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path "$PSScriptRoot/../../..").Path
$baselineScriptPath = Join-Path $repositoryRoot 'tools/IncludePerf/Invoke-ImportBaseline.ps1'
if ($null -eq ('System.Net.Http.HttpResponseMessage' -as [type])) {
    Add-Type -AssemblyName System.Net.Http
}

. $baselineScriptPath

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)] $Expected,
        [Parameter(Mandatory = $true)] $Actual,
        [Parameter(Mandatory = $true)] [string] $Description
    )

    if ($Expected -ne $Actual) {
        throw "$Description. Expected '$Expected', actual '$Actual'."
    }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)] [scriptblock] $Action,
        [Parameter(Mandatory = $true)] [string] $ExpectedMessage,
        [Parameter(Mandatory = $true)] [string] $Description
    )

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notlike "*$ExpectedMessage*") {
            throw "$Description. Expected error containing '$ExpectedMessage', actual '$($_.Exception.Message)'."
        }

        return
    }

    throw "$Description. Expected an exception."
}

$baselineScript = Get-Content -LiteralPath $baselineScriptPath -Raw
Assert-Equal -Expected $true -Actual ($baselineScript -match '\$response\.Content\.Headers\.ContentLocation') `
    -Description 'Import submission did not read the Content-Location response content header'
Assert-Equal -Expected $true -Actual ($baselineScript -match 'Get-FileHash\b' -and $baselineScript -match '-Algorithm\s+SHA256') `
    -Description 'Manifest hashing did not use the Windows PowerShell-compatible Get-FileHash cmdlet'
Assert-Equal -Expected $false -Actual ($baselineScript -match 'SHA256\]::HashData|Convert\]::ToHexString') `
    -Description 'Manifest hashing retained .NET Core-only hash APIs'

$submissionResponse = [Net.Http.HttpResponseMessage]::new([Net.HttpStatusCode]::Accepted)
try {
    $submissionResponse.Headers.Location = [Uri]'https://fhir.example.test/legacy-status'
    $submissionResponse.Content = [Net.Http.StringContent]::new('')
    $submissionResponse.Content.Headers.ContentLocation = [Uri]'https://fhir.example.test/$import/status/42'

    Assert-Equal -Expected 'https://fhir.example.test/$import/status/42' `
        -Actual (Get-ImportStatusLocation -Response $submissionResponse) `
        -Description 'Import submission did not prefer Content-Location over Location'
}
finally {
    $submissionResponse.Dispose()
}

function New-ValidReport {
    param(
        [string] $ImportProvider = 'Firely',
        [string] $ImportEvidenceSource = 'operator-provided configuration snapshot',
        [string] $CorpusHash = '7c3a3a8b5b09cbb09b7ad78c0f0edab0ebda1820d0db63128d4a3175b49c03f1'
    )

    return [pscustomobject]@{
        SchemaVersion = '1.0'
        Corpus = [pscustomobject]@{
            Identity = 'synthetic-small'
            Sha256 = $CorpusHash
            TotalResources = 2
            InputFileCount = 1
            ResourceTypeCounts = [ordered]@{ Patient = 2 }
        }
        FhirVersion = 'R4'
        Binary = [pscustomobject]@{
            Identity = 'sha256:server-image'
            Kind = 'container-image'
        }
        ProviderConfiguration = [pscustomobject]@{
            Import = [pscustomobject]@{
                EffectiveProvider = $ImportProvider
                EvidenceSource = $ImportEvidenceSource
            }
            FhirPath = [pscustomobject]@{
                EffectiveProvider = 'Firely'
                EvidenceSource = 'operator-provided configuration snapshot'
            }
        }
        Environment = [pscustomobject]@{
            Backend = 'SqlServer'
            Identity = 'isolated-store-a'
            LoadProfile = 'single-client'
            PreparedStoreIdentity = 'store-snapshot-42'
        }
        Execution = [pscustomobject]@{
            Repetitions = 1
            TimingWindow = [pscustomobject]@{
                RequestSubmittedUtc = '2026-09-11T01:00:00.0000000Z'
                TerminalUtc = '2026-09-11T01:00:01.2345678Z'
                WallClockMilliseconds = 1234.5678
            }
            Counts = [pscustomobject]@{
                RequestedResourceCount = 2
                SuccessfulResourceCount = 2
                ErrorResourceCount = 0
            }
            TerminalOutcome = 'Succeeded'
        }
        ServerMetrics = [pscustomobject]@{
            Cpu = [pscustomobject]@{ Status = 'unavailable'; Value = $null }
            AllocationRateBytesPerSecond = [pscustomobject]@{ Status = 'unavailable'; Value = $null }
            PeakWorkingSetBytes = [pscustomobject]@{ Status = 'unavailable'; Value = $null }
        }
    }
}

$valid = New-ValidReport
Assert-Equal -Expected $true -Actual (Test-ImportBaselineReport -Report $valid) -Description 'A complete successful report should validate'

$missingEvidence = New-ValidReport
$missingEvidence.ProviderConfiguration.Import.EvidenceSource = ''
Assert-Throws -Action {
    Test-ImportBaselineReport -Report $missingEvidence
} -ExpectedMessage 'EvidenceSource' -Description 'A report without import provider evidence was accepted'

$missingMetricStatus = New-ValidReport
$missingMetricStatus.ServerMetrics.Cpu = [pscustomobject]@{ Value = $null }
Assert-Throws -Action {
    Test-ImportBaselineReport -Report $missingMetricStatus
} -ExpectedMessage 'ServerMetrics.Cpu.Status' -Description 'A report without explicit server metric availability was accepted'

$errorReport = New-ValidReport
$errorReport.Execution.Counts.ErrorResourceCount = 1
Assert-Throws -Action {
    Test-ImportBaselineReport -Report $errorReport
} -ExpectedMessage 'cannot be Succeeded' -Description 'A report with import errors was accepted as successful'

$candidate = New-ValidReport -ImportProvider 'Ignixa'
Assert-Equal -Expected $true -Actual (Test-ImportBaselineComparison -BaselineReport $valid -CandidateReport $candidate -SelectedCapability 'Import') -Description 'Reports with only the selected import provider changed should be comparable'
$candidate.Execution.TimingWindow.WallClockMilliseconds = 2345.6789
$baselineReportPath = [IO.Path]::GetTempFileName()
$candidateReportPath = [IO.Path]::GetTempFileName()
try {
    $valid | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $baselineReportPath -Encoding utf8
    $candidate | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $candidateReportPath -Encoding utf8
    $comparison = Compare-ImportBaselineReports -BaselineReportPath $baselineReportPath -CandidateReportPath $candidateReportPath -SelectedCapability 'Import' | ConvertFrom-Json
    Assert-Equal -Expected 1234.5678 -Actual $comparison.BaselineWallClockMilliseconds -Description 'The comparison rounded the baseline timing'
    Assert-Equal -Expected 2345.6789 -Actual $comparison.CandidateWallClockMilliseconds -Description 'The comparison rounded the candidate timing'
}
finally {
    Remove-Item -LiteralPath $baselineReportPath, $candidateReportPath -Force -ErrorAction SilentlyContinue
}

$sameProvider = New-ValidReport
Assert-Throws -Action {
    Test-ImportBaselineComparison -BaselineReport $valid -CandidateReport $sameProvider -SelectedCapability 'Import'
} -ExpectedMessage 'selected provider is unchanged' -Description 'Reports without a selected provider change were compared'

$differentCorpus = New-ValidReport -ImportProvider 'Ignixa' -CorpusHash '2c8d3bca3ff822f3b9a37c90bc7ce8e028ef20ab5f1e0d269956074d29b17a8c'
Assert-Throws -Action {
    Test-ImportBaselineComparison -BaselineReport $valid -CandidateReport $differentCorpus -SelectedCapability 'Import'
} -ExpectedMessage 'Corpus.Sha256' -Description 'Reports with different corpora were compared'

$differentResourceTypeCounts = New-ValidReport -ImportProvider 'Ignixa'
$differentResourceTypeCounts.Corpus.ResourceTypeCounts.Patient = 1
Assert-Throws -Action {
    Test-ImportBaselineComparison -BaselineReport $valid -CandidateReport $differentResourceTypeCounts -SelectedCapability 'Import'
} -ExpectedMessage 'Corpus.ResourceTypeCounts' -Description 'Reports with different corpus resource counts were compared'

$unverifiedProvider = New-ValidReport -ImportProvider 'Ignixa' -ImportEvidenceSource ''
Assert-Throws -Action {
    Test-ImportBaselineComparison -BaselineReport $valid -CandidateReport $unverifiedProvider -SelectedCapability 'Import'
} -ExpectedMessage 'EvidenceSource' -Description 'Provider-unverified reports were compared'

$differentBinary = New-ValidReport -ImportProvider 'Ignixa'
$differentBinary.Binary.Identity = 'sha256:other-server-image'
Assert-Throws -Action {
    Test-ImportBaselineComparison -BaselineReport $valid -CandidateReport $differentBinary -SelectedCapability 'Import'
} -ExpectedMessage 'Binary.Identity' -Description 'Reports for different binaries were compared'

$missingBinary = New-ValidReport
$missingBinary.Binary.Identity = ''
Assert-Throws -Action {
    Test-ImportBaselineReport -Report $missingBinary
} -ExpectedMessage 'Binary.Identity' -Description 'A report without binary identity was accepted'

$mismatchedCounts = New-ValidReport
$mismatchedCounts.Execution.Counts.SuccessfulResourceCount = 1
Assert-Throws -Action {
    Test-ImportBaselineReport -Report $mismatchedCounts
} -ExpectedMessage 'account for every requested resource' -Description 'A successful report with mismatched resource counts was accepted'

$mismatchedCorpusCounts = New-ValidReport
$mismatchedCorpusCounts.Corpus.ResourceTypeCounts.Patient = 1
Assert-Throws -Action {
    Test-ImportBaselineReport -Report $mismatchedCorpusCounts
} -ExpectedMessage 'Corpus.ResourceTypeCounts' -Description 'A report with mismatched corpus resource counts was accepted'

$timedOut = New-ValidReport -ImportProvider 'Ignixa'
$timedOut.Execution.TerminalOutcome = 'TimedOut'
Assert-Throws -Action {
    Test-ImportBaselineComparison -BaselineReport $valid -CandidateReport $timedOut -SelectedCapability 'Import'
} -ExpectedMessage 'successfully completed' -Description 'A timed-out run was compared'

Assert-Equal -Expected 'https://fhir.example.test/$import/status/42' -Actual (Resolve-ImportStatusUri -Endpoint 'https://fhir.example.test' -StatusLocation '/$import/status/42') -Description 'A same-origin relative status URI was not resolved'
Assert-Throws -Action {
    Resolve-ImportStatusUri -Endpoint 'https://fhir.example.test' -StatusLocation 'https://untrusted.example.test/status/42'
} -ExpectedMessage 'same origin' -Description 'A cross-origin status URI was accepted'
Assert-Equal -Expected 'Import request, status polling, or terminal response validation failed.' `
    -Actual (Get-SafeErrorSummary -Exception ([Exception]::new('Bearer secret-token failed for https://storage.example.test/file?sig=secret with {"resourceType":"Patient"}'))) `
    -Description 'An external exception leaked sensitive request or customer-body data into a report'

$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) "import-baseline-test-$([Guid]::NewGuid())"
New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
try {
    $manifestPath = Join-Path $temporaryDirectory 'manifest.json'
    $planPath = Join-Path $temporaryDirectory 'plan.json'
    '{"totalResources":2,"resourceTypes":[{"type":"Patient","count":2,"files":["patients.ndjson"]}]}' | Set-Content -LiteralPath $manifestPath -Encoding utf8

    & (Join-Path $repositoryRoot 'tools/IncludePerf/Invoke-ImportBaseline.ps1') `
        -ManifestPath $manifestPath -OutputPath $planPath -FhirVersion R4 -BinaryIdentity 'sha256:server-image' `
        -ImportProvider Firely -ImportProviderEvidenceSource 'operator snapshot' `
        -FhirPathProvider Firely -FhirPathProviderEvidenceSource 'operator snapshot' `
        -Backend SqlServer -EnvironmentIdentity 'isolated-store-a' -LoadProfile 'single-client' `
        -PreparedStoreIdentity 'store-snapshot-42'

    $plan = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json
    Assert-Equal -Expected 'Planned' -Actual $plan.Execution.TerminalOutcome -Description 'The default invocation made a live import request'
    Assert-Equal -Expected 'unavailable' -Actual $plan.ServerMetrics.Cpu.Status -Description 'The offline plan fabricated a server CPU metric'
    Assert-Equal -Expected ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()) `
        -Actual $plan.Corpus.Sha256 -Description 'The offline plan did not report the manifest SHA-256 hash'

    Assert-Throws -Action {
        & (Join-Path $repositoryRoot 'tools/IncludePerf/Invoke-ImportBaseline.ps1') `
            -ManifestPath $manifestPath -OutputPath $planPath -FhirVersion R4 -BinaryIdentity 'sha256:server-image' `
            -ImportProvider Firely -ImportProviderEvidenceSource 'operator snapshot' `
            -FhirPathProvider Firely -FhirPathProviderEvidenceSource 'operator snapshot' `
            -Backend SqlServer -EnvironmentIdentity 'isolated-store-a' -LoadProfile 'single-client' `
            -PreparedStoreIdentity 'store-snapshot-42' -Repetitions 2
    } -ExpectedMessage 'one import attempt' -Description 'A single report claimed multiple import attempts'
}
finally {
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Import baseline tooling tests passed.'
