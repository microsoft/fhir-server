<#
.SYNOPSIS
Runs dotnet tests with automatic retry of failed tests only.

.DESCRIPTION
This script runs dotnet tests and automatically retries only the tests that failed,
preserving test results and publishing them correctly to Azure DevOps.
It uses TRX result files to identify failed tests and re-runs them with --filter.

WHERE IT'S USED:
- E2E Tests: All E2E test jobs (Cosmos, SQL, various test categories)
- Integration Tests: Both Cosmos and SQL integration tests with code coverage
- Export Tests: Long-running export tests
(NOT used for infrastructure tasks like deployment, provisioning, AAD setup)

BENEFITS OVER retryCountOnTaskFailure:
- Saves time: Only failed tests are retried, not the entire suite
- Better visibility: Clear indication of initial failures and retry attempts
- Faster execution: Significant time savings when only a few tests fail
- Multiple TRX files: All attempts are recorded and published to Azure DevOps
- No timeout issues: Retrying subset of tests is much faster than full suite

HOW IT WORKS:
1. Runs initial test suite and captures TRX results
2. If failures detected, parses TRX to extract failed test names
3. Builds dotnet test --filter targeting only failed tests
4. Retries with the filtered set (up to MaxRetries times)
5. Generates new TRX for each retry attempt
6. Sets TestResultsDirectory variable for PublishTestResults@2 task

CODE COVERAGE SUPPORT:
Pass code coverage arguments via AdditionalArgs parameter:
  -AdditionalArgs "--collect 'XPlat Code Coverage' -s 'CodeCoverage.runsettings'"
Coverage will be collected on both initial run and retry attempts.

.PARAMETER TestAssemblies
The test assemblies or pattern to test (e.g., "**/*Tests.E2E*.dll").

.PARAMETER Filter
Optional filter expression for initial test run (e.g., "FullyQualifiedName~CosmosDb").

.PARAMETER MaxRetries
Maximum number of retry attempts for failed tests. Default is 1.

.PARAMETER WorkingDirectory
Working directory for test execution. Default is current directory.

.PARAMETER AdditionalArgs
Additional arguments to pass to dotnet test (e.g., "--collect 'XPlat Code Coverage'").

.PARAMETER TestRunTitle
Title for the test run in Azure DevOps.

.EXAMPLE
# E2E Tests
.\Invoke-TestWithRetry.ps1 `
  -TestAssemblies "**/*.Tests.E2E*.dll" `
  -Filter "Category=E2E" `
  -MaxRetries 1 `
  -AdditionalArgs "--blame-hang-timeout 15m" `
  -TestRunTitle "E2E Tests"

.EXAMPLE
# Integration Tests with Code Coverage
.\Invoke-TestWithRetry.ps1 `
  -TestAssemblies "**/*.Tests.Integration*.dll" `
  -Filter "DisplayName!~SqlServer" `
  -MaxRetries 1 `
  -AdditionalArgs "--collect 'XPlat Code Coverage' -s 'CodeCoverage.runsettings' -v normal" `
  -TestRunTitle "Cosmos Integration Tests"

.NOTES
This script outputs TRX files that can be consumed by Azure DevOps test result publishing.
Use with PublishTestResults@2 task with mergeTestResults: true to combine all attempts.
Environment variables are inherited via PowerShell & operator for proper test execution.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$TestAssemblies,

    [Parameter(Mandatory = $false)]
    [string]$Filter = "",

    [Parameter(Mandatory = $false)]
    [int]$MaxRetries = 1,

    [Parameter(Mandatory = $false)]
    [string]$WorkingDirectory = ".",

    [Parameter(Mandatory = $false)]
    [string]$AdditionalArgs = "",

    [Parameter(Mandatory = $false)]
    [string]$TestRunTitle = "Test Run"
)

$ErrorActionPreference = "Stop"

# Create a unique results directory for this test run
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$resultsDir = Join-Path $WorkingDirectory "TestResults_$timestamp"
New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null

Write-Host "##[section]Starting test run: $TestRunTitle"
Write-Host "Test assemblies: $TestAssemblies"
Write-Host "Initial filter: $Filter"
Write-Host "Max retries: $MaxRetries"
Write-Host "Results directory: $resultsDir"

# Function to run tests and return exit code
function Invoke-DotNetTest {
    param(
        [string]$Assemblies,
        [string]$TestFilter,
        [string]$RunName,
        [int]$AttemptNumber
    )
    
    $trxFileName = "TestResults_${RunName}_Attempt${AttemptNumber}.trx"
    $trxPath = Join-Path $resultsDir $trxFileName
    
    $testArgs = @(
        "test"
        $Assemblies
        "--logger"
        "trx;LogFileName=$trxPath"
    )
    
    if ($TestFilter) {
        $testArgs += @("--filter", $TestFilter)
    }
    
    if ($AdditionalArgs) {
        # Split additional args by space, preserving quoted strings
        $testArgs += $AdditionalArgs.Split(' ')
    }
    
    Write-Host "##[command]dotnet $($testArgs -join ' ')"
    
    # Save current location
    $currentLocation = Get-Location
    
    try {
        # Change to working directory
        Set-Location $WorkingDirectory
        
        # Run the test using & operator which inherits environment variables
        & dotnet $testArgs
        $exitCode = $LASTEXITCODE
    }
    finally {
        # Restore location
        Set-Location $currentLocation
    }
    
    return @{
        ExitCode = $exitCode
        TrxPath = $trxPath
        AttemptNumber = $AttemptNumber
    }
}

# Function to parse TRX file and extract failed test names
function Get-FailedTestsFromTrx {
    param(
        [string]$TrxPath
    )
    
    if (-not (Test-Path $TrxPath)) {
        Write-Warning "TRX file not found: $TrxPath"
        return @()
    }
    
    try {
        [xml]$trxContent = Get-Content $TrxPath
        $ns = New-Object System.Xml.XmlNamespaceManager($trxContent.NameTable)
        $ns.AddNamespace("ns", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
        
        # Find all failed test results
        $failedResults = $trxContent.SelectNodes("//ns:UnitTestResult[@outcome='Failed']", $ns)
        
        $failedTests = @()
        foreach ($result in $failedResults) {
            $testName = $result.testName
            if ($testName) {
                $failedTests += $testName
            }
        }
        
        return $failedTests
    }
    catch {
        Write-Warning "Error parsing TRX file: $_"
        return @()
    }
}

# Initial test run
$attempt = 0
$initialResult = Invoke-DotNetTest -Assemblies $TestAssemblies -TestFilter $Filter -RunName "Initial" -AttemptNumber $attempt

$allTrxFiles = @($initialResult.TrxPath)
$finalExitCode = $initialResult.ExitCode

Write-Host "##[section]Initial test run completed with exit code: $finalExitCode"

# If initial run failed and retries are enabled, retry failed tests
if ($finalExitCode -ne 0 -and $MaxRetries -gt 0) {
    $failedTests = Get-FailedTestsFromTrx -TrxPath $initialResult.TrxPath
    
    if ($failedTests.Count -eq 0) {
        Write-Host "##[warning]Tests failed but no failed tests found in TRX. This may indicate a crash or infrastructure issue."
    }
    else {
        Write-Host "##[warning]Found $($failedTests.Count) failed test(s). Will retry up to $MaxRetries time(s)."
        
        for ($retryAttempt = 1; $retryAttempt -le $MaxRetries; $retryAttempt++) {
            Write-Host "##[section]Retry attempt $retryAttempt of $MaxRetries"
            
            # Build filter for failed tests
            # Create an OR filter for all failed test names
            # Note: Test names are matched as-is. If test names contain special characters
            # like parentheses, they should still work with the ~ (contains) operator
            $retryFilter = ($failedTests | ForEach-Object { 
                # Escape any special characters that might cause issues in the filter
                $escapedName = $_ -replace '\|', '\|'
                "FullyQualifiedName~$escapedName" 
            }) -join "|"
            
            # Combine with original filter if it exists
            if ($Filter) {
                $retryFilter = "($Filter)&($retryFilter)"
            }
            
            Write-Host "Retrying $($failedTests.Count) failed test(s)"
            
            $retryResult = Invoke-DotNetTest -Assemblies $TestAssemblies -TestFilter $retryFilter -RunName "Retry" -AttemptNumber $retryAttempt
            $allTrxFiles += $retryResult.TrxPath
            
            # Check if retry succeeded
            if ($retryResult.ExitCode -eq 0) {
                Write-Host "##[section]All previously failed tests passed on retry attempt $retryAttempt"
                $finalExitCode = 0
                break
            }
            else {
                # Get the still-failing tests for next retry
                $stillFailedTests = Get-FailedTestsFromTrx -TrxPath $retryResult.TrxPath
                
                if ($stillFailedTests.Count -eq 0) {
                    Write-Host "##[warning]Retry failed but no failed tests found in TRX. Stopping retries."
                    break
                }
                
                Write-Host "##[warning]$($stillFailedTests.Count) test(s) still failing after retry attempt $retryAttempt"
                $failedTests = $stillFailedTests
                $finalExitCode = $retryResult.ExitCode
            }
        }
    }
}

# Output summary
Write-Host "##[section]Test Execution Summary"
Write-Host "Total TRX files generated: $($allTrxFiles.Count)"
foreach ($trx in $allTrxFiles) {
    Write-Host "  - $trx"
}

# Set output variable for Azure DevOps
Write-Host "##vso[task.setvariable variable=TestResultsDirectory]$resultsDir"

# Exit with the final result
if ($finalExitCode -eq 0) {
    Write-Host "##[section]Test run completed successfully"
}
else {
    Write-Host "##[error]Test run failed with exit code: $finalExitCode"
}

exit $finalExitCode
