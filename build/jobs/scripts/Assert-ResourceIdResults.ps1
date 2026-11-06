param(
    [Parameter(Mandatory = $true)] [string] $ResultsDirectory,
    [Parameter(Mandatory = $true)] [ValidateSet('Standard64', 'Long128')] [string] $Profile,
    [Parameter(Mandatory = $true)] [DateTimeOffset] $StartedUtc
)

$ErrorActionPreference = 'Stop'
$expected = if ($Profile -eq 'Standard64') {
    @('Standard64_64_Accept', 'Standard64_65_Reject', 'Standard64_128_Reject')
} else {
    @('Long128_64_Accept', 'Long128_65_Accept', 'Long128_128_Accept', 'Long128_129_Reject')
}
$method = 'GivenResourceIdBoundary_WhenUsingRemoteSql_ThenExpectedStatusCodesAreReturned'
$class = 'Microsoft.Health.Fhir.Tests.E2E.Rest.RemoteResourceIdLengthTests'
$results = @()
$files = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -Recurse)
if ($files.Count -eq 0) { throw 'No TRX results were produced for the resource ID contract.' }

foreach ($file in $files) {
    [xml] $trx = Get-Content -LiteralPath $file.FullName -Raw
    $definitions = @{}
    foreach ($definition in $trx.SelectNodes("//*[local-name()='UnitTest']")) {
        $testMethod = $definition.SelectSingleNode("*[local-name()='TestMethod']")
        if ($testMethod.className -eq $class -and $testMethod.name -eq $method) {
            $definitions[[string]$definition.id] = $true
        }
    }

    foreach ($result in $trx.SelectNodes("//*[local-name()='UnitTestResult']")) {
        if (-not $definitions.ContainsKey([string]$result.testId)) { continue }
        $pattern = '^' + [regex]::Escape("$class.$method") + '\(caseId: "(?<case>[^"]+)"\)$'
        if ($result.testName -cnotmatch $pattern) { throw "Unrecognized boundary result identity '$($result.testName)'." }
        $case = $Matches['case']
        if ($expected -cnotcontains $case) { throw "Unexpected boundary case '$case' in the $Profile process." }
        $ended = [DateTimeOffset]::Parse($result.endTime, [Globalization.CultureInfo]::InvariantCulture)
        if ($ended -lt $StartedUtc) { throw "Stale boundary result '$case'." }
        $results += [pscustomobject]@{ Case = $case; Ended = $ended; Outcome = [string]$result.outcome }
    }
}

foreach ($case in $expected) {
    $attempts = @($results | Where-Object Case -CEQ $case | Sort-Object Ended)
    if ($attempts.Count -eq 0) { throw "Missing boundary case '$case'." }
    $latest = $attempts[-1]
    $finalAttempts = @($attempts | Where-Object Ended -EQ $latest.Ended)
    if (@($finalAttempts | Where-Object Outcome -CNE 'Passed').Count -ne 0) {
        throw "Final boundary outcome for '$case' is not Passed."
    }
    Write-Host "Verified $case Passed ($($attempts.Count) attempt(s))."
}
