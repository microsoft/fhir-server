# FHIR Server Instance Configuration Service

## Overview

The `IFhirServerInstanceConfiguration` service provides **global, thread-safe access** to FHIR server instance metadata (base URI and vanity URL) that persists across HTTP requests and is available to background tasks and job processing that execute outside the HTTP request context.

This design solves a critical problem: background operations like reindexing, bulk import/export, and async jobs need access to the server's base URI and vanity URL, but they execute outside the scope of an HTTP request where the `RequestContextAccessor` is unavailable.

## Problem Statement

### Background Operations Need Server Metadata

Background services and async jobs in the FHIR server run independently of HTTP requests. They need to:
- Reference resources with proper URIs (e.g., `https://server.com/fhir/Patient/123`)
- Handle vanity URLs when configured
- Construct proper FHIR references for operations

However, these operations don't have access to `HttpContext` or `RequestContextAccessor<IFhirRequestContext>`, making it impossible to obtain the server's base URI during request processing.

### Previous Approach Limitations

Without this service, background operations would either:
1. Fail silently when unable to determine the server URI
2. Use hardcoded URIs (brittle and configuration-dependent)
3. Require complex workarounds to pass context information

## Solution Architecture

### Design Principles

1. **Lazy Initialization**: The configuration is populated on the first HTTP request via middleware
2. **Thread-Safe**: Uses `Interlocked` operations to ensure only one thread successfully initializes
3. **Idempotent**: Multiple initialization attempts are safe; only the first succeeds
4. **Minimal Overhead**: Values are captured once and cached for the application's lifetime
5. **Available Globally**: Registered as a singleton service accessible to all components

### Initialization Flow

```
┌─────────────────────────────────────────────────────────────┐
│ First HTTP Request Arrives                                   │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│ FhirRequestContextMiddleware.Invoke()                        │
│ - Extract base URI from HttpContext (build absolute URL)    │
│ - Extract vanity URL from X-MS-VANITY-URL header (optional) │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│ instanceConfiguration.Initialize(baseUri, vanityUrl)        │
│ - Thread-safe check: Interlocked.CompareExchange()          │
│ - Only first thread sets values                             │
│ - Subsequent calls are no-ops (idempotent)                  │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│ Configuration Cached for Application Lifetime               │
│ Available to:                                               │
│ - Background services (reindex, bulk operations)            │
│ - Async job processing                                      │
│ - Any component with access to IFhirServerInstanceConfig    │
└─────────────────────────────────────────────────────────────┘
```

### Key Components

#### IFhirServerInstanceConfiguration Interface

```csharp
public interface IFhirServerInstanceConfiguration
{
    /// <summary>
    /// Gets the base URI of the FHIR server instance.
    /// Populated on first HTTP request and cached for the lifetime of the application.
    /// </summary>
    Uri BaseUri { get; }

    /// <summary>
    /// Gets the vanity URI of the FHIR server instance.
    /// If not explicitly set, defaults to the base URI.
    /// Populated on first HTTP request and cached for the lifetime of the application.
    /// </summary>
    Uri VanityUrl { get; }

    /// <summary>
    /// Gets a value indicating whether the instance configuration has been initialized.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Initializes the instance configuration with server metadata.
    /// This method is idempotent - only the first call will succeed in setting values.
    /// </summary>
    void Initialize(string baseUriString, string vanityUrlString = null);
}
```

#### FhirServerInstanceConfiguration Implementation

The implementation uses:
- **`Interlocked.CompareExchange()`**: Atomic operation ensuring only one thread wins the initialization race
- **Private fields with lazy initialization**: `_cachedBaseUri`, `_cachedVanityUrl`, `_initialized` flag
- **Explicit vanity URL**: The `VanityUrl` property returns the explicitly set vanity URL or `null` if not provided. No fallback to base URI to avoid confusion.
- **Exception handling**: Invalid URIs don't throw; initialization is skipped gracefully

### Vanity URL Concept

A **vanity URL** is an optional alternative external URI for the FHIR server, configured via the `X-MS-VANITY-URL` header on HTTP requests.

**Use Cases:**
- Load balancers or reverse proxies with a single public-facing URI
- When internal URIs differ from the externally visible URI
- Single vanity URL deployments where the server is accessed through a custom domain

**Note**: This feature supports a **single vanity URL per application instance**. Multi-tenanted deployments with different vanity URLs for different tenants would require multiple server instances or a different architectural approach.

**Behavior:**
- If the `X-MS-VANITY-URL` header is present, it's stored as the vanity URL
- If not provided, `VanityUrl` returns `null` to avoid confusion
- Background operations can check if a vanity URL was explicitly configured before using it
- Stored in the global configuration for background services to use

## Integration Points

### 1. FhirRequestContextMiddleware

**Location**: `Microsoft.Health.Fhir.Api/Features/Context/FhirRequestContextMiddleware.cs`

On each HTTP request, the middleware:
1. Extracts the base URI from the request context
2. Reads the optional `X-MS-VANITY-URL` header
3. Calls `instanceConfiguration.Initialize(baseUri, vanityUrl)`
4. Sets the vanity URL in the response headers

