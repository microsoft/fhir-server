# Test Retry Framework

This document describes how to use the retry framework in the FHIR server test suite to handle flaky tests and transient failures.

## Overview

The retry framework provides attribute-based test retries for individual test methods. It's designed to handle common transient failures such as:

- Network timeouts
- Database connection issues
- Temporary service unavailability
- Resource contention
- Host crashes (at the pipeline level)

## Basic Usage

### Simple Retry

Replace `[Fact]` with `[RetryFact]` for tests that need retry capability:

```csharp
[RetryFact]
public void MyFlakyTest()
{
    // Test code that might occasionally fail due to transient issues
}
```

**Default behavior:**
- Maximum 3 retry attempts
- 1 second delay between retries
- Retries any test failure

### Custom Retry Settings

Configure retry behavior with parameters:

```csharp
[RetryFact(MaxRetries = 5, DelayMs = 2000)]
public void MyIntegrationTest()
{
    // Will retry up to 5 times with 2-second delays
}
```

### Exponential Backoff

Use exponential backoff for scenarios where the system might need time to recover:

```csharp
[RetryFact(MaxRetries = 4, DelayMs = 500, UseExponentialBackoff = true)]
public void MyE2ETest()
{
    // Delays: 500ms, 1000ms, 2000ms, 4000ms
}
```

## Recommended Settings by Test Category

### Unit Tests
- Generally should NOT use retries
- Unit tests should be deterministic and fast
- Use only for tests that interact with external resources

### Integration Tests
```csharp
[RetryFact(MaxRetries = 5, DelayMs = 2000)]
[Trait("Category", "Integration")]
public void DatabaseIntegrationTest()
{
    // Database operations that might have transient failures
}
```

### E2E Tests
```csharp
[RetryFact(MaxRetries = 3, DelayMs = 5000)]
[Trait("Category", "E2E")]
public void EndToEndTest()
{
    // Full system tests that might need longer recovery times
}
```

## When to Use Retries

✅ **Good candidates for retries:**
- Database connection timeouts
- HTTP request failures
- Resource initialization delays
- Container startup timing issues
- Network-related failures

❌ **Bad candidates for retries:**
- Logical test failures (assertions)
- Configuration errors
- Missing test data
- Code bugs

## Framework Features

### Automatic Failure Detection
The framework automatically retries on common transient exceptions:
- `TimeoutException`
- `HttpRequestException`
- `TaskCanceledException`
- `OperationCanceledException`
- `SocketException`
- SQL connection errors
- Database timeout errors

### Enhanced Logging
Failed attempts are logged with details:
```
Test MyTest failed on attempt 1/3. Retrying in 1000ms due to: TimeoutException: Operation timed out
```

### Compatible with Existing Framework
- Works with existing test traits and categories
- Compatible with `IClassFixture<T>` tests
- Integrates with the existing `IClassFixtureExtensions.Retry()` methods

## Migration Guide

### Step 1: Identify Flaky Tests
Look for tests that occasionally fail in CI/CD with transient errors:
- Timeout exceptions
- Connection failures
- "Host process crashed" errors
- Intermittent assertion failures

### Step 2: Apply Retry Attributes
Replace `[Fact]` with `[RetryFact]` for identified tests:

```csharp
// Before
[Fact]
public void MyFlakyTest() { ... }

// After
[RetryFact(MaxRetries = 3, DelayMs = 1000)]
public void MyFlakyTest() { ... }
```

### Step 3: Configure Based on Test Type
Use appropriate retry settings:
- **Integration tests**: 5 retries, 2-3 second delays
- **E2E tests**: 3 retries, 5-10 second delays
- **Unit tests**: Avoid retries or minimal (2 retries, 1 second)

## Best Practices

1. **Start Conservative**: Begin with default settings and adjust based on failure patterns
2. **Monitor Results**: Track retry success rates to optimize settings
3. **Category-Specific**: Use different settings for different test categories
4. **Document Reasons**: Add comments explaining why a test needs retries
5. **Fix Root Causes**: Retries are a mitigation, not a permanent solution

## Example Usage

```csharp
public class MyTestClass : IClassFixture<TestFixture>
{
    [RetryFact]  // Default: 3 retries, 1s delay
    public void QuickRetryTest() { ... }
    
    [RetryFact(MaxRetries = 5, DelayMs = 2000)]
    [Trait("Category", "Integration")]
    public void DatabaseTest() { ... }
    
    [RetryFact(MaxRetries = 3, DelayMs = 5000, UseExponentialBackoff = true)]
    [Trait("Category", "E2E")]
    public void EndToEndTest() { ... }
}
```

## Troubleshooting

### Test Still Failing After Retries
- Check if the exception type is retryable
- Increase retry count or delay
- Investigate root cause of failures

### Too Many Retries
- Tests taking too long to run
- Reduce retry count or delay
- Fix underlying stability issues

### Retries Not Working
- Verify you're using `[RetryFact]` instead of `[Fact]`
- Check that the test framework recognizes the attribute
- Ensure proper using statements: `using Microsoft.Health.Extensions.Xunit;`
