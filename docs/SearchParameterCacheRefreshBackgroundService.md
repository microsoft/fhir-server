# SearchParameter Cache Refresh Background Service

The SearchParameter Cache Refresh Background Service ensures that SearchParameter definitions remain synchronized across all FHIR server instances by periodically checking cache freshness and delegating to `SearchParameterOperations.GetAndApplySearchParameterUpdates()` for full synchronization when needed.

## Overview

This background service addresses the challenge of keeping SearchParameter cache fresh in multi-instance deployments where one instance might add, update, or delete SearchParameters while others are unaware of these changes.

## How It Works

1. **Initialization Wait**: The service waits for `SearchParametersInitializedNotification` before starting the refresh loop
2. **Periodic Check**: Every configured interval (default: 60 seconds), the service calls `ISearchParameterStatusManager.EnsureCacheFreshnessAsync()`
3. **Efficient Database Check**: `EnsureCacheFreshnessAsync()` efficiently checks the database's max `LastUpdated` timestamp using optimized stored procedures and **returns a boolean**
4. **Conditional Full Sync**: If cache is stale (returns `true`), the service calls `ISearchParameterOperations.GetAndApplySearchParameterUpdates()` for comprehensive SearchParameter lifecycle management
5. **Error Resilience**: Continues running even if individual refresh attempts fail

## Database Integration

### Stored Procedure Usage
The cache freshness check uses the `GetSearchParamMaxLastUpdated` stored procedure (available in schema version V96+) for optimal performance:

- **Schema V96+**: Uses the dedicated stored procedure with error handling and EventLog logging
- **Earlier Versions**: Falls back to direct SQL query for backward compatibility
- **Pre-Status Versions**: Delegates to file-based store for schema versions before V6

### Schema Migration
The feature requires:
- **Minimum Schema Version**: V6 (for SearchParam status columns)
- **Optimal Schema Version**: V96 (for dedicated stored procedure)
- **Migration Files**: 96.diff.sql adds the `GetSearchParamMaxLastUpdated` stored procedure

## Configuration

Configure the refresh interval in your application settings:

### appsettings.json
```json
{
  "Core": {
    "SearchParameterCacheRefreshIntervalSeconds": 60
  }
}
```

### Environment Variables
```
Core__SearchParameterCacheRefreshIntervalSeconds=60
```

### Default Behavior
- **Default Interval**: 60 seconds
- **Minimum Interval**: Any positive integer
- **Invalid Values**: 0 or negative values default to 1 second

## Registration

The service is automatically registered in the DI container via `SearchModule`:

```csharp
services
    .Add<SearchParameterCacheRefreshBackgroundService>()
    .Singleton()
    .AsSelf()
    .AsService<IHostedService>()
    .AsService<INotificationHandler<SearchParametersInitializedNotification>>();
```

## Performance Characteristics

- **Low Overhead**: Uses optimized stored procedure for max timestamp query (V96+)
- **No API Impact**: Runs in background, doesn't affect request processing
- **Conditional Updates**: Only performs full sync when actual changes are detected
- **Configurable**: Interval can be tuned based on environment needs
- **Database Logging**: All stored procedure calls are logged to EventLog for monitoring

## Use Cases

- **Multi-Instance Deployments**: Keeps SearchParameters synchronized across instances
- **Custom SearchParameter Management**: Ensures custom SearchParameters are available across all instances
- **High-Availability Scenarios**: Maintains consistency without manual intervention
- **Schema Migration**: Seamlessly handles upgrades from direct queries to stored procedures

## Monitoring

The service logs at different levels:
- **Information**: Service lifecycle events and full sync operations
- **Debug**: Cache check details and SearchParameter operation progress
- **Error**: Exceptions during refresh attempts
- **Warning**: Retryable errors and resource retrieval issues

Additional monitoring through SQL EventLog:
- **Stored Procedure Execution**: All calls to `GetSearchParamMaxLastUpdated` are logged
- **Performance Metrics**: Execution time and result logging
- **Error Tracking**: Automatic error logging with stack traces

## Alternative Approaches Considered

1. **Request-Level Caching**: Rejected due to performance impact on every API call
2. **Event-Based Updates**: Would require additional infrastructure complexity
3. **Manual Refresh**: Not suitable for automated environments
4. **Direct SQL Queries**: Replaced with stored procedures for better performance and monitoring

## Technical Implementation

### Two-Phase Architecture
The service uses a two-phase approach for optimal performance:

```csharp
// Phase 1: Efficient staleness check (returns boolean)
bool cacheIsStale = await _searchParameterStatusManager.EnsureCacheFreshnessAsync(cancellationToken);

// Phase 2: Full synchronization only if needed
if (cacheIsStale)
{
    await _searchParameterOperations.GetAndApplySearchParameterUpdates(cancellationToken);
}
```

### Schema Version Handling
```csharp
// V96+ uses optimized stored procedure
if (_schemaInformation.Current >= SchemaVersionConstants.SearchParameterMaxLastUpdatedStoredProcedure)
{
    VLatest.GetSearchParamMaxLastUpdated.PopulateCommand(sqlCommandWrapper);
}
// Earlier versions use direct query
else
{
    sqlCommandWrapper.CommandText = "SELECT MAX(LastUpdated) FROM dbo.SearchParam WHERE LastUpdated IS NOT NULL";
}
```

### Error Handling
The stored procedure includes comprehensive error handling:
- BEGIN TRY/CATCH blocks
- EventLog logging for all operations
- Graceful handling of empty result sets
- Transaction management

## Related Components

- `ISearchParameterStatusManager.EnsureCacheFreshnessAsync()`: Returns boolean indicating cache staleness
- `ISearchParameterOperations.GetAndApplySearchParameterUpdates()`: Performs complete SearchParameter synchronization
- `ISearchParameterStatusDataStore.GetMaxLastUpdatedAsync()`: Efficient database query
- `GetSearchParamMaxLastUpdated`: Stored procedure (V96+)
- `SearchParameterStatusManager`: Manages SearchParameter status and caching
- `SearchParameterOperations`: Handles full SearchParameter resource lifecycle
- `CoreFeatureConfiguration`: Contains configuration settings
- Schema Version V96: Introduces the optimized stored procedure

## Flow Diagram

```mermaid
graph TD
    A[Timer Trigger] --> B[EnsureCacheFreshnessAsync]
    B --> C{Cache Stale?}
    C -->|false| D[Log: Cache Up-to-Date]
    C -->|true| E[GetAndApplySearchParameterUpdates]
    E --> F[Process Deletions]
    E --> G[Process Additions/Updates]
    E --> H[Fetch SearchParameter Resources]
    E --> I[Apply Status Updates]
    D --> J[Wait for Next Timer]
    I --> J
