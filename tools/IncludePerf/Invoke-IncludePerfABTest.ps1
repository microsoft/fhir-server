<#
.SYNOPSIS
    Runs the _include / _revinclude A/B benchmark against a baseline and a branch FHIR service.

.DESCRIPTION
    Executes the benchmark catalog against both endpoints in ALTERNATING rounds
    (baseline, branch, baseline, branch, ...) and then compares the results.

    Alternating matters. Both services are intentionally pointed at the SAME database, which removes
    data, statistics and fragmentation differences as sources of false deltas, but it also means the two
    services share a buffer pool: whichever runs first warms the cache for the other. Alternating and
    taking the best round per case cancels that ordering bias, and it also absorbs the one-sided noise of
    a shared cloud environment (interference only ever makes a sample slower).

    Runs are deliberately sequential, never parallel, so the two services never contend for the same SQL
    resources while being measured.

.EXAMPLE
    ./Invoke-IncludePerfABTest.ps1 `
        -BaselineEndpoint https://fhir-baseline.example.azurecontainerapps.io `
        -BranchEndpoint   https://fhir-branch.example.azurecontainerapps.io `
        -ManifestPath     C:\perfdata-large\manifest.json `
        -Rounds 3 -Iterations 25
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $BaselineEndpoint,

    [Parameter(Mandatory = $true)]
    [string] $BranchEndpoint,

    [Parameter(Mandatory = $true)]
    [string] $ManifestPath,

    [Parameter(Mandatory = $false)]
    [string] $OutputDirectory,

    [Parameter(Mandatory = $false)]
    [ValidateRange(1, 10)]
    [int] $Rounds = 3,

    [Parameter(Mandatory = $false)]
    [int] $Iterations = 25,

    [Parameter(Mandatory = $false)]
    [int] $Warmup = 5,

    [Parameter(Mandatory = $false)]
    [double] $NoiseThresholdPercent = 15.0
)

$ErrorActionPreference = 'Stop'

$repoRoot = (git rev-parse --show-toplevel 2>$null)
if (-not $repoRoot) { $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot "include-perf-results/$(Get-Date -Format 'yyyyMMddHHmmss')"
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$benchmarkProject = Join-Path $PSScriptRoot 'FhirIncludeBenchmark'
$benchmarkDll = Join-Path $benchmarkProject 'bin/Release/net10.0/Microsoft.Health.Internal.Fhir.IncludePerf.Benchmark.dll'

if (-not (Test-Path $benchmarkDll)) {
    Write-Host "Building benchmark tool..." -ForegroundColor Cyan
    dotnet build $benchmarkProject -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Failed to build the benchmark tool." }
}

Write-Host ""
Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host " _include / _revinclude A/B benchmark" -ForegroundColor Cyan
Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host " Baseline : $BaselineEndpoint"
Write-Host " Branch   : $BranchEndpoint"
Write-Host " Rounds   : $Rounds  (alternating)"
Write-Host " Iters    : $Iterations (+$Warmup warmup) per case per round"
Write-Host " Output   : $OutputDirectory"
Write-Host ""

$baselineFiles = @()
$branchFiles = @()

for ($round = 1; $round -le $Rounds; $round++) {
    foreach ($side in @('baseline', 'branch')) {
        $endpoint = if ($side -eq 'baseline') { $BaselineEndpoint } else { $BranchEndpoint }
        $outFile = Join-Path $OutputDirectory "$side-round$round.json"

        Write-Host "--- Round $round / $Rounds : $side ---" -ForegroundColor Yellow

        & dotnet $benchmarkDll `
            --endpoint $endpoint `
            --label "$side-round$round" `
            --manifest $ManifestPath `
            --output $outFile `
            --iterations $Iterations `
            --warmup $Warmup

        if ($LASTEXITCODE -ne 0) { throw "Benchmark run failed for $side round $round." }

        if ($side -eq 'baseline') { $baselineFiles += $outFile } else { $branchFiles += $outFile }
        Write-Host ""
    }
}

$reportPath = Join-Path $OutputDirectory 'comparison-report.md'

& (Join-Path $PSScriptRoot 'Compare-IncludeBenchmark.ps1') `
    -BaselinePath $baselineFiles `
    -BranchPath $branchFiles `
    -OutputPath $reportPath `
    -NoiseThresholdPercent $NoiseThresholdPercent

Write-Host ""
Write-Host "Done. Report: $reportPath" -ForegroundColor Green
