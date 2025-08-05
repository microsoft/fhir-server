# Automated Test Retry Mechanism

This directory contains the implementation of an automated retry mechanism for flaky integration and E2E tests in CI and PR pipelines.

## Overview

The retry mechanism automatically retries test executions when the success rate falls below 95% and the number of failures suggests flaky tests rather than systematic issues.

## Components

### 1. PowerShell Script (`test-retry-helper.ps1`)
- Analyzes TRX test result files
- Calculates success/failure rates
- Determines whether tests should be retried based on configurable thresholds
- Sets Azure DevOps pipeline variables for retry logic

### 2. Retry Template (`retry-tests.yml`)
- Reusable YAML template that wraps test execution with retry logic
- Supports up to 3 total attempts (1 initial + 2 retries)
- Automatically publishes merged test results from all attempts
- Provides detailed retry summary information

### 3. Updated Test Templates
- `run-tests.yml`: Integration tests now use retry mechanism
- `e2e-tests.yml`: E2E tests now use retry mechanism

## Configuration

### Parameters
- `successThreshold`: Minimum success rate to avoid retry (default: 0.95 = 95%)

### Usage Example
```yaml
- template: retry-tests.yml
  parameters:
    testCommand: test
    testArguments: '"path/to/tests.dll" --filter SomeFilter'
    testRunTitle: 'My Test Suite'
    successThreshold: 0.95
    environmentVariables:
      SOME_ENV_VAR: 'value'
```

## How It Works

1. **Initial Test Run**: Tests execute normally
2. **Result Analysis**: PowerShell script analyzes TRX files to calculate success rate
3. **Retry Decision**: If success rate < threshold and retries available, retry is triggered
4. **Retry Execution**: Tests run again with same parameters
5. **Final Analysis**: After all attempts, final results are evaluated
6. **Result Publishing**: All test results are merged and published to Azure DevOps

## Benefits

- **Reduced Pipeline Delays**: No manual intervention needed for flaky test failures
- **Improved Success Rate**: Genuine test issues are separated from transient failures
- **Complete Visibility**: All retry attempts are logged and test results are preserved
- **Configurable Thresholds**: Teams can adjust sensitivity based on their test stability

## Integration with Azure DevOps

- Uses Azure DevOps built-in flaky test management for test result visualization
- Preserves all test execution history including retry attempts
- Compatible with existing test result analysis and reporting tools
- Pipeline variables track retry attempts and success rates for monitoring

## Monitoring

The retry mechanism provides detailed logging including:
- Number of tests executed, passed, and failed
- Success rate calculation
- Retry attempt counts
- Final status determination

This information appears in pipeline logs and can be used for monitoring test stability over time.