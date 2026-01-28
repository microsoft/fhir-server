# Test Retry Implementation - Validation Checklist

This document provides a checklist for validating the test retry implementation in a real PR build.

## What Was Changed

### New Files
1. **build/scripts/Invoke-TestWithRetry.ps1** - PowerShell script that retries only failed tests
2. **build/scripts/README.md** - Documentation for the retry mechanism
3. **build/scripts/VALIDATION.md** - This file

### Modified Files
1. **build/jobs/e2e-tests.yml**
   - Changed from `DotNetCoreCLI@2` with `retryCountOnTaskFailure: 1` to `PowerShell@2` running the retry script
   - Added `PublishTestResults@2` task to publish TRX results
   
2. **build/jobs/run-cosmos-tests.yml**
   - Removed `retryCountOnTaskFailure: 1` from integration test task
   
3. **build/jobs/run-sql-tests.yml**
   - Removed `retryCountOnTaskFailure: 1` from integration test task
   
4. **build/jobs/run-export-tests.yml**
   - Removed `retryCountOnTaskFailure: 1` from both CosmosDB and SQL export test tasks

## Validation Steps

### 1. Pre-Validation Checks
- [ ] Confirm all files are committed and pushed
- [ ] Verify the PR build pipeline has started
- [ ] Check that the pipeline can access the new PowerShell script

### 2. E2E Test Execution Validation

For each E2E test job (cosmosE2eTests, sqlE2eTests, etc.):

- [ ] **Initial Run Logging**
  - Look for `##[section]Starting test run:` message
  - Verify test assemblies and filter are correct
  - Check that `Results directory:` is set
  
- [ ] **Test Execution**
  - Look for `##[command]dotnet test ...` with proper arguments
  - Verify `--blame-hang-timeout 15m` is present
  - Check that environment variables are available to tests
  
- [ ] **Retry Behavior (if tests fail)**
  - Look for `##[warning]Found N failed test(s)` message
  - Verify `##[section]Retry attempt 1 of 1` appears
  - Check that retry filter shows only failed test names
  - Confirm `Retrying N failed test(s)` message
  - Look for either:
    - `##[section]All previously failed tests passed on retry attempt 1` (success)
    - `##[warning]N test(s) still failing after retry attempt 1` (still failing)

- [ ] **Test Results Publishing**
  - Verify `##vso[task.setvariable variable=TestResultsDirectory]` is set
  - Check that `Publish E2E Test Results` task runs
  - Look for `Publishing test results from` message
  - Confirm test results appear in Azure DevOps Test tab
  - Verify test counts are correct (initial + retry attempts)
  - Check that `mergeTestResults: true` combined the results properly

### 3. Integration Test Validation

For integration test jobs (CosmosIntegrationTests, SqlIntegrationTests):

- [ ] **Cosmos Integration Tests**
  - Verify tests run with `--filter DisplayName!~SqlServer`
  - Check that code coverage is collected
  - Confirm coverage is published
  - Verify NO automatic retry happens (we removed retryCountOnTaskFailure)
  
- [ ] **SQL Integration Tests**
  - Verify tests run with `--filter DisplayName!~CosmosDb`
  - Check that code coverage is collected
  - Confirm coverage is published
  - Verify NO automatic retry happens (we removed retryCountOnTaskFailure)

### 4. Export Test Validation

- [ ] **Cosmos Export Tests**
  - Verify `FullyQualifiedName~CosmosDb&Category=ExportLongRunning` filter
  - Check NO automatic retry (removed retryCountOnTaskFailure)
  
- [ ] **SQL Export Tests**
  - Verify `FullyQualifiedName~SqlServer&Category=ExportLongRunning` filter
  - Check NO automatic retry (removed retryCountOnTaskFailure)

### 5. Time Comparison

To validate time savings:

- [ ] **Baseline (if available)**
  - Find a recent PR build before these changes
  - Note the duration of E2E test jobs
  - Count how many times tests were retried
  
- [ ] **With New Implementation**
  - Note the duration of E2E test jobs
  - If tests fail and retry, note how long retry takes
  - Compare: retrying only failed tests should be much faster than retrying all tests

### 6. Infrastructure Task Validation

Ensure infrastructure tasks still have their retries:

- [ ] Check `add-aad-test-environment.yml` still has `retryCountOnTaskFailure: 1`
- [ ] Check `provision-deploy.yml` still has `retryCountOnTaskFailure: 1`
- [ ] Check `provision-sqlServer.yml` still has `retryCountOnTaskFailure: 1`
- [ ] Check `build.yml` restore/build tasks still have `retryCountOnTaskFailure: 1`