```csharp
public async Task Invoke(
    HttpContext context,
    RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
    IFhirServerInstanceConfiguration instanceConfiguration,
    CorrelationIdProvider correlationIdProvider)
{
    // ... extract URIs from context ...
    
    if (!instanceConfiguration.IsInitialized)
    {
        instanceConfiguration.Initialize(baseUriInString, vanityUrlString);
    }
    
    // ... continue middleware processing ...
}
```

### 2. ReferenceSearchValueParser

**Location**: `Microsoft.Health.Fhir.Core/Features/Search/SearchValues/ReferenceSearchValueParser.cs`

The parser uses instance configuration when the request context is unavailable:

```csharp
public ReferenceSearchValueParser(
    RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
    IFhirServerInstanceConfiguration instanceConfiguration)
{
    _fhirRequestContextAccessor = fhirRequestContextAccessor;
    _instanceConfiguration = instanceConfiguration;
}

public ReferenceSearchValue Parse(string s)
{
    // Get base URI from request context first
    var requestContext = _fhirRequestContextAccessor?.RequestContext;
    var contextBaseUri = requestContext?.BaseUri ?? _instanceConfiguration?.BaseUri;
    
    // If a vanity URL was explicitly set, prefer it for comparison
    if (_instanceConfiguration?.VanityUrl != null)
    {
        contextBaseUri = _instanceConfiguration.VanityUrl;
    }
    
    // Determine if reference is internal or external based on base URI
    if (baseUri == contextBaseUri)
    {
        // Internal reference
    }
    else
    {
        // External reference
    }
}
```

This is critical for background operations like bulk import/export that need to identify internal vs. external resource references without an active HTTP context.

### 3. Service Registration

**Location**: `Microsoft.Health.Fhir.Shared.Api/Registration/FhirServerServiceCollectionExtensions.cs`

The service is registered as a singleton:

```csharp
services.AddSingleton<IFhirServerInstanceConfiguration, FhirServerInstanceConfiguration>();
```

This ensures:
- Single instance across the application lifetime
- Thread-safe initialization on first request
- Consistent values for all background operations

## Usage Patterns

### Pattern 1: Background Services Accessing Instance Configuration

```csharp
public class ReindexBackgroundService : IHostedService
{
    private readonly IFhirServerInstanceConfiguration _instanceConfiguration;
    
    public ReindexBackgroundService(IFhirServerInstanceConfiguration instanceConfiguration)
    {
        _instanceConfiguration = instanceConfiguration;
    }
    
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // Wait for configuration to be initialized
        if (!_instanceConfiguration.IsInitialized)
        {
            throw new InvalidOperationException(
                "Instance configuration not initialized. Ensure at least one HTTP request has been processed.");
        }
        
        // Use the cached URI
        var baseUri = _instanceConfiguration.BaseUri;
        var vanityUrl = _instanceConfiguration.VanityUrl; // May be null if not explicitly set
        
        // Use vanity URL if configured, otherwise use base URI for reference comparison
        var effectiveUri = vanityUrl ?? baseUri;
        
        // Process reindexing...
    }
}
```

### Pattern 2: Dependency Injection in Search Operations

```csharp
public class SearchService
{
    private readonly ReferenceSearchValueParser _referenceParser;
    
    public SearchService(IFhirServerInstanceConfiguration instanceConfiguration)
    {
        // ReferenceSearchValueParser uses instance configuration as fallback
        _referenceParser = new ReferenceSearchValueParser(
            requestContextAccessor: null, // No HTTP context available
            instanceConfiguration: instanceConfiguration);
    }
    
    public void ProcessBackgroundSearch()
    {
        // Parser will use instance configuration for base URI comparison
        var referenceValue = _referenceParser.Parse("https://server.com/fhir/Patient/123");
    }
}
```

### Pattern 3: Testing with Instance Configuration

```csharp
[Fact]
public void GivenAValidReferenceWhenRequestContextIsNull_WhenParsing_ThenFallsBackToInstanceConfiguration()
{
    // Arrange
    var nullContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
    nullContextAccessor.RequestContext.Returns((IFhirRequestContext)null);
    
    var instanceConfig = new FhirServerInstanceConfiguration();
    instanceConfig.Initialize("https://localhost/stu3/");
    
    var parser = new ReferenceSearchValueParser(nullContextAccessor, instanceConfig);
    
    // Act
    var value = parser.Parse("https://localhost/stu3/Observation/abc");
    
    // Assert - Should recognize as internal reference
    Assert.Equal(ReferenceKind.Internal, value.Kind);
    Assert.Equal(ResourceType.Observation.ToString(), value.ResourceType);
}
```

## Thread Safety

The implementation uses **`Interlocked` operations** to ensure thread-safe initialization:

