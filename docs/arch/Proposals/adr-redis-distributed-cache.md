# ADR: Redis Distributed Cache for Search Parameter Caching

Labels: [Caching](https://github.com/microsoft/fhir-server/labels/Area-Caching), [Performance](https://github.com/microsoft/fhir-server/labels/Area-Performance)

## Context

The FHIR server previously used only in-memory caching for search parameters with database polling to synchronize updates across multiple instances. This approach had several limitations:

1. **Database Load**: Each instance polled the database periodically (via `GetAndApplySearchParameterUpdates`) to check for search parameter changes
2. **Eventual Consistency**: Changes to search parameters took time to propagate across all instances
3. **Reindexing Impact**: Reindex operations had to call `GetAndApplySearchParameterUpdates` to ensure latest parameters were applied
4. **Scalability**: Database became a bottleneck as the number of instances increased

## Decision

We have implemented Redis as a distributed cache for search parameter status and metadata with conditional enablement:

### Implemented Components

1. **IDistributedCache<T>**: Generic interface for distributed cache operations
2. **RedisDistributedCache<T>**: Generic Redis implementation with compression, versioning, and distributed locking
3. **RedisSearchParameterCache**: Specialized implementation for search parameter caching
4. **ISearchParameterCache**: Interface for search parameter distributed caching
5. **FhirServerCachingConfiguration & RedisConfiguration**: Configuration classes for Redis settings
6. **SearchModule Integration**: Factory pattern service registration with conditional Redis enablement

### Architecture Overview

**Redis Enabled Configuration:**
```
┌─────────────────────────────────┐    ┌─────────────────────────────────┐
│        FHIR Instance A          │    │        FHIR Instance B          │
│                                 │    │                                 │
│  ┌─────────────────────────────┐ │    │ ┌─────────────────────────────┐ │
│  │ SearchParameterOperations A │ │    │ │ SearchParameterOperations B │ │
│  └─────────────┬───────────────┘ │    │ └─────────────┬───────────────┘ │
│                │                 │    │               │                 │
│  ┌─────────────▼───────────────┐ │    │ ┌─────────────▼───────────────┐ │
│  │SearchParameterStatusMgr A   │◄┼────┼►│SearchParameterStatusMgr B   │ │
│  │  (uses RedisSearchParam     │ │    │ │  (uses RedisSearchParam     │ │
│  │        Cache)               │ │    │ │        Cache)               │ │
│  └─────────────┬───────────────┘ │    │ └─────────────┬───────────────┘ │
└─────────────────┼─────────────────┘    └─────────────────┼─────────────────┘
                  │                                        │
                  └────────────┬───────────────────────────┘
                               │
                  ┌────────────▼────────────┐
                  │                         │
                  │  Redis Distributed      │
                  │       Cache             │
                  │                         │
                  └────────────┬────────────┘
                               │
                  ┌────────────▼────────────┐
                  │                         │
                  │    Database Store       │
                  │   (Source of Truth)     │
                  │                         │
                  └─────────────────────────┘
```

**Redis Disabled Configuration:**
```
┌─────────────────────────────────┐    ┌─────────────────────────────────┐
│        FHIR Instance A          │    │        FHIR Instance B          │
│                                 │    │                                 │
│  ┌─────────────────────────────┐ │    │ ┌─────────────────────────────┐ │
│  │ SearchParameterOperations A │ │    │ │ SearchParameterOperations B │ │
│  └─────────────┬───────────────┘ │    │ └─────────────┬───────────────┘ │
│                │                 │    │               │                 │
│  ┌─────────────▼───────────────┐ │    │ ┌─────────────▼───────────────┐ │
│  │SearchParameterStatusMgr A   │ │    │ │SearchParameterStatusMgr B   │ │
│  │  (in-memory cache only)     │ │    │ │  (in-memory cache only)     │ │
│  └─────────────┬───────────────┘ │    │ └─────────────┬───────────────┘ │
└─────────────────┼─────────────────┘    └─────────────────┼─────────────────┘
                  │                                        │
                  └────────────┬───────────────────────────┘
                               │
                  ┌────────────▼────────────┐
                  │                         │
                  │    Database Store       │
                  │ (polled periodically)   │
                  │                         │
                  └─────────────────────────┘
```

### Implementation Strategy

### Current Configuration
Redis configuration is embedded in appsettings.json:
```json
{
  "FhirServer": {
    "Caching": {
      "Redis": {
        "Enabled": true,
        "ConnectionString": "localhost:6379",
        "Database": 0,
        "OperationTimeout": "00:00:05",
        "RetryAttempts": 3,
        "EnablePubSub": true,
        "InvalidationChannelName": "fhir:cache:invalidation",
        "CacheTypes": {
          "SearchParameters": {
            "CacheExpiry": "01:00:00",
            "KeyPrefix": "fhir:searchparams",
            "EnableVersioning": true,
            "EnableCompression": true
          }
        }
      }
    }
  }
}
```

#### 2. Cache Strategy Implementation
The implementation uses **either/or** caching strategy:
- **Redis Enabled**: Uses `RedisSearchParameterCache` for distributed caching across instances
- **Redis Disabled**: Uses standard in-memory caching with database polling
- **Database**: Always serves as the source of truth for both configurations

#### 3. Service Registration
Implemented factory pattern in SearchModule:
```csharp
services.AddSingleton<ISearchParameterCache>(provider =>
{
    var cachingConfig = provider.GetRequiredService<IOptions<FhirServerCachingConfiguration>>();
    
    if (cachingConfig.Value.Redis.Enabled)
    {
        return new RedisSearchParameterCache(distributedCache, searchParamConfig, logger, dataStore);
    }
    else
    {
        return null; // No distributed caching
    }
});
```

## Status
Implemented ✅

## Implementation Status

### ✅ Completed Features
1. **Redis Configuration**: Complete configuration system with support for cache-specific settings
2. **Generic Redis Cache**: `RedisDistributedCache<T>` supporting any cache type with compression and versioning
3. **Search Parameter Cache**: `RedisSearchParameterCache` specifically for search parameter status
4. **Distributed Locking**: Prevents race conditions during cache updates across instances
5. **Graceful Degradation**: System works without Redis when disabled
6. **Service Registration**: Factory pattern conditionally enables Redis based on configuration
7. **Health Checks**: Built-in Redis connectivity monitoring
8. **Data Compression**: Optional GZip compression to reduce memory usage

### Current Architecture
The implementation uses **conditional service registration** with a factory pattern:

```csharp
// SearchModule conditionally registers Redis cache
services.AddSingleton<ISearchParameterCache>(provider =>
{
    if (Redis.Enabled) 
        return new RedisSearchParameterCache(...);
    else 
        return null; // No distributed caching
});
```

This approach provides:
- **Clean Separation**: Redis functionality only loaded when enabled
- **Zero Overhead**: No Redis dependencies when disabled
- **Easier Testing**: Each implementation tested independently

## Consequences

## Implementation Results

### ✅ Achieved Benefits:
- **Reduced Database Load**: Redis cache reduces search parameter polling queries
- **Improved Consistency**: Near real-time synchronization of search parameter changes via distributed cache
- **Better Performance**: Faster search parameter lookups with Redis L2 cache
- **Enhanced Scalability**: Supports more instances with shared cache state
- **Graceful Degradation**: System continues to work if Redis is unavailable
- **Generic Design**: `RedisDistributedCache<T>` can be extended to other cache types

### ✅ Addressed Challenges:
- **Infrastructure Dependency**: Redis deployment requirement acknowledged in documentation
- **Complexity Management**: Clean separation between Redis and non-Redis code paths
- **Network Calls**: Minimal overhead with compression and local memory caching
- **Memory Usage**: Configurable compression reduces Redis memory consumption
- **Monitoring**: Comprehensive logging and health checks implemented

### ✅ Risk Mitigation:
- **Fallback Strategy**: Null cache when Redis disabled means no distributed caching but full functionality
- **Distributed Locking**: Prevents cache corruption during concurrent updates
- **Comprehensive Logging**: Debug, info, warning, and error logging for operations
- **Health Monitoring**: Built-in Redis connectivity checks
- **Configuration Validation**: Throws clear errors when misconfigured

## Future Enhancements

The current implementation provides a solid foundation for extending distributed caching to other FHIR server components:

1. **Resource Metadata Caching**: Extend pattern to resource definitions and schemas
2. **Search Result Caching**: Cache frequently-used search results
3. **Configuration Data Caching**: Share configuration updates across instances  
4. **User Session Caching**: Distribute user authentication/authorization data

## Usage

To enable Redis distributed caching:

1. **Deploy Redis Infrastructure** (Azure Cache for Redis or local Redis)
2. **Configure Connection String** in appsettings.json
3. **Enable Feature** by setting `FhirServer:Caching:Redis:Enabled: true`
4. **Restart FHIR Server instances** - cache will auto-populate from database

The system automatically handles cache warming, consistency, and fallback scenarios without additional intervention.