## Expected Outcomes

### Success Scenario (All Tests Pass First Time)
```
##[section]Starting test run: R4 SqlServer
Test assemblies: **/E2ETests/**/*R4.Tests.E2E*.dll
Initial filter: FullyQualifiedName~SqlServer&Category!=ExportLongRunning
Max retries: 1
Results directory: /home/vsts/work/1/a/TestResults_20260128_183000

##[command]dotnet test ...
[Test execution output]

##[section]Initial test run completed with exit code: 0
##[section]Test Execution Summary
Total TRX files generated: 1
  - /home/vsts/work/1/a/TestResults_20260128_183000/TestResults_Initial_Attempt0.trx
##[section]Test run completed successfully

[Publish Test Results task runs and publishes the TRX]
```

### Retry Scenario (Some Tests Fail, Then Pass)
```
##[section]Starting test run: R4 SqlServer
[... initial run ...]
##[section]Initial test run completed with exit code: 1
##[warning]Found 3 failed test(s). Will retry up to 1 time(s).

##[section]Retry attempt 1 of 1
Retrying 3 failed test(s)
##[command]dotnet test ... --filter "(FullyQualifiedName~SqlServer&Category!=ExportLongRunning)&(FullyQualifiedName~Test1|FullyQualifiedName~Test2|FullyQualifiedName~Test3)"
[Test execution output]

##[section]All previously failed tests passed on retry attempt 1
##[section]Test Execution Summary
Total TRX files generated: 2
  - /home/vsts/work/1/a/TestResults_20260128_183000/TestResults_Initial_Attempt0.trx
  - /home/vsts/work/1/a/TestResults_20260128_183000/TestResults_Retry_Attempt1.trx
##[section]Test run completed successfully

[Publish Test Results task merges both TRX files]
```

### Failure Scenario (Tests Fail After Retry)
```
##[section]Starting test run: R4 SqlServer
[... initial run ...]
##[section]Initial test run completed with exit code: 1
##[warning]Found 2 failed test(s). Will retry up to 1 time(s).

##[section]Retry attempt 1 of 1
Retrying 2 failed test(s)
[... retry run ...]
##[warning]2 test(s) still failing after retry attempt 1

##[section]Test Execution Summary
Total TRX files generated: 2
  - /home/vsts/work/1/a/TestResults_20260128_183000/TestResults_Initial_Attempt0.trx
  - /home/vsts/work/1/a/TestResults_20260128_183000/TestResults_Retry_Attempt1.trx
##[error]Test run failed with exit code: 1

[Publish Test Results task still publishes both TRX files]
[Job fails as expected]
```

## Troubleshooting

### Issue: Script not found
**Symptom**: "The term './build/scripts/Invoke-TestWithRetry.ps1' is not recognized"
**Solution**: Verify the file exists and the path in e2e-tests.yml is correct

### Issue: Environment variables not available
**Symptom**: Tests fail with missing configuration
**Solution**: Check that the `env:` section in e2e-tests.yml is properly indented under the PowerShell@2 task

### Issue: TRX files not published
**Symptom**: No test results in Azure DevOps Test tab
**Solution**: 
- Check that `TestResultsDirectory` variable is set by the script
- Verify PublishTestResults@2 task ran with `condition: always()`
- Check the path pattern: `$(TestResultsDirectory)/**/*.trx`

### Issue: Retry not happening
**Symptom**: Tests fail but no retry attempt
**Solution**:
- Check that `MaxRetries` parameter is set to 1 or higher
- Verify TRX file is being generated and is valid XML
- Look for warnings about TRX parsing errors

### Issue: All tests retrying instead of just failed ones
**Symptom**: Retry takes as long as initial run
**Solution**:
- Check the retry filter in logs - should have `FullyQualifiedName~TestName1|FullyQualifiedName~TestName2`
- Verify TRX parsing is working (look for "Found N failed test(s)" message)
- Check test names don't contain characters that break the filter

## Sign-Off

After completing validation:

- [ ] All E2E tests execute correctly with retry capability
- [ ] Test results are properly published to Azure DevOps
- [ ] Failed tests are correctly identified and retried
- [ ] Time savings observed when retrying only failed tests
- [ ] Infrastructure tasks still have their original retry logic
- [ ] No regressions in test execution or reporting

## Notes

Record any issues or observations here:

```
[Add notes during validation]
```
