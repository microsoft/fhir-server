<#
.SYNOPSIS
Runs dotnet tests with automatic retry of failed tests only.

.DESCRIPTION
This script runs dotnet tests and automatically retries only the tests that failed,
preserving test results and publishing them correctly to Azure DevOps.
It uses TRX result files to identify failed tests and re-runs them with --filter.

WHERE IT'S USED:
- Unit Tests: All unit test jobs (build.yml) with optional code coverage
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
7. Azure DevOps detects retry attempts and shows tests as "Passed with Retry"

RETRY-AWARE TEST PUBLISHING:
Azure DevOps automatically detects retry attempts when multiple TRX files are published
from the same test run. Tests that fail initially but pass on retry are shown as 
"Passed with Retry" in the test blade, providing full visibility into flaky tests.

This requires the AllowPtrToDetectTestRunRetryFiles variable to be set to true at the
pipeline level (configured in build-variables.yml). 

Reference: https://devblogs.microsoft.com/dotnet/microsoft-testing-platform-azure-retry/

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

When AllowPtrToDetectTestRunRetryFiles is enabled (set in build-variables.yml), Azure DevOps
will automatically show tests as "Passed with Retry" instead of "Failed" when they pass on
retry attempts. This provides better visibility into test reliability without hiding failures.
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
    
    # Create a subdirectory for this attempt to collect all TRX files
    $attemptDir = Join-Path $resultsDir "${RunName}_Attempt${AttemptNumber}"
    New-Item -ItemType Directory -Path $attemptDir -Force | Out-Null
    
    # Resolve assembly paths
    $expandedAssemblies = @()
    $pushLocation = Get-Location
    
    try {
        Set-Location $WorkingDirectory

        if ($Assemblies -match '[\*\?]') {
            # 1. Try standard glob resolution first
            $expandedAssemblies = Get-ChildItem -Path $Assemblies -Recurse -File -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName
            
            # 2. Fallback: Recursive search for filename if standard glob failed
            if (-not $expandedAssemblies) {
                $fileName = Split-Path $Assemblies -Leaf
                # Strip directory separators just in case Split-Path didn't catch everything on regex glob
                $fileName = $fileName -replace '^.*[\\/]', ''
                
                Write-Host "##[warning]Standard glob failed. Recursively searching for matching files named '$fileName'"
                $expandedAssemblies = Get-ChildItem -Path . -Filter $fileName -Recurse -File -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName
            }
            
            if ($expandedAssemblies) {
                Write-Host "Resolved '$Assemblies' to $($expandedAssemblies.Count) file(s)"
            }
        } else {
             # No wildcards, treat as literal path (relative to WD if not rooted)
             $fullPath = if ([System.IO.Path]::IsPathRooted($Assemblies)) { $Assemblies } else { Join-Path $WorkingDirectory $Assemblies }
             $expandedAssemblies = @($fullPath)
        }
    }
    finally {
        Set-Location $pushLocation
    }
    
    # Parse additional args once for reuse
    $parsedAdditionalArgs = @()
    if ($AdditionalArgs) {
        # Parse additional args respecting quoted strings (single and double quotes)
        $regex = @'
(?x)                    # Allow whitespace and comments in regex
[^\s"']+                # Match unquoted arguments (no spaces, no quotes)
|                       # OR
"[^"]*"                 # Match double-quoted strings (including quotes)
|                       # OR
'[^']*'                 # Match single-quoted strings (including quotes)
'@
        $matches = [regex]::Matches($AdditionalArgs, $regex)
        
        foreach ($match in $matches) {
            $arg = $match.Value
            # Remove surrounding quotes if present (both single and double)
            if (($arg.StartsWith("'") -and $arg.EndsWith("'")) -or 
                ($arg.StartsWith('"') -and $arg.EndsWith('"'))) {
                $arg = $arg.Substring(1, $arg.Length - 2)
            }
            $parsedAdditionalArgs += $arg
        }
    }
    
    $overallExitCode = 0
    $currentLocation = Get-Location
    
    try {
        Set-Location $WorkingDirectory
        
        # Run dotnet test for each assembly separately
        foreach ($assembly in $expandedAssemblies) {
            $testArgs = @(
                "test"
                $assembly
                "--results-directory"
                $attemptDir
                "--logger"
                "trx"
            )
            
            if ($TestFilter) {
                $testArgs += @("--filter", $TestFilter)
            }
            
            $testArgs += $parsedAdditionalArgs
            
            Write-Host "##[command]dotnet $($testArgs -join ' ')"
            
            # Run the test using & operator which inherits environment variables
            & dotnet $testArgs
            $exitCode = $LASTEXITCODE
            
            # Track if any project failed
            if ($exitCode -ne 0) {
                $overallExitCode = $exitCode
            }
        }
    }
    finally {
        Set-Location $currentLocation
    }
    
    return @{
        ExitCode = $overallExitCode
        TrxDirectory = $attemptDir
        AttemptNumber = $AttemptNumber
    }
}

# Function to get all TRX files from a directory
function Get-TrxFilesFromDirectory {
    param(
        [string]$Directory
    )
    
    if (-not (Test-Path $Directory)) {
        return @()
    }
    
    return Get-ChildItem -Path $Directory -Filter "*.trx" -File | Select-Object -ExpandProperty FullName
}

# Function to get coverage files from a directory
function Get-CoverageFilesFromDirectory {
    param(
        [string]$Directory
    )

    if (-not (Test-Path $Directory)) {
        return @()
    }

    return Get-ChildItem -Path $Directory -Recurse -Filter "coverage.cobertura.xml" -File | Select-Object -ExpandProperty FullName
}

# Function to parse TRX files and extract failed test names
function Get-FailedTestsFromTrx {
    param(
        [string]$TrxDirectory
    )
    
    $trxFiles = Get-TrxFilesFromDirectory -Directory $TrxDirectory
    if ($trxFiles.Count -eq 0) {
        Write-Warning "No TRX files found in: $TrxDirectory"
        return @()
    }
    
    $allFailedTests = @()
    foreach ($trxPath in $trxFiles) {
        try {
            [xml]$trxContent = Get-Content $trxPath
            $ns = New-Object System.Xml.XmlNamespaceManager($trxContent.NameTable)
            $ns.AddNamespace("ns", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
            
            # Find all failed test results
            $failedResults = $trxContent.SelectNodes("//ns:UnitTestResult[@outcome='Failed']", $ns)
            
            foreach ($result in $failedResults) {
                $testName = $result.testName
                if ($testName -and $allFailedTests -notcontains $testName) {
                    $allFailedTests += $testName
                }
            }
        }
        catch {
            Write-Warning "Error parsing TRX file $trxPath : $_"
        }
    }
    
    return $allFailedTests
}

# Function to count total executed tests from TRX files in a directory
function Get-ExecutedTestCountFromTrx {
    param(
        [string]$TrxDirectory
    )
    
    $trxFiles = Get-TrxFilesFromDirectory -Directory $TrxDirectory
    if ($trxFiles.Count -eq 0) {
        Write-Warning "No TRX files found in: $TrxDirectory"
        return 0
    }
    
    $totalCount = 0
    foreach ($trxPath in $trxFiles) {
        try {
            [xml]$trxContent = Get-Content $trxPath
            $ns = New-Object System.Xml.XmlNamespaceManager($trxContent.NameTable)
            $ns.AddNamespace("ns", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
            
            # Count all test results (executed tests)
            $allResults = $trxContent.SelectNodes("//ns:UnitTestResult", $ns)
            $totalCount += $allResults.Count
        }
        catch {
            Write-Warning "Error parsing TRX file $trxPath : $_"
        }
    }
    
    return $totalCount
}

# Function to extract unique method names from full test names for retry filtering
# 
# WHY THIS IS NEEDED:
# The FHIR Server E2E tests use xUnit fixtures (IClassFixture) that append fixture info to test names.
# TRX files contain test names in this format:
#   ClassName(FixtureName, Format).MethodName(param1: value, param2: "string with special chars"...)
# 
# Example from actual test output:
#   FhirPathPatchTests(CosmosDb, Json).GivenAServerThatSupportsIt_WhenSubmittingInvalidFhirPatch_ThenServerShouldBadRequest(patchRequest: [···], expectedError: "Patch replace operations must have the 'path' part"···)
#
# These test names contain characters that break dotnet test --filter expressions:
#   - Parentheses: ( )
#   - Brackets: [ ]
#   - Quotes: " '
#   - Ellipses: ···
#   - Colons: :
#   - Commas in unexpected places
#
# When we try to use FullyQualifiedName~<full test name>, the filter silently fails to match
# and dotnet test returns exit code 0 with 0 tests run (false success).
#
# SOLUTION:
# Extract just the method name (e.g., "GivenAServerThatSupportsIt_WhenSubmittingInvalidFhirPatch_ThenServerShouldBadRequest")
# and use DisplayName~MethodName for filtering. This is reliable because method names only contain
# alphanumeric characters and underscores.
#
# TRADE-OFF:
# This may retry slightly more tests if the same method name exists in multiple test classes,
# but this is acceptable to ensure failed tests actually get retried.
function Get-MethodNamesFromTestNames {
    param(
        [string[]]$TestNames
    )
    
    $methodNames = @()
    foreach ($testName in $TestNames) {
        # Pattern: Find the method name after the fixture parentheses
        # Example: "FhirPathPatchTests(CosmosDb, Json).MethodName(patchRequest: ...)"
        # The regex looks for: closing paren, dot, then captures word characters until ( or end
        if ($testName -match '\)\.([\w_]+)(?:\(|$)') {
            $methodName = $Matches[1]
            if ($methodName -and $methodNames -notcontains $methodName) {
                $methodNames += $methodName
            }
        }
        elseif ($testName -match '\.([\w_]+)(?:\(|$)') {
            # Fallback for simpler test names without fixture parentheses
            # Example: "SimpleTestClass.TestMethod" or "SimpleTestClass.TestMethod(params)"
            $methodName = $Matches[1]
            if ($methodName -and $methodNames -notcontains $methodName) {
                $methodNames += $methodName
            }
        }
    }
    
    return $methodNames
}

# Initial test run
$attempt = 0
$initialResult = Invoke-DotNetTest -Assemblies $TestAssemblies -TestFilter $Filter -RunName "Initial" -AttemptNumber $attempt

$allTrxDirectories = @($initialResult.TrxDirectory)
$finalExitCode = $initialResult.ExitCode

Write-Host "##[section]Initial test run completed with exit code: $finalExitCode"

# If initial run failed and retries are enabled, retry failed tests
if ($finalExitCode -ne 0 -and $MaxRetries -gt 0) {
    $failedTests = Get-FailedTestsFromTrx -TrxDirectory $initialResult.TrxDirectory
    $previousRunCrashed = ($failedTests.Count -eq 0)
    $currentFilter = $Filter

    if ($previousRunCrashed) {
        Write-Host "##[warning]Tests failed but no failed tests found in TRX. This indicates a crash or infrastructure issue."
        Write-Host "##[warning]Will retry the previous filter configuration."
    }
    else {
        Write-Host "##[warning]Found $($failedTests.Count) failed test(s). Will retry up to $MaxRetries time(s)."
    }

    if ($failedTests.Count -gt 0 -or $previousRunCrashed) {
        
        for ($retryAttempt = 1; $retryAttempt -le $MaxRetries; $retryAttempt++) {
            Write-Host "##[section]Retry attempt $retryAttempt of $MaxRetries"
            
            $retryFilter = ""

            if ($previousRunCrashed) {
                Write-Host "Retrying with same filter due to previous crash: $currentFilter"
                $retryFilter = $currentFilter
            }
            else {
                # Extract method names for reliable filtering (see Get-MethodNamesFromTestNames for details)
                $methodNames = Get-MethodNamesFromTestNames -TestNames $failedTests
                
                if ($methodNames.Count -eq 0) {
                    Write-Host "##[warning]Could not extract method names from failed tests. Using full test names."
                    $methodNames = $failedTests
                }
                
                Write-Host "Extracted $($methodNames.Count) unique method name(s) for retry filter:"
                foreach ($name in $methodNames) {
                    Write-Host "  - $name"
                }
                
                # Build filter using DisplayName which works better for method name matching
                $retryFilter = ($methodNames | ForEach-Object { 
                    "DisplayName~$_" 
                }) -join "|"
                
                # Combine with original filter if it exists (and we aren't already just re-running it)
                if ($Filter) {
                    $retryFilter = "($Filter)&($retryFilter)"
                }
                
                Write-Host "Retrying with filter: $retryFilter"
                # Update current filter in case this attempt crashes and we need to retry it
                $currentFilter = $retryFilter
            }
            
            $retryResult = Invoke-DotNetTest -Assemblies $TestAssemblies -TestFilter $retryFilter -RunName "Retry" -AttemptNumber $retryAttempt
            $allTrxDirectories += $retryResult.TrxDirectory
            
            # Check if retry succeeded
            if ($retryResult.ExitCode -eq 0) {
                # Only check executed count if we weren't recovering from a crash (if crash, we might expect many tests)
                # Actually, checking count is always good.
                $executedCount = Get-ExecutedTestCountFromTrx -TrxDirectory $retryResult.TrxDirectory
                
                if (-not $previousRunCrashed) {
                    $expectedCount = $failedTests.Count
                    if ($executedCount -eq 0) {
                        Write-Host "##[error]Retry ran 0 tests but expected $expectedCount. Filter may not have matched any tests."
                        Write-Host "##[warning]The filter used was: $retryFilter"
                        break
                    }
                    elseif ($executedCount -lt $expectedCount) {
                        Write-Host "##[warning]Retry only ran $executedCount of $expectedCount expected tests. Some tests may not have matched the filter."
                    }
                }
                
                Write-Host "##[section]Retry attempt $retryAttempt passed ($executedCount tests executed)"
                $finalExitCode = 0
                break
            }
            else {
                # Get the still-failing tests for next retry
                $stillFailedTests = Get-FailedTestsFromTrx -TrxDirectory $retryResult.TrxDirectory
                
                if ($stillFailedTests.Count -eq 0) {
                    Write-Host "##[warning]Retry attempt $retryAttempt failed with no recorded test failures (Crash)."
                    $previousRunCrashed = $true
                    $failedTests = @() # Reset so we don't carry over old failures if we confusingly mixed states, though current filter handles it
                } else {
                    Write-Host "##[warning]$($stillFailedTests.Count) test(s) still failing after retry attempt $retryAttempt"
                    $failedTests = $stillFailedTests
                    $previousRunCrashed = $false
                }
                
                $finalExitCode = $retryResult.ExitCode
            }
        }
    }
}

# Output summary
Write-Host "##[section]Test Execution Summary"

# Collect all TRX files from all attempt directories
$allTrxFiles = @()
foreach ($dir in $allTrxDirectories) {
    $trxFiles = Get-TrxFilesFromDirectory -Directory $dir
    $allTrxFiles += $trxFiles
}

Write-Host "Total TRX files generated: $($allTrxFiles.Count)"
foreach ($trx in $allTrxFiles) {
    Write-Host "  - $trx"
}

# Coverage file discovery
$coverageFiles = Get-CoverageFilesFromDirectory -Directory $resultsDir
Write-Host "Coverage files found: $($coverageFiles.Count)"
foreach ($coverageFile in $coverageFiles) {
    Write-Host "  - $coverageFile"
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
