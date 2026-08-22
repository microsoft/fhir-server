<#
.SYNOPSIS
    Fails a test leg where a failing test was retried and never actually passed.

.DESCRIPTION
    The test legs let the runner's exit code be the verdict, because the results of every retry
    attempt are published and failing on those would cancel out every retry. That reasoning holds
    only while "the runner exited zero" means "every failure was cleared by a later attempt", and it
    does not: a test that fails and then skips is not counted as a failure by the retry extension, so
    a run where a real failure turned into a skip exits zero and the leg goes green.

    This reads what the attempts actually reported. Every test that failed in any attempt has to be
    recorded as passed in the final attempt. A test that failed and then skipped, or that failed and
    then was not run at all, fails the leg here instead of disappearing.

    Results are keyed by the assembly they came from as well as the test name, because this
    repository compiles the same shared test files into several assemblies and the names alone
    collide.

.PARAMETER ResultsDirectory
    The directory the runner was given for its results. Retry attempts are below it under Retries.
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ResultsDirectory)) {
    throw "The results directory '$ResultsDirectory' does not exist, so no test results can be read from it."
}

$allReports = @(Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -Filter *.trx -File)
$attemptReports = @($allReports | Where-Object { $_.FullName -match '[\\/]Retries[\\/]' })

if ($attemptReports.Count -eq 0) {
    Write-Host "No retry attempts were recorded under '$ResultsDirectory', so there is nothing to reconcile."
    exit 0
}

$finalReports = @($allReports | Where-Object { $_.FullName -notmatch '[\\/]Retries[\\/]' })

if ($finalReports.Count -eq 0) {
    throw "Retry attempts were recorded under '$ResultsDirectory' but no final report was found beside them, so what the retried tests ended up doing cannot be read."
}

function Read-Outcomes {
    param([System.IO.FileInfo] $Report)

    [xml] $document = Get-Content -LiteralPath $Report.FullName -Raw

    $storageById = @{}
    foreach ($definition in $document.TestRun.TestDefinitions.UnitTest) {
        if ($null -ne $definition) {
            $storageById[$definition.id] = ($definition.storage ?? '').ToLowerInvariant()
        }
    }

    foreach ($result in $document.TestRun.Results.UnitTestResult) {
        if ($null -eq $result) {
            continue
        }

        $storage = ''
        if ($storageById.ContainsKey($result.testId)) {
            $storage = $storageById[$result.testId]
        }

        [PSCustomObject]@{
            Key     = "$storage|$($result.testName)"
            Name    = $result.testName
            Outcome = $result.outcome
        }
    }
}

$failedWhenRetried = @{}
foreach ($report in $attemptReports) {
    foreach ($outcome in Read-Outcomes -Report $report) {
        if ($outcome.Outcome -eq 'Failed') {
            $failedWhenRetried[$outcome.Key] = $outcome.Name
        }
    }
}

if ($failedWhenRetried.Count -eq 0) {
    Write-Host "Retry attempts were recorded but none of them reported a failure, so there is nothing to reconcile."
    exit 0
}

$finalOutcomes = @{}
foreach ($report in $finalReports) {
    foreach ($outcome in Read-Outcomes -Report $report) {
        # A test can be recorded more than once in a final report. Passed is what clears an earlier
        # failure, so it wins over any other outcome recorded for the same test.
        if ($outcome.Outcome -eq 'Passed' -or -not $finalOutcomes.ContainsKey($outcome.Key)) {
            $finalOutcomes[$outcome.Key] = $outcome.Outcome
        }
    }
}

$unresolved = @()
foreach ($key in $failedWhenRetried.Keys) {
    $final = if ($finalOutcomes.ContainsKey($key)) { $finalOutcomes[$key] } else { 'not run at all' }

    if ($final -ne 'Passed') {
        $unresolved += "  $($failedWhenRetried[$key]) -> $final"
    }
}

if ($unresolved.Count -gt 0) {
    $detail = $unresolved -join [Environment]::NewLine
    throw @"
These tests failed and were retried, but the final attempt does not record them as passing:
$detail

The runner treats anything other than a failure as retried successfully, so a test that fails and
then skips, or that fails and then does not run, exits the runner zero and would otherwise leave
this leg green with a real failure inside it.
"@
}

Write-Host "Every test that failed in a retry attempt passed in the final attempt ($($failedWhenRetried.Count) reconciled)."
