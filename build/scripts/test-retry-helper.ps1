param(
    [Parameter(Mandatory = $true)]
    [string]$TestResultsPath,
    
    [Parameter(Mandatory = $false)]
    [double]$SuccessThreshold = 0.95,
    
    [Parameter(Mandatory = $false)]
    [int]$MaxRetries = 2
)

# Function to analyze test results and determine if retry is needed
function Analyze-TestResults {
    param(
        [string]$ResultsPath,
        [double]$Threshold
    )
    
    Write-Host "Analyzing test results from: $ResultsPath"
    Write-Host "Success threshold: $($Threshold * 100)%"
    
    # Look for test result files (TRX format used by dotnet test)
    $testResultFiles = Get-ChildItem -Path $ResultsPath -Filter "*.trx" -Recurse -ErrorAction SilentlyContinue
    
    if ($testResultFiles.Count -eq 0) {
        Write-Host "No test result files found. Assuming tests failed and retry is needed."
        return @{
            ShouldRetry = $true
            TotalTests = 0
            PassedTests = 0
            FailedTests = 0
            SuccessRate = 0.0
        }
    }
    
    $totalTests = 0
    $passedTests = 0
    $failedTests = 0
    
    foreach ($file in $testResultFiles) {
        Write-Host "Processing test result file: $($file.FullName)"
        
        try {
            [xml]$trxContent = Get-Content $file.FullName
            
            # Parse TRX format - look for test results
            $testDefinitions = $trxContent.TestRun.TestDefinitions.UnitTest
            $testResults = $trxContent.TestRun.Results.UnitTestResult
            
            if ($testResults) {
                foreach ($result in $testResults) {
                    $totalTests++
                    if ($result.outcome -eq "Passed") {
                        $passedTests++
                    } elseif ($result.outcome -eq "Failed" -or $result.outcome -eq "Error" -or $result.outcome -eq "Timeout") {
                        $failedTests++
                        Write-Host "Failed test: $($result.testName) - Outcome: $($result.outcome)"
                    } else {
                        # Handle skipped, not executed, etc. - don't count as failed but mention them
                        Write-Host "Test skipped or not executed: $($result.testName) - Outcome: $($result.outcome)"
                    }
                }
            }
        }
        catch {
            Write-Warning "Failed to parse test result file $($file.FullName): $($_.Exception.Message)"
        }
    }
    
    $successRate = if ($totalTests -gt 0) { $passedTests / $totalTests } else { 0.0 }
    $shouldRetry = $successRate -lt $Threshold -and $failedTests -gt 0
    
    Write-Host "Test Results Summary:"
    Write-Host "  Total Tests: $totalTests"
    Write-Host "  Passed Tests: $passedTests"  
    Write-Host "  Failed Tests: $failedTests"
    Write-Host "  Success Rate: $($successRate * 100)%"
    Write-Host "  Should Retry: $shouldRetry"
    
    return @{
        ShouldRetry = $shouldRetry
        TotalTests = $totalTests
        PassedTests = $passedTests
        FailedTests = $failedTests
        SuccessRate = $successRate
    }
}

# Function to set Azure DevOps pipeline variables
function Set-PipelineVariables {
    param(
        [hashtable]$Results,
        [int]$RetryAttempt
    )
    
    Write-Host "##vso[task.setvariable variable=TestShouldRetry]$($Results.ShouldRetry)"
    Write-Host "##vso[task.setvariable variable=TestTotalCount]$($Results.TotalTests)"
    Write-Host "##vso[task.setvariable variable=TestPassedCount]$($Results.PassedTests)"
    Write-Host "##vso[task.setvariable variable=TestFailedCount]$($Results.FailedTests)"
    Write-Host "##vso[task.setvariable variable=TestSuccessRate]$($Results.SuccessRate)"
    Write-Host "##vso[task.setvariable variable=TestRetryAttempt]$RetryAttempt"
}

# Main execution
$currentRetry = if ($env:TestRetryAttempt) { [int]$env:TestRetryAttempt } else { 0 }

Write-Host "Starting test result analysis (Retry attempt: $currentRetry/$MaxRetries)"

$results = Analyze-TestResults -ResultsPath $TestResultsPath -Threshold $SuccessThreshold

# Don't retry if we've exceeded max retries
if ($currentRetry -ge $MaxRetries) {
    Write-Host "Maximum retry attempts ($MaxRetries) reached. Not retrying further."
    $results.ShouldRetry = $false
}

Set-PipelineVariables -Results $results -RetryAttempt $currentRetry

if ($results.ShouldRetry) {
    Write-Host "Tests will be retried due to success rate below threshold."
    exit 1  # Exit with error code to trigger retry
} else {
    if ($results.TotalTests -gt 0 -and $results.FailedTests -eq 0) {
        Write-Host "All tests passed successfully."
        exit 0
    } elseif ($results.TotalTests -gt 0 -and $results.SuccessRate -ge $SuccessThreshold) {
        Write-Host "Success rate meets threshold. Not retrying despite some test failures."
        exit 0
    } else {
        Write-Host "Tests failed and will not be retried (max retries reached or no tests found)."
        exit 1
    }
}