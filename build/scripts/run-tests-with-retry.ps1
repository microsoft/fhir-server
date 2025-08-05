param(
    [Parameter(Mandatory = $true)]
    [string]$TestCommand,
    
    [Parameter(Mandatory = $true)]
    [AllowEmptyString()]
    [string]$TestArguments,
    
    [Parameter(Mandatory = $true)]
    [string]$TestRunTitle,
    
    [Parameter(Mandatory = $false)]
    [string]$WorkingDirectory = ".",
    
    [Parameter(Mandatory = $false)]
    [double]$SuccessThreshold = 0.95
)

# Fixed retry configuration - 3 total attempts (1 initial + 2 retries)
$MaxRetries = 2
$MaxAttempts = 3

function Run-DotNetTest {
    param(
        [string]$Command,
        [string]$Arguments,
        [string]$WorkDir,
        [string]$RunTitle,
        [int]$AttemptNumber
    )
    
    $resultsDirectory = Join-Path $env:AGENT_TEMPDIRECTORY "TestResults"
    if (-not (Test-Path $resultsDirectory)) {
        New-Item -Path $resultsDirectory -ItemType Directory -Force | Out-Null
    }
    
    # Create unique TRX file name for this attempt
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $trxFileName = "$RunTitle-Attempt$AttemptNumber-$timestamp.trx"
    $trxFilePath = Join-Path $resultsDirectory $trxFileName
    
    Write-Host "=== Test Execution Attempt $AttemptNumber ==="
    Write-Host "Test Run Title: $RunTitle"
    Write-Host "Working Directory: $WorkDir"
    Write-Host "TRX Output: $trxFilePath"
    Write-Host "Test Arguments: $Arguments"
    
    # Build the argument list for dotnet test
    $argumentList = @()
    $argumentList += $Command
    
    # Split the test arguments and add them individually
    if (-not [string]::IsNullOrWhiteSpace($Arguments)) {
        # Split arguments respecting quotes - use regex to properly handle quoted strings
        $Arguments = $Arguments.Trim()
        $argMatches = [regex]::Matches($Arguments, '("[^"]*"|\S+)')
        foreach ($match in $argMatches) {
            $arg = $match.Value
            # Remove surrounding quotes if present
            if ($arg.StartsWith('"') -and $arg.EndsWith('"')) {
                $arg = $arg.Substring(1, $arg.Length - 2)
            }
            $argumentList += $arg
        }
    }
    
    # Add logger and results directory arguments
    $argumentList += "--logger"
    $argumentList += "trx;LogFileName=$trxFilePath"
    $argumentList += "--results-directory"
    $argumentList += $resultsDirectory
    $argumentList += "-v"
    $argumentList += "normal"
    
    $fullCommand = "dotnet " + ($argumentList -join " ")
    Write-Host "Executing: $fullCommand"
    Write-Host "Arguments passed to dotnet:"
    for ($i = 0; $i -lt $argumentList.Count; $i++) {
        Write-Host "  [$i]: '$($argumentList[$i])'"
    }
    Write-Host "==========================================="
    
    # Change to working directory
    $originalLocation = Get-Location
    try {
        Set-Location $WorkDir
        
        # Execute the dotnet test command
        $process = Start-Process -FilePath "dotnet" -ArgumentList $argumentList -NoNewWindow -Wait -PassThru -RedirectStandardOutput "$resultsDirectory\stdout-attempt$AttemptNumber.log" -RedirectStandardError "$resultsDirectory\stderr-attempt$AttemptNumber.log"
        
        Write-Host "Test execution completed with exit code: $($process.ExitCode)"
        
        # Display stdout and stderr
        if (Test-Path "$resultsDirectory\stdout-attempt$AttemptNumber.log") {
            $stdout = Get-Content "$resultsDirectory\stdout-attempt$AttemptNumber.log" -Raw
            if ($stdout) {
                Write-Host "=== STDOUT ==="
                Write-Host $stdout
                Write-Host "=============="
            }
        }
        
        if (Test-Path "$resultsDirectory\stderr-attempt$AttemptNumber.log") {
            $stderr = Get-Content "$resultsDirectory\stderr-attempt$AttemptNumber.log" -Raw
            if ($stderr) {
                Write-Host "=== STDERR ==="
                Write-Host $stderr
                Write-Host "=============="
            }
        }
        
        return @{
            ExitCode = $process.ExitCode
            TRXFile = $trxFilePath
        }
    }
    finally {
        Set-Location $originalLocation
    }
}

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

function Set-PipelineVariables {
    param(
        [hashtable]$Results,
        [int]$AttemptsMade
    )
    
    Write-Host "##vso[task.setvariable variable=TestTotalCount]$($Results.TotalTests)"
    Write-Host "##vso[task.setvariable variable=TestPassedCount]$($Results.PassedTests)"
    Write-Host "##vso[task.setvariable variable=TestFailedCount]$($Results.FailedTests)"
    Write-Host "##vso[task.setvariable variable=TestSuccessRate]$($Results.SuccessRate)"
    Write-Host "##vso[task.setvariable variable=TestAttemptsMade]$AttemptsMade"
}

# Main execution
Write-Host "Starting test execution with retry mechanism"
Write-Host "Test Run: $TestRunTitle"
Write-Host "Max Attempts: $MaxAttempts"
Write-Host "Success Threshold: $($SuccessThreshold * 100)%"

# Environment variables are passed through the Azure DevOps task env section

$attempt = 1
$shouldContinue = $true
$finalResult = $null

while ($shouldContinue -and $attempt -le $MaxAttempts) {
    Write-Host ""
    Write-Host "=========================================="
    Write-Host "STARTING ATTEMPT $attempt of $MaxAttempts"
    Write-Host "=========================================="
    
    # Run the test
    $testResult = Run-DotNetTest -Command $TestCommand -Arguments $TestArguments -WorkDir $WorkingDirectory -RunTitle $TestRunTitle -AttemptNumber $attempt
    
    # Analyze results
    $resultsPath = Join-Path $env:AGENT_TEMPDIRECTORY "TestResults"
    $analysis = Analyze-TestResults -ResultsPath $resultsPath -Threshold $SuccessThreshold
    
    # Determine if we should continue
    if ($analysis.ShouldRetry -and $attempt -lt $MaxAttempts) {
        Write-Host ""
        Write-Host "Success rate below threshold. Preparing for retry..."
        $attempt++
    } else {
        $shouldContinue = $false
        $finalResult = $analysis
    }
}

# Set pipeline variables with final results
Set-PipelineVariables -Results $finalResult -AttemptsMade $attempt

# Print final summary
Write-Host ""
Write-Host "=== FINAL TEST RETRY SUMMARY ==="
Write-Host "Test Run: $TestRunTitle"
Write-Host "Total Tests: $($finalResult.TotalTests)"
Write-Host "Passed Tests: $($finalResult.PassedTests)"
Write-Host "Failed Tests: $($finalResult.FailedTests)"
Write-Host "Success Rate: $([math]::Round([double]$finalResult.SuccessRate * 100, 2))%"
Write-Host "Success Threshold: $([math]::Round($SuccessThreshold * 100, 2))%"
Write-Host "Attempts Made: $attempt"
Write-Host "Max Attempts: $MaxAttempts"

if ($finalResult.SuccessRate -ge $SuccessThreshold) {
    Write-Host "Status: Tests passed threshold - SUCCESS" -ForegroundColor Green
    exit 0
} elseif ($finalResult.TotalTests -eq 0) {
    Write-Host "Status: No tests found - FAILED" -ForegroundColor Red
    exit 1
} else {
    Write-Host "Status: Tests failed threshold and max attempts reached - FAILED" -ForegroundColor Red
    exit 1
}