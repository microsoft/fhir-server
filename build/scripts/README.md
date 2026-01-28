# Test Retry Scripts

## Overview

This directory contains PowerShell scripts used in Azure DevOps pipelines to improve test reliability and reduce flakiness.

## Invoke-TestWithRetry.ps1

### Purpose

This script runs .NET tests with automatic retry of only the failed tests, rather than re-running the entire test suite. This approach:

- **Saves time**: Only failed tests are retried, not the entire suite
- **Maintains clear test results**: Each attempt generates its own TRX file
- **Works with Azure DevOps**: Results are published correctly to the test blade
- **Handles transient failures**: Automatically retries flaky tests without manual intervention

### Benefits over retryCountOnTaskFailure

The previous approach used Azure DevOps' `retryCountOnTaskFailure` on the `DotNetCoreCLI@2` task, which had several limitations:

1. **Timeout issues**: Re-running all tests took too much time
2. **Poor visibility**: Hard to see which tests actually failed vs. passed on retry
3. **Inefficient**: All tests re-run, even ones that passed

The new approach addresses these issues by:

1. **Selective retry**: Only failed tests are re-run
2. **Better logging**: Clear indication of initial failures and retry attempts
3. **Faster execution**: Significant time savings when only a few tests fail
4. **Multiple TRX files**: All attempts are recorded and published to Azure DevOps

### Usage

```yaml
- task: PowerShell@2
  displayName: 'Run E2E Tests with Retry'
  inputs:
    filePath: '$(Build.SourcesDirectory)/build/scripts/Invoke-TestWithRetry.ps1'
    arguments: >
      -TestAssemblies "$(Agent.TempDirectory)/E2ETests/**/*.Tests.E2E*.dll"
      -Filter "Category=E2E"
      -MaxRetries 1
      -WorkingDirectory "$(System.ArtifactsDirectory)"
      -AdditionalArgs "--blame-hang-timeout 15m"
      -TestRunTitle "E2E Tests"
  env:
    # Your test environment variables here
    'TestEnvironmentUrl': $(TestEnvironmentUrl)

- task: PublishTestResults@2
  displayName: 'Publish Test Results'
  condition: always()
  inputs:
    testResultsFormat: 'VSTest'
    testResultsFiles: '$(TestResultsDirectory)/**/*.trx'
    testRunTitle: 'E2E Tests'
    mergeTestResults: true
```

### Parameters

- **TestAssemblies** (required): The test assemblies or pattern to test (e.g., "**/*Tests.E2E*.dll")
- **Filter** (optional): Filter expression for initial test run (e.g., "FullyQualifiedName~CosmosDb")
- **MaxRetries** (optional): Maximum number of retry attempts for failed tests. Default is 1
- **WorkingDirectory** (optional): Working directory for test execution. Default is current directory
- **AdditionalArgs** (optional): Additional arguments to pass to dotnet test (e.g., "--collect 'XPlat Code Coverage'")
- **TestRunTitle** (optional): Title for the test run in Azure DevOps

### How It Works

1. **Initial Run**: Runs all tests matching the filter and generates a TRX file
2. **Parse Results**: If tests fail, parses the TRX to extract failed test names
3. **Retry Loop**: For each retry attempt (up to MaxRetries):
   - Builds a filter for only the failed tests
   - Runs tests again with the filtered set
   - Generates a new TRX file for this attempt
   - If all tests pass, stops and reports success
   - If tests still fail, updates the failed test list and continues
4. **Publish Results**: Sets the `TestResultsDirectory` variable for the PublishTestResults task
5. **Exit Code**: Returns 0 if all tests eventually pass, non-zero otherwise

### Example Output

```
##[section]Starting test run: E2E Tests
Test assemblies: **/E2ETests/**/*R4.Tests.E2E*.dll
Initial filter: FullyQualifiedName~SqlServer&Category!=ExportLongRunning
Max retries: 1
Results directory: /home/vsts/work/1/a/TestResults_20260128_183000

##[command]dotnet test **/E2ETests/**/*R4.Tests.E2E*.dll --logger "trx;LogFileName=/home/vsts/work/1/a/TestResults_20260128_183000/TestResults_Initial_Attempt0.trx" --filter "FullyQualifiedName~SqlServer&Category!=ExportLongRunning" --blame-hang-timeout 15m

##[section]Initial test run completed with exit code: 1
##[warning]Found 2 failed test(s). Will retry up to 1 time(s).

##[section]Retry attempt 1 of 1
Retrying 2 failed test(s)
##[command]dotnet test **/E2ETests/**/*R4.Tests.E2E*.dll --logger "trx;LogFileName=/home/vsts/work/1/a/TestResults_20260128_183000/TestResults_Retry_Attempt1.trx" --filter "(FullyQualifiedName~SqlServer&Category!=ExportLongRunning)&(FullyQualifiedName~Test1|FullyQualifiedName~Test2)" --blame-hang-timeout 15m

##[section]All previously failed tests passed on retry attempt 1

##[section]Test Execution Summary
Total TRX files generated: 2
  - /home/vsts/work/1/a/TestResults_20260128_183000/TestResults_Initial_Attempt0.trx
  - /home/vsts/work/1/a/TestResults_20260128_183000/TestResults_Retry_Attempt1.trx

##[section]Test run completed successfully
```

### Notes

- The script is compatible with xUnit 2.x (which is what the FHIR server uses)
- Custom XUnit components in Microsoft.Health.Fhir.Tests.Common work correctly with this approach
- All environment variables are passed through to the test execution
- TRX files from all attempts are preserved and published to Azure DevOps
- The script uses PowerShell Start-Process to properly capture test exit codes

### Future Improvements

If Azure DevOps adds native job-level retry support in the future, we could consider:

1. Moving to job-level retry for even cleaner separation
2. Using Azure DevOps retry strategies for better UI integration
3. Implementing more sophisticated retry logic (exponential backoff, different retry counts per test category, etc.)
