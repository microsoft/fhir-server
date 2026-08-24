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

    Results are keyed by the identifier the report gives each test, together with the assembly it
    came from. Display names are not identities: a theory whose rows differ only in the case of an
    argument produces two tests with names PowerShell's hashtables cannot tell apart, so keying by
    name lets one test's pass clear a different test's failure. The assembly stays in the key
    because this repository compiles the same shared test files into several assemblies, which can
    give the same test the same identifier in each of them.

.PARAMETER ResultsDirectory
    The directory the runner was given for its results. Retry attempts are below it under Retries.
#>
#requires -Version 7.0

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

    # An empty <TestDefinitions/> or <Results/> is what a report that recorded nothing looks like,
    # and the XML adapter surfaces an empty element as a string, so reaching through it for child
    # elements fails. That would fail the leg with a PowerShell error in place of the verdict below,
    # which is the one message that explains a report holding no results at all. Selecting from the
    # document instead is unaffected by how any one element happens to be surfaced, and matching on
    # local names keeps it working whether or not the TRX carries its namespace.
    $definitions = @($document.SelectNodes('//*[local-name()="UnitTest"]'))

    $storageById = [System.Collections.Hashtable]::new([System.StringComparer]::Ordinal)
    foreach ($definition in $definitions) {
        if ($null -ne $definition) {
            # Attributes are read with GetAttribute rather than as properties: strict mode turns a
            # missing property into an error about PowerShell, which would replace the explanations
            # below with noise for the one kind of report that most needs explaining.
            $storageById[$definition.GetAttribute('id').ToLowerInvariant()] = $definition.GetAttribute('storage').ToLowerInvariant()
        }
    }

    $results = @($document.SelectNodes('//*[local-name()="UnitTestResult"]'))

    foreach ($result in $results) {
        if ($null -eq $result) {
            continue
        }

        $testId = $result.GetAttribute('testId')
        $testName = $result.GetAttribute('testName')

        # Without an identifier there is nothing to reconcile this result against, and guessing from
        # the name is what this key exists to avoid. Refusing here fails the leg, which is the safe
        # direction: the alternative is dropping a result that may be the failure being looked for.
        if ([string]::IsNullOrWhiteSpace($testId)) {
            throw "'$($Report.FullName)' records a result for '$testName' with no test id, so it cannot be matched to the same test in another attempt."
        }

        $testId = $testId.ToLowerInvariant()

        $storage = ''
        if ($storageById.ContainsKey($testId)) {
            $storage = $storageById[$testId]
        }

        [PSCustomObject]@{
            Key     = "$storage|$testId"
            Name    = $testName
            Outcome = $result.GetAttribute('outcome')
        }
    }
}

$failedWhenRetried = [System.Collections.Hashtable]::new([System.StringComparer]::Ordinal)
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

$finalOutcomes = [System.Collections.Hashtable]::new([System.StringComparer]::Ordinal)
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
