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
1. Fail when unable to determine the server URI
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
    /// Initializes the base URI of the instance configuration.
    /// This method is idempotent and thread-safe - only the first caller will succeed in setting the value.
    /// </summary>
    /// <param name="baseUriString">The base URI string of the FHIR server.</param>
    /// <returns>True if the base URI is initialized (either by this call or a previous call); false if the URI is invalid.</returns>
    bool InitializeBaseUri(string baseUriString);
}
```

#### FhirServerInstanceConfiguration Implementation

The implementation uses:
- **`Interlocked.CompareExchange()`**: Atomic operation ensuring only one thread wins the initialization race
- **Private fields with lazy initialization**: `_cachedBaseUri`, `_baseUriInitialized` flag
- **Single initialization**: Only the base URI is cached; simpler than the original design
- **Return-based status**: `InitializeBaseUri()` returns `bool` indicating success or prior initialization

### Vanity URL Concept

**Note**: The current implementation focuses on base URI initialization. Vanity URL support was considered but is not currently implemented in this version.

## Integration Points

### 1. FhirRequestContextMiddleware

**Location**: `Microsoft.Health.Fhir.Api/Features/Context/FhirRequestContextMiddleware.cs`

On each HTTP request, the middleware initializes the base URI from the request context, but only if the request is not from a loopback/local address (to avoid initializing from health check requests):

1. Extracts the base URI from the request context
2. Checks if the request is from a loopback or local IP
3. If external request, calls `instanceConfiguration.InitializeBaseUri(baseUri)`
4. Continues with middleware processing

```csharp
public async Task Invoke(
    HttpContext context,
    RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
    IFhirServerInstanceConfiguration instanceConfiguration,
    CorrelationIdProvider correlationIdProvider)
{
    // ... extract baseUri from context ...
    
    // Initialize the global instance configuration on first external request (thread-safe, idempotent)
    // Skip initialization if the request is from a loopback/local IP to avoid using health check requests
    if (!FhirRequestContextMiddlewareExtensions.IsLoopbackOrLocalRequest(context.Request.Host.Host))
    {
        instanceConfiguration.InitializeBaseUri(baseUriInString);
    }
    
    // ... continue middleware processing ...
}
```

**Loopback Detection**: The middleware includes a helper method `IsLoopbackOrLocalRequest()` that identifies local addresses:
- `localhost`
- `127.x.x.x` range (IPv4 loopback)
- `::1` (IPv6 loopback)
- `192.168.x.x`, `10.x.x.x`, `172.16-31.x.x` ranges (private networks)

### 2. ReferenceSearchValueParser

**Location**: `Microsoft.Health.Fhir.Core/Features/Search/SearchValues/ReferenceSearchValueParser.cs`

The parser uses instance configuration as a fallback when the request context is unavailable:

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
    // Get base URI from request context first, then fall back to global instance configuration
    // This ensures background operations like reindexing can access the base URI when there's no active HTTP context
    var requestContext = _fhirRequestContextAccessor?.RequestContext;
    var contextBaseUri = requestContext?.BaseUri ?? _instanceConfiguration?.BaseUri;
    
    // Determine if reference is internal or external based on base URI
    if (contextBaseUri != null && baseUri == contextBaseUri)
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
        // Wait for configuration to be initialized if needed
        if (_instanceConfiguration.BaseUri == null)
        {
            throw new InvalidOperationException(
                "Instance configuration not initialized. Ensure at least one external HTTP request has been processed.");
        }
        
        // Use the cached URI
        var baseUri = _instanceConfiguration.BaseUri;
        
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
    bool initialized = instanceConfig.InitializeBaseUri("https://localhost/stu3/");
    
    var parser = new ReferenceSearchValueParser(nullContextAccessor, instanceConfig);
    
    // Act
    var value = parser.Parse("https://localhost/stu3/Observation/abc");
    
    // Assert - Should recognize as internal reference
    Assert.True(initialized);
    Assert.Equal(ReferenceKind.Internal, value.Kind);
    Assert.Equal(ResourceType.Observation.ToString(), value.ResourceType);
}
```

## Thread Safety

The implementation uses **`Interlocked` operations** to ensure thread-safe initialization:

```csharp
// Atomic compare-and-swap: only one thread sets _baseUriInitialized from 0 to 1
if (Uri.TryCreate(baseUriString, UriKind.Absolute, out Uri baseUri) &&
    Interlocked.CompareExchange(ref _baseUriInitialized, 1, 0) == 0)
{
    // We won the race - set the value
    BaseUri = baseUri;
}

// If validation failed or we lost the race (return value != 0), return the current state
return _baseUriInitialized != 0;
```

**Race Condition Scenario:**
```
Thread 1: Check URI validity (valid) ──┐
Thread 2: Check URI validity (valid) ──┼─► Both have valid URIs
                                        │
Thread 1: CompareExchange(0→1) ✓       ──► Wins, sets value, returns true
Thread 2: CompareExchange(0→1) ✗       ──► Loses, skips setting, returns true
```

