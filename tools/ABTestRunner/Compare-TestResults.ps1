<#
.SYNOPSIS
    Compares .trx test result files and generates a markdown report
    highlighting differences between baseline and branch test runs.

.DESCRIPTION
    Parses Visual Studio .trx (XML) result files, compares test outcomes and
    durations, and produces a markdown report focused on:
    1. Failures unique to one run (most important)
    2. Latency regressions and improvements
    3. Overall summary statistics

    When multiple TRX files are provided per side (from multiple iterations),
    durations are averaged to reduce noise from timing variability.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string] $BaselineTrxPath,

    [Parameter(Mandatory = $false)]
    [string[]] $BaselineTrxPaths,

    [Parameter(Mandatory = $false)]
    [string] $BranchTrxPath,

    [Parameter(Mandatory = $false)]
    [string[]] $BranchTrxPaths,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [Parameter(Mandatory = $false)]
    [string] $BaselineLabel = 'Baseline (main)',

    [Parameter(Mandatory = $false)]
    [string] $BranchLabel = 'Branch',

    [Parameter(Mandatory = $false)]
    [double] $LatencyThresholdPercent = 20.0
)

$ErrorActionPreference = 'Stop'

# Normalize paths: support both single-path and multi-path parameters
if (-not $BaselineTrxPaths -and $BaselineTrxPath) { $BaselineTrxPaths = @($BaselineTrxPath) }
if (-not $BranchTrxPaths -and $BranchTrxPath) { $BranchTrxPaths = @($BranchTrxPath) }

if (-not $BaselineTrxPaths -or -not $BranchTrxPaths) {
    throw "At least one baseline and one branch TRX path must be provided."
}

$iterationCount = [Math]::Max($BaselineTrxPaths.Count, $BranchTrxPaths.Count)

# ─────────────────────────────────────────────────────────────────────────────
# TRX Parsing
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

        $errorMessage = $null
        $errorStackTrace = $null
        $outputNode = $el.Output
        if ($null -ne $outputNode) {
            $errorInfoNode = $outputNode.ErrorInfo
            if ($null -ne $errorInfoNode) {
                $errorMessage = $errorInfoNode.Message
                $errorStackTrace = $errorInfoNode.StackTrace
            }
        }

        $results[$testName] = @{
            TestName    = $testName
            Outcome     = $outcome
            Duration    = $duration
            DurationMs  = $duration.TotalMilliseconds
            Error       = $errorMessage
            StackTrace  = $errorStackTrace
        }
    }

    return $results
}

# ─────────────────────────────────────────────────────────────────────────────
# Parse all TRX files and aggregate (average durations, majority-vote outcomes)
# ─────────────────────────────────────────────────────────────────────────────

function Merge-TrxResults {
    param([string[]] $Paths)

    $allRuns = @()
    foreach ($p in $Paths) {
        Write-Host "  Parsing: $p"
        $allRuns += @(Parse-TrxFile -Path $p)
    }

    if ($allRuns.Count -eq 0) { return @{} }
    if ($allRuns.Count -eq 1) { return $allRuns[0] }

    # Gather all test names across iterations
    $allNames = $allRuns | ForEach-Object { $_.Keys } | Sort-Object -Unique
    $merged = @{}

    foreach ($testName in $allNames) {
        $durations = @()
        $outcomes = @()
        $lastError = $null
        $lastStack = $null

        foreach ($run in $allRuns) {
            if ($run.ContainsKey($testName)) {
                $r = $run[$testName]
                $durations += $r.DurationMs
                $outcomes += $r.Outcome
                if ($r.Error) { $lastError = $r.Error }
                if ($r.StackTrace) { $lastStack = $r.StackTrace }
            }
        }

        # Average duration
        $avgMs = if ($durations.Count -gt 0) { ($durations | Measure-Object -Average).Average } else { 0 }

        # Outcome: if it failed in ANY iteration, mark as failed (conservative)
        $failCount = ($outcomes | Where-Object { $_ -ne 'Passed' }).Count
        $finalOutcome = if ($failCount -gt 0) { 'Failed' } else { 'Passed' }

        $merged[$testName] = @{
            TestName       = $testName
            Outcome        = $finalOutcome
            Duration       = [TimeSpan]::FromMilliseconds($avgMs)
            DurationMs     = $avgMs
            Error          = $lastError
            StackTrace     = $lastStack
            IterationCount = $durations.Count
            FailCount      = $failCount
        }
    }

    return $merged
}

Write-Host "Parsing baseline results ($($BaselineTrxPaths.Count) file(s)):"
$baselineResults = Merge-TrxResults -Paths $BaselineTrxPaths