```csharp
// Atomic compare-and-swap: only one thread sets _initialized from 0 to 1
if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
{
    // We won the race - set values
    BaseUri = baseUri;
    if (!string.IsNullOrWhiteSpace(vanityUrlString) &&
        Uri.TryCreate(vanityUrlString, UriKind.Absolute, out Uri vanityUrl))
    {
        VanityUrl = vanityUrl;
    }
}
// If _initialized was already 1, we lost the race; do nothing (idempotent)
```

**Race Condition Scenario:**
```
Thread 1: Check if initialized (no)     ──┐
Thread 2: Check if initialized (no)     ──┼─► Both see _initialized == 0
                                          │
Thread 1: CompareExchange(0→1) ✓         ──► Wins, sets values
Thread 2: CompareExchange(0→1) ✗         ──► Loses, returns 1, skips setting
```

Result: Only Thread 1's values are set, and all subsequent checks see the same values.

## Limitations and Considerations

### 1. Application Lifetime Persistence

Once initialized, values persist for the application lifetime. If the vanity URL changes:
- The application must be restarted to pick up the new value
- This is acceptable because server URIs rarely change during operation

### 2. First Request Dependency

Background services that start before any HTTP request will not have access to initialized configuration:

```csharp
// This may fail if called before first HTTP request
var baseUri = _instanceConfiguration.BaseUri; // Might be null
```

**Mitigation**: Check `IsInitialized` before accessing:

```csharp
if (!_instanceConfiguration.IsInitialized)
{
    // Wait or defer operation
    await Task.Delay(TimeSpan.FromSeconds(5));
}
```

### 3. Invalid URI Handling

The implementation gracefully handles invalid URIs:

```csharp
if (Uri.TryCreate(baseUriString, UriKind.Absolute, out Uri baseUri))
{
    // Initialization proceeds only if base URI is valid
}
// If invalid, initialization is skipped silently
```

## Testing Considerations

### Unit Testing

Provide mock implementations or real instances:

```csharp
[Fact]
public void TestWithInstanceConfiguration()
{
    var config = new FhirServerInstanceConfiguration();
    config.Initialize("https://localhost/fhir/");
    
    var service = new MyService(config);
    service.DoSomething(); // Uses config.BaseUri
}
```

### Integration Testing

Ensure at least one HTTP request is processed before background operations start:

```csharp
// In test setup
await httpClient.GetAsync("https://localhost/fhir/Patient"); // Triggers initialization

// Now background services can use configuration
var backgroundService = provider.GetRequiredService<IReindexBackgroundService>();
await backgroundService.Start();
```

### Test Cleanup

Reset the configuration between tests (using internal `ResetForTesting()` method):

```csharp
[Fixture]
public void Setup()
{
    var config = new FhirServerInstanceConfiguration();
    // ... test code ...
}

[Cleanup]
public void Teardown()
{
    config.ResetForTesting(); // Reset for next test
}
```

## Performance Impact

- **Minimal**: Values are captured once and cached
- **No per-request overhead**: After initialization, all accesses are simple property reads
- **Memory efficient**: Only two `Uri` objects cached in memory

## Related Headers and Features

### X-MS-VANITY-URL Header

- **Request**: Specifies the vanity URL for the server
- **Response**: Echoes the vanity URL (or base URI if not provided)
- **Use**: Multi-tenant deployments, custom domain mappings

### X-Request-Id and X-Correlation-Id

These headers continue to work alongside instance configuration:
- Correlation IDs track individual requests
- Instance configuration persists across all requests

## Migration Guide for Existing Code

### Before (No Instance Configuration)

```csharp
public class MyService
{
    private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
    
    public Uri GetBaseUri()
    {
        return _contextAccessor?.RequestContext?.BaseUri; // Null if no HTTP context
    }
}
```

### After (With Instance Configuration)

```csharp
public class MyService
{
    private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
    private readonly IFhirServerInstanceConfiguration _instanceConfiguration;
    
    public Uri GetBaseUri()
    {
        // Try request context first, fall back to instance configuration
        return _contextAccessor?.RequestContext?.BaseUri 
            ?? _instanceConfiguration?.BaseUri;
    }
}
```

## Troubleshooting

### Issue: `BaseUri` is null in background service

**Cause**: Service started before first HTTP request

**Solution**: 
```csharp
// Wait for initialization
while (!_instanceConfiguration.IsInitialized)
{
    await Task.Delay(100);
}
var baseUri = _instanceConfiguration.BaseUri;
```

### Issue: References not recognized as internal in background operations

**Cause**: Instance configuration not initialized

**Solution**: 
- Ensure at least one HTTP request has been processed
- Pass `IFhirServerInstanceConfiguration` to `ReferenceSearchValueParser`

### Issue: Vanity URL not being used

**Cause**: 
- Header not provided in request, or
- Application started before header was set

**Solution**:
- Send request with `X-MS-VANITY-URL` header before background operations
- Restart application if vanity URL needs to change

## See Also

- [FhirRequestContext Documentation](./RunningTheProject.md)
- [Bulk Import/Export Documentation](./BulkImport.md)
- [Background Service Architecture](./SearchParameterCacheRefreshBackgroundService.md)
