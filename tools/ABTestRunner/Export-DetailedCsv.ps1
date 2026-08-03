<#
.SYNOPSIS
    Exports detailed test results to CSV with per-test statistics for both services.

.DESCRIPTION
    Parses all TRX files from baseline and branch test runs and produces a CSV
    containing every test with min, max, average, standard deviation of duration,
    outcome counts, and pass rate for each service. Designed for detailed analysis
    alongside the summary markdown report.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]] $BaselineTrxPaths,

    [Parameter(Mandatory = $true)]
    [string[]] $BranchTrxPaths,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [Parameter(Mandatory = $false)]
    [string] $BaselineLabel = 'Baseline',

    [Parameter(Mandatory = $false)]
    [string] $BranchLabel = 'Branch'
)

$ErrorActionPreference = 'Stop'

# ─────────────────────────────────────────────────────────────────────────────
# TRX Parsing (same logic as Compare-TestResults)
# ─────────────────────────────────────────────────────────────────────────────

function Parse-TrxFile {
    param([string] $Path)

    if (-not (Test-Path $Path)) {
        Write-Warning "TRX file not found: $Path"
        return @{}
    }

    [xml]$trx = Get-Content $Path -Raw
    $ns = @{ t = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010' }
    $results = @{}

    $unitResults = Select-Xml -Xml $trx -XPath '//t:UnitTestResult' -Namespace $ns
    foreach ($node in $unitResults) {
        $el = $node.Node
        $testName = $el.testName
        $outcome = $el.outcome
        $duration = [TimeSpan]::Zero

        if ($el.duration) {
            try { $duration = [TimeSpan]::Parse($el.duration) } catch { }
        }

        $results[$testName] = @{
            TestName   = $testName
            Outcome    = $outcome
            DurationMs = $duration.TotalMilliseconds
        }
    }

    return $results
}

# ─────────────────────────────────────────────────────────────────────────────
# Collect per-test statistics across iterations
# ─────────────────────────────────────────────────────────────────────────────

function Get-TestStats {
    param([string[]] $Paths)

    $allRuns = @()
    foreach ($p in $Paths) {
        $parsed = Parse-TrxFile -Path $p
        if ($parsed.Count -gt 0) { $allRuns += @($parsed) }
    }

    if ($allRuns.Count -eq 0) { return @{} }

    $allNames = $allRuns | ForEach-Object { $_.Keys } | Sort-Object -Unique
    $stats = @{}

    foreach ($testName in $allNames) {
        $durations = @()
        $outcomes = @()

        foreach ($run in $allRuns) {
            if ($run.ContainsKey($testName)) {
                $r = $run[$testName]
                $durations += $r.DurationMs
                $outcomes += $r.Outcome
            }
        }

        $measure = $durations | Measure-Object -Minimum -Maximum -Average
        $avg = $measure.Average
        $min = $measure.Minimum
        $max = $measure.Maximum
        $count = $measure.Count

        # Standard deviation
        $stdDev = 0.0
        if ($count -gt 1) {
            $sumSquares = ($durations | ForEach-Object { ($_ - $avg) * ($_ - $avg) } | Measure-Object -Sum).Sum
            $stdDev = [Math]::Sqrt($sumSquares / ($count - 1))
        }

        $passCount = ($outcomes | Where-Object { $_ -eq 'Passed' }).Count
        $failCount = $count - $passCount
        $passRate = if ($count -gt 0) { [Math]::Round(($passCount / $count) * 100, 1) } else { 0 }

        $stats[$testName] = @{
            Iterations = $count
            MinMs      = $min
            MaxMs      = $max
            AvgMs      = $avg
            StdDevMs   = $stdDev
            PassCount  = $passCount
            FailCount  = $failCount
            PassRate   = $passRate
        }
    }

    return $stats
}

# ─────────────────────────────────────────────────────────────────────────────
# Generate statistics
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "  Generating detailed CSV report..."
Write-Host "    Parsing baseline TRX files ($($BaselineTrxPaths.Count))..."
$baselineStats = Get-TestStats -Paths $BaselineTrxPaths

Write-Host "    Parsing branch TRX files ($($BranchTrxPaths.Count))..."
$branchStats = Get-TestStats -Paths $BranchTrxPaths

# ─────────────────────────────────────────────────────────────────────────────
# Build CSV rows
# ─────────────────────────────────────────────────────────────────────────────

$allTestNames = @($baselineStats.Keys) + @($branchStats.Keys) | Sort-Object -Unique

$rows = [System.Collections.Generic.List[psobject]]::new()

foreach ($testName in $allTestNames) {
    $bl = $baselineStats[$testName]
    $br = $branchStats[$testName]

    $row = [ordered]@{
        TestName                              = $testName
        "${BaselineLabel}_Iterations"         = if ($bl) { $bl.Iterations } else { 0 }
        "${BaselineLabel}_AvgMs"              = if ($bl) { [Math]::Round($bl.AvgMs, 2) } else { '' }
        "${BaselineLabel}_MinMs"              = if ($bl) { [Math]::Round($bl.MinMs, 2) } else { '' }
        "${BaselineLabel}_MaxMs"              = if ($bl) { [Math]::Round($bl.MaxMs, 2) } else { '' }
        "${BaselineLabel}_StdDevMs"           = if ($bl) { [Math]::Round($bl.StdDevMs, 2) } else { '' }
        "${BaselineLabel}_PassCount"          = if ($bl) { $bl.PassCount } else { 0 }
        "${BaselineLabel}_FailCount"          = if ($bl) { $bl.FailCount } else { 0 }
        "${BaselineLabel}_PassRate"           = if ($bl) { $bl.PassRate } else { '' }
        "${BranchLabel}_Iterations"           = if ($br) { $br.Iterations } else { 0 }
        "${BranchLabel}_AvgMs"               = if ($br) { [Math]::Round($br.AvgMs, 2) } else { '' }
        "${BranchLabel}_MinMs"               = if ($br) { [Math]::Round($br.MinMs, 2) } else { '' }
        "${BranchLabel}_MaxMs"               = if ($br) { [Math]::Round($br.MaxMs, 2) } else { '' }
        "${BranchLabel}_StdDevMs"            = if ($br) { [Math]::Round($br.StdDevMs, 2) } else { '' }
        "${BranchLabel}_PassCount"           = if ($br) { $br.PassCount } else { 0 }
        "${BranchLabel}_FailCount"           = if ($br) { $br.FailCount } else { 0 }
        "${BranchLabel}_PassRate"            = if ($br) { $br.PassRate } else { '' }
        AvgMs_Diff                            = if ($bl -and $br) { [Math]::Round($br.AvgMs - $bl.AvgMs, 2) } else { '' }
        AvgMs_DiffPercent                     = if ($bl -and $br -and $bl.AvgMs -gt 0) { [Math]::Round((($br.AvgMs - $bl.AvgMs) / $bl.AvgMs) * 100, 2) } else { '' }
        OutcomeDiff                           = if ($bl -and $br) {
            if ($bl.PassRate -eq $br.PassRate) { 'Same' }
            elseif ($br.PassRate -gt $bl.PassRate) { 'Improved' }
            else { 'Regressed' }
        } elseif ($br -and -not $bl) { 'New' }
        elseif ($bl -and -not $br) { 'Removed' }
        else { '' }
    }

    $rows.Add([pscustomobject]$row)
}

# ─────────────────────────────────────────────────────────────────────────────
# Export
# ─────────────────────────────────────────────────────────────────────────────

$rows | Export-Csv -Path $OutputPath -NoTypeInformation -Encoding UTF8
Write-Host "    ✓ Detailed CSV report: $OutputPath ($($rows.Count) tests)"