Write-Host "Parsing branch results ($($BranchTrxPaths.Count) file(s)):"
$branchResults = Merge-TrxResults -Paths $BranchTrxPaths

if ($baselineResults.Count -eq 0 -and $branchResults.Count -eq 0) {
    Write-Warning "Both TRX files are empty or missing. Cannot generate comparison."
    "# A/B Test Comparison Report`n`n⚠️ No test results found in either TRX file." | Set-Content $OutputPath
    return
}

# ─────────────────────────────────────────────────────────────────────────────
# Classify results
# ─────────────────────────────────────────────────────────────────────────────

$allTestNames = @($baselineResults.Keys) + @($branchResults.Keys) | Sort-Object -Unique

$branchOnlyFailures = @()    # Failed in branch but passed in baseline
$baselineOnlyFailures = @()  # Failed in baseline but passed in branch (regressions fixed)
$bothFailed = @()            # Failed in both
$latencyRegressions = @()    # Significantly slower in branch
$latencyImprovements = @()   # Significantly faster in branch
$newTests = @()              # Only in branch
$removedTests = @()          # Only in baseline

$baselinePassed = 0
$baselineFailed = 0
$branchPassed = 0
$branchFailed = 0

foreach ($testName in $allTestNames) {
    $bResult = $baselineResults[$testName]
    $brResult = $branchResults[$testName]

    $inBaseline = $null -ne $bResult
    $inBranch = $null -ne $brResult

    if (-not $inBaseline -and $inBranch) {
        $newTests += $brResult
        if ($brResult.Outcome -eq 'Passed') { $branchPassed++ } else { $branchFailed++ }
        continue
    }

    if ($inBaseline -and -not $inBranch) {
        $removedTests += $bResult
        if ($bResult.Outcome -eq 'Passed') { $baselinePassed++ } else { $baselineFailed++ }
        continue
    }

    # Both present
    if ($bResult.Outcome -eq 'Passed') { $baselinePassed++ } else { $baselineFailed++ }
    if ($brResult.Outcome -eq 'Passed') { $branchPassed++ } else { $branchFailed++ }

    $baselineFailed_test = $bResult.Outcome -ne 'Passed'
    $branchFailed_test = $brResult.Outcome -ne 'Passed'

    if ($branchFailed_test -and -not $baselineFailed_test) {
        $branchOnlyFailures += @{ Baseline = $bResult; Branch = $brResult }
    } elseif ($baselineFailed_test -and -not $branchFailed_test) {
        $baselineOnlyFailures += @{ Baseline = $bResult; Branch = $brResult }
    } elseif ($baselineFailed_test -and $branchFailed_test) {
        $bothFailed += @{ Baseline = $bResult; Branch = $brResult }
    }

    # Latency comparison (only for tests that passed in both)
    if (-not $baselineFailed_test -and -not $branchFailed_test) {
        $bMs = $bResult.DurationMs
        $brMs = $brResult.DurationMs

        if ($bMs -gt 0) {
            $diffPercent = (($brMs - $bMs) / $bMs) * 100.0
            if ($diffPercent -gt $LatencyThresholdPercent) {
                $latencyRegressions += @{
                    TestName       = $testName
                    BaselineMs     = $bMs
                    BranchMs       = $brMs
                    DiffPercent    = $diffPercent
                }
            } elseif ($diffPercent -lt -$LatencyThresholdPercent) {
                $latencyImprovements += @{
                    TestName       = $testName
                    BaselineMs     = $bMs
                    BranchMs       = $brMs
                    DiffPercent    = $diffPercent
                }
            }
        }
    }
}

# Sort latency by magnitude
$latencyRegressions = $latencyRegressions | Sort-Object { -$_.DiffPercent }
$latencyImprovements = $latencyImprovements | Sort-Object { $_.DiffPercent }

# ─────────────────────────────────────────────────────────────────────────────
# Generate markdown report
# ─────────────────────────────────────────────────────────────────────────────

$sb = [System.Text.StringBuilder]::new()

[void]$sb.AppendLine("# A/B E2E Test Comparison Report")
[void]$sb.AppendLine("")
if ($iterationCount -gt 1) {
    [void]$sb.AppendLine("> **$iterationCount iterations** per side — durations are averaged to reduce timing noise.")
    [void]$sb.AppendLine("")
}
[void]$sb.AppendLine("| | $BaselineLabel | $BranchLabel |")
[void]$sb.AppendLine("|---|---|---|")
[void]$sb.AppendLine("| **Total Tests** | $($baselineResults.Count) | $($branchResults.Count) |")
[void]$sb.AppendLine("| **Passed** | $baselinePassed | $branchPassed |")
[void]$sb.AppendLine("| **Failed** | $baselineFailed | $branchFailed |")
[void]$sb.AppendLine("")