Result: Only Thread 1's value is set, but both calls return `true` indicating successful initialization.

## Limitations and Considerations

### 1. Application Lifetime Persistence

Once initialized, the base URI persists for the application lifetime. If the server URI changes:
- The application must be restarted to pick up the new value
- This is acceptable because server URIs rarely change during operation

### 2. External Request Dependency

Background services that start before any external HTTP request will not have access to initialized configuration:

```csharp
// This may be null if called before first external HTTP request
var baseUri = _instanceConfiguration.BaseUri; // Might be null
```

**Mitigation**: Check if `BaseUri` is initialized before accessing:

```csharp
if (_instanceConfiguration.BaseUri == null)
{
    // Wait or defer operation
    await Task.Delay(TimeSpan.FromSeconds(5));
}
```

**Note**: Loopback requests (health checks, localhost) do not initialize the configuration. Only external requests trigger initialization.

### 3. Invalid URI Handling

The implementation gracefully handles invalid URIs through `Uri.TryCreate()`:

```csharp
if (Uri.TryCreate(baseUriString, UriKind.Absolute, out Uri baseUri) &&
    Interlocked.CompareExchange(ref _baseUriInitialized, 1, 0) == 0)
{
    // Initialization proceeds only if base URI is valid and we win the race
    BaseUri = baseUri;
}
// If invalid or we lost the race, returns false
```

## Testing Considerations

### Unit Testing

Provide mock implementations or real instances:

```csharp
[Fact]
public void TestWithInstanceConfiguration()
{
    var config = new FhirServerInstanceConfiguration();
    bool initialized = config.InitializeBaseUri("https://localhost/fhir/");
    
    var service = new MyService(config);
    service.DoSomething(); // Uses config.BaseUri
    
    Assert.True(initialized);
    Assert.NotNull(config.BaseUri);
}
```

### Integration Testing

Ensure at least one external HTTP request is processed before background operations start:

```csharp
// In test setup - make a request from an external host (not localhost)
await httpClient.GetAsync("https://api.example.com/fhir/Patient"); // Triggers initialization

// Now background services can use configuration
var backgroundService = provider.GetRequiredService<IReindexBackgroundService>();
await backgroundService.Start();
```

**Note**: Requests from `localhost`, `127.0.0.1`, or other loopback/private IPs do not initialize the configuration.

### Test Cleanup

The current implementation doesn't expose a public reset method, so tests should use separate instances:

```csharp
[Fact]
public void TestConfigurationIsolation()
{
    // Each test gets its own isolated instance
    var config1 = new FhirServerInstanceConfiguration();
    var config2 = new FhirServerInstanceConfiguration();
    
    // They initialize independently
    config1.InitializeBaseUri("https://server1.com/fhir/");
    config2.InitializeBaseUri("https://server2.com/fhir/");
    
    // Each maintains its own state
    Assert.Equal(new Uri("https://server1.com/fhir/"), config1.BaseUri);
    Assert.Equal(new Uri("https://server2.com/fhir/"), config2.BaseUri);
}
```

## Performance Impact

- **Minimal**: Values are captured once and cached
- **No per-request overhead**: After initialization, all accesses are simple property reads
- **Memory efficient**: Only two `Uri` objects cached in memory

## Related Headers and Features

### X-Request-Id and X-Correlation-Id

These headers continue to work alongside instance configuration:
- Correlation IDs track individual requests
- Instance configuration persists across all requests
- Loopback requests (e.g., health checks) don't affect instance configuration initialization

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

**Cause**: Service started before first external HTTP request, or only loopback requests have been made

**Solution**: 
```csharp
// Wait for initialization
int attempts = 0;
while (_instanceConfiguration.BaseUri == null && attempts < 50)
{
    await Task.Delay(100);
    attempts++;
}

if (_instanceConfiguration.BaseUri == null)
{
    throw new InvalidOperationException("Instance configuration not initialized after waiting.");
}

var baseUri = _instanceConfiguration.BaseUri;
```

**Prevention**: Ensure external requests are made to the server before background operations start.

### Issue: References not recognized as internal in background operations

**Cause**: Instance configuration not initialized or BaseUri is null

**Solution**: 
- Ensure at least one external HTTP request has been processed before background operations
- Pass `IFhirServerInstanceConfiguration` to `ReferenceSearchValueParser`
- Verify non-loopback hosts are making the requests

### Issue: InitializeBaseUri returns false

**Cause**: Invalid URI string provided

**Solution**:
```csharp
// Validate URI before initializing
if (Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
{
    bool success = _instanceConfiguration.InitializeBaseUri(uriString);
    // success should be true
}
else
{
    throw new ArgumentException("Invalid URI format");
}
```

## See Also

- [FhirRequestContext Documentation](./RunningTheProject.md)
- [Bulk Import/Export Documentation](./BulkImport.md)
- [Background Service Architecture](./SearchParameterCacheRefreshBackgroundService.md)
