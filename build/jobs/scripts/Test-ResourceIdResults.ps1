$ErrorActionPreference = 'Stop'
$checker = Join-Path $PSScriptRoot 'Assert-ResourceIdResults.ps1'
if (-not (Test-Path $checker)) { throw 'The resource ID result checker is not implemented.' }

$root = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
[void](New-Item $root -ItemType Directory)
$started = [DateTimeOffset]::UtcNow.AddMinutes(-1)
$method = 'GivenResourceIdBoundary_WhenUsingRemoteSql_ThenExpectedStatusCodesAreReturned'
$class = 'Microsoft.Health.Fhir.Tests.E2E.Rest.RemoteResourceIdLengthTests'
$standard = @('Standard64_64_Accept', 'Standard64_65_Reject', 'Standard64_128_Reject')
$long = @('Long128_64_Accept', 'Long128_65_Accept', 'Long128_128_Accept', 'Long128_129_Reject')

function Write-Fixture($Name, $Cases, $Outcomes, $Times, $ClassName = $class) {
    $directory = Join-Path $root $Name
    [void](New-Item $directory -ItemType Directory -Force)
    $definitions = @()
    $results = @()
    for ($i = 0; $i -lt $Cases.Count; $i++) {
        $testName = [Security.SecurityElement]::Escape("$class.$method(caseId: `"$($Cases[$i])`")")
        $definitions += "<UnitTest id='test$i' name='$testName'><TestMethod className='$ClassName' name='$method' /></UnitTest>"
        $time = if ($Times) { $Times[$i] } else { [DateTimeOffset]::UtcNow.ToString('o') }
        $results += "<UnitTestResult testId='test$i' testName='$testName' outcome='$($Outcomes[$i])' endTime='$time' />"
    }
    $xml = "<TestRun xmlns='http://microsoft.com/schemas/VisualStudio/TeamTest/2010'><TestDefinitions>$($definitions -join '')</TestDefinitions><Results>$($results -join '')</Results></TestRun>"
    [IO.File]::WriteAllText((Join-Path $directory 'results.trx'), $xml)
    return $directory
}

function Assert-Fixture($Name, $Cases, $Outcomes, $ShouldPass, $Profile = 'Long128', $Times = $null, $ClassName = $class) {
    $directory = Write-Fixture $Name $Cases $Outcomes $Times $ClassName
    $failure = $null
    try { & $checker -ResultsDirectory $directory -Profile $Profile -StartedUtc $started }
    catch { $failure = $_ }
    if ($ShouldPass -eq ($null -ne $failure)) {
        throw "Fixture '$Name' expected pass=$ShouldPass. Actual error: $failure"
    }
    Write-Host "PASS $Name$(if ($failure) { " rejected: $($failure.Exception.Message)" })"
}

try {
    Assert-Fixture 'standard' $standard @('Passed', 'Passed', 'Passed') $true 'Standard64'
    Assert-Fixture 'long' $long @('Passed', 'Passed', 'Passed', 'Passed') $true
    Assert-Fixture 'empty' @() @() $false
    Assert-Fixture 'missing' $long[0..2] @('Passed', 'Passed', 'Passed') $false
    Assert-Fixture 'skipped' $long @('Passed', 'Passed', 'Passed', 'NotExecuted') $false
    Assert-Fixture 'failed' $long @('Passed', 'Passed', 'Passed', 'Failed') $false
    Assert-Fixture 'duplicates-do-not-fill-missing' @($long[0], $long[0], $long[1], $long[2]) @('Passed', 'Passed', 'Passed', 'Passed') $false
    Assert-Fixture 'wrong-profile' $standard @('Passed', 'Passed', 'Passed') $false
    Assert-Fixture 'unrelated-class' $long @('Passed', 'Passed', 'Passed', 'Passed') $false 'Long128' $null 'Other.Tests'
    $stale = [DateTimeOffset]::UtcNow.AddDays(-1).ToString('o')
    Assert-Fixture 'stale' $long @('Passed', 'Passed', 'Passed', 'Passed') $false 'Long128' @($stale, $stale, $stale, $stale)
    $early = [DateTimeOffset]::UtcNow.AddSeconds(-10).ToString('o')
    $late = [DateTimeOffset]::UtcNow.ToString('o')
    Assert-Fixture 'retry-passes' @($long + $long[3]) @('Passed', 'Passed', 'Passed', 'Failed', 'Passed') $true 'Long128' @($early, $early, $early, $early, $late)
    Assert-Fixture 'retry-final-fails' @($long + $long[3]) @('Passed', 'Passed', 'Passed', 'Passed', 'Failed') $false 'Long128' @($early, $early, $early, $early, $late)
    Assert-Fixture 'retry-final-skips' @($long + $long[3]) @('Passed', 'Passed', 'Passed', 'Passed', 'NotExecuted') $false 'Long128' @($early, $early, $early, $early, $late)
    Assert-Fixture 'unexpected-case' @($long + 'Long128_130_Reject') @('Passed', 'Passed', 'Passed', 'Passed', 'Passed') $false
}
finally {
    Remove-Item $root -Recurse -Force
}