# Verdict
if ($branchOnlyFailures.Count -gt 0) {
    [void]$sb.AppendLine("## ⚠️ Verdict: REGRESSIONS DETECTED")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("The branch introduces **$($branchOnlyFailures.Count) new failure(s)** not present in the baseline.")
} elseif ($baselineOnlyFailures.Count -gt 0) {
    [void]$sb.AppendLine("## ✅ Verdict: IMPROVEMENTS")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("The branch fixes **$($baselineOnlyFailures.Count) failure(s)** that exist in the baseline.")
} else {
    [void]$sb.AppendLine("## ✅ Verdict: NO REGRESSIONS")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("Both services produce identical pass/fail outcomes.")
}
[void]$sb.AppendLine("")

# ─── New Failures (Branch only) ──────────────────────────────────────────────
if ($branchOnlyFailures.Count -gt 0) {
    [void]$sb.AppendLine("## 🔴 New Failures (only in branch)")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("These tests **pass** on the baseline but **fail** on the branch — likely regressions.")
    [void]$sb.AppendLine("")

    $codeFence = '```'
    foreach ($item in $branchOnlyFailures) {
        $t = $item.Branch
        [void]$sb.AppendLine("### ``$($t.TestName)``")
        [void]$sb.AppendLine("")
        if ($t.Error) {
            [void]$sb.AppendLine("**Error:**")
            [void]$sb.AppendLine($codeFence)
            [void]$sb.AppendLine($t.Error.Substring(0, [Math]::Min(500, $t.Error.Length)))
            [void]$sb.AppendLine($codeFence)
        }
        if ($t.StackTrace) {
            [void]$sb.AppendLine("<details><summary>Stack trace</summary>")
            [void]$sb.AppendLine("")
            [void]$sb.AppendLine($codeFence)
            [void]$sb.AppendLine($t.StackTrace.Substring(0, [Math]::Min(1000, $t.StackTrace.Length)))
            [void]$sb.AppendLine($codeFence)
            [void]$sb.AppendLine("</details>")
        }
        [void]$sb.AppendLine("")
    }
}

# ─── Fixed Failures (Baseline only) ─────────────────────────────────────────
if ($baselineOnlyFailures.Count -gt 0) {
    [void]$sb.AppendLine("## 🟢 Fixed Failures (only in baseline)")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("These tests **fail** on the baseline but **pass** on the branch — issues fixed by your changes.")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Test Name | Baseline Error |")
    [void]$sb.AppendLine("|-----------|---------------|")

    foreach ($item in $baselineOnlyFailures) {
        $t = $item.Baseline
        $shortError = if ($t.Error) { $t.Error.Substring(0, [Math]::Min(80, $t.Error.Length)) -replace '\|', '¦' -replace "`n", ' ' } else { '—' }
        [void]$sb.AppendLine("| ``$($t.TestName)`` | $shortError |")
    }
    [void]$sb.AppendLine("")
}

# ─── Latency Regressions ────────────────────────────────────────────────────
if ($latencyRegressions.Count -gt 0) {
    [void]$sb.AppendLine("## 🐢 Latency Regressions (>$($LatencyThresholdPercent)% slower)")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Test Name | Baseline (ms) | Branch (ms) | Change |")
    [void]$sb.AppendLine("|-----------|:-------------:|:-----------:|:------:|")

    $topRegressions = $latencyRegressions | Select-Object -First 25
    foreach ($item in $topRegressions) {
        $changeStr = "+$([Math]::Round($item.DiffPercent, 1))%"
        [void]$sb.AppendLine("| ``$($item.TestName)`` | $([Math]::Round($item.BaselineMs, 1)) | $([Math]::Round($item.BranchMs, 1)) | $changeStr |")
    }

    if ($latencyRegressions.Count -gt 25) {
        [void]$sb.AppendLine("")
        [void]$sb.AppendLine("*...and $($latencyRegressions.Count - 25) more.*")
    }
    [void]$sb.AppendLine("")
}

# ─── Latency Improvements ───────────────────────────────────────────────────
if ($latencyImprovements.Count -gt 0) {
    [void]$sb.AppendLine("## 🚀 Latency Improvements (>$($LatencyThresholdPercent)% faster)")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Test Name | Baseline (ms) | Branch (ms) | Change |")
    [void]$sb.AppendLine("|-----------|:-------------:|:-----------:|:------:|")

    $topImprovements = $latencyImprovements | Select-Object -First 25
    foreach ($item in $topImprovements) {
        $changeStr = "$([Math]::Round($item.DiffPercent, 1))%"
        [void]$sb.AppendLine("| ``$($item.TestName)`` | $([Math]::Round($item.BaselineMs, 1)) | $([Math]::Round($item.BranchMs, 1)) | $changeStr |")
    }

    if ($latencyImprovements.Count -gt 25) {
        [void]$sb.AppendLine("")
        [void]$sb.AppendLine("*...and $($latencyImprovements.Count - 25) more.*")
    }
    [void]$sb.AppendLine("")
}

# ─── Tests that fail in both ─────────────────────────────────────────────────
if ($bothFailed.Count -gt 0) {
    [void]$sb.AppendLine("## ⚪ Failures in Both (pre-existing)")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("These $($bothFailed.Count) test(s) fail in both baseline and branch — not caused by your changes.")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("<details><summary>Show all $($bothFailed.Count) shared failures</summary>")
    [void]$sb.AppendLine("")

    foreach ($item in $bothFailed) {
        [void]$sb.AppendLine("- ``$($item.Branch.TestName)``")
    }

    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("</details>")
    [void]$sb.AppendLine("")
}

# ─── New / Removed tests ─────────────────────────────────────────────────────
if ($newTests.Count -gt 0) {
    [void]$sb.AppendLine("## ➕ New Tests (only in branch)")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("$($newTests.Count) test(s) exist in the branch but not in the baseline.")
    [void]$sb.AppendLine("")
    $newFailed = $newTests | Where-Object { $_.Outcome -ne 'Passed' }
    $newPassed = $newTests | Where-Object { $_.Outcome -eq 'Passed' }
    if ($newFailed.Count -gt 0) {
        [void]$sb.AppendLine("**Failing new tests:**")
        foreach ($t in $newFailed) { [void]$sb.AppendLine("- ❌ ``$($t.TestName)``") }
        [void]$sb.AppendLine("")
    }
    if ($newPassed.Count -gt 0 -and $newPassed.Count -le 20) {
        [void]$sb.AppendLine("**Passing new tests:**")
        foreach ($t in $newPassed) { [void]$sb.AppendLine("- ✅ ``$($t.TestName)``") }
        [void]$sb.AppendLine("")
    } elseif ($newPassed.Count -gt 20) {
        [void]$sb.AppendLine("**$($newPassed.Count) new tests all passing.** ✅")
        [void]$sb.AppendLine("")
    }
}

if ($removedTests.Count -gt 0) {
    [void]$sb.AppendLine("## ➖ Removed Tests (only in baseline)")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("$($removedTests.Count) test(s) exist in the baseline but not in the branch.")
    [void]$sb.AppendLine("")
}

# ─── Footer ──────────────────────────────────────────────────────────────────
[void]$sb.AppendLine("---")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("*Generated by FHIR Server A/B Test Runner on $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss UTC' -AsUTC)*")
$footerParts = @("Latency threshold: ±$($LatencyThresholdPercent)%")
if ($iterationCount -gt 1) { $footerParts += "Iterations: $iterationCount (durations averaged)" }
[void]$sb.AppendLine("*$($footerParts -join ' | ')*")

$report = $sb.ToString()
$report | Set-Content -Path $OutputPath -Encoding utf8

Write-Host "`nComparison report written to: $OutputPath" -ForegroundColor Green

# ─── Console summary ─────────────────────────────────────────────────────────
Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host " SUMMARY" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

if ($branchOnlyFailures.Count -gt 0) {
    Write-Host " 🔴 NEW FAILURES:         $($branchOnlyFailures.Count)" -ForegroundColor Red
}
if ($baselineOnlyFailures.Count -gt 0) {
    Write-Host " 🟢 FIXED FAILURES:       $($baselineOnlyFailures.Count)" -ForegroundColor Green
}
if ($bothFailed.Count -gt 0) {
    Write-Host " ⚪ PRE-EXISTING FAILURES: $($bothFailed.Count)" -ForegroundColor Gray
}
if ($latencyRegressions.Count -gt 0) {
    Write-Host " 🐢 LATENCY REGRESSIONS:  $($latencyRegressions.Count)" -ForegroundColor Yellow
}
if ($latencyImprovements.Count -gt 0) {
    Write-Host " 🚀 LATENCY IMPROVEMENTS: $($latencyImprovements.Count)" -ForegroundColor Green
}

Write-Host ""
Write-Host " Baseline: $baselinePassed passed / $baselineFailed failed (total: $($baselineResults.Count))"
Write-Host " Branch:   $branchPassed passed / $branchFailed failed (total: $($branchResults.Count))"
Write-Host ""
