# Redis Integration in Microsoft FHIR Server

## Overview

The Microsoft FHIR Server implements Redis as a distributed pub/sub notification system to coordinate state changes across multiple server instances in a deployment. This enables real-time synchronization of in-memory caches and configurations when the FHIR server is deployed across multiple instances for high availability and scalability.

## Core Components

### 1. RedisConfiguration

The Redis integration is configured through the `RedisConfiguration` class located in `Microsoft.Health.Fhir.Core.Configs`:

```csharp
public class RedisConfiguration
{
    public const string SectionName = "Redis";
    
    public bool Enabled { get; set; } = false;
    public string ConnectionString { get; set; } = string.Empty;
    public string InstanceName { get; set; } = string.Empty;
    public int SearchParameterNotificationDelayMs { get; set; } = 10000; // Default 10 seconds
    public RedisNotificationChannels NotificationChannels { get; } = new RedisNotificationChannels();
    public RedisConnectionConfiguration Configuration { get; } = new RedisConnectionConfiguration();
}
```

**Configuration Properties:**
- **`Enabled`**: Master switch to enable/disable Redis functionality
- **`ConnectionString`**: Redis server connection string (supports local Redis, Azure Redis Cache, etc.)
- **`InstanceName`**: Unique identifier for the FHIR server instance
- **`SearchParameterNotificationDelayMs`**: Debounce delay for search parameter change notifications
- **`NotificationChannels`**: Defines Redis pub/sub channels for different notification types
- **`Configuration`**: Additional connection settings (timeouts, retries, etc.)

### 2. Notification Channels

Redis uses predefined channels for different types of notifications:

```csharp
public class RedisNotificationChannels
{
    public string SearchParameterUpdates { get; set; } = "fhir:notifications:searchparameters";
    public string ResourceUpdates { get; set; } = "fhir:notifications:resources";
}
```

This channel-based approach allows:
- Segregation of different notification types
- Selective subscription to relevant channels
- Easy addition of new notification types

### 3. RedisConnectionConfiguration

Fine-grained connection settings for Redis:

```csharp
public class RedisConnectionConfiguration
{
    public bool AbortOnConnectFail { get; set; } = false;
    public int ConnectRetry { get; set; } = 3;
    public int ConnectTimeout { get; set; } = 5000;
    public int SyncTimeout { get; set; } = 5000;
    public int AsyncTimeout { get; set; } = 5000;
}
```

## Service Interfaces

### INotificationService

The core interface for Redis pub/sub operations:

```csharp
public delegate Task NotificationHandler<T>(T message, CancellationToken cancellationToken = default) where T : class;

public interface INotificationService
{
    Task PublishAsync<T>(string channel, T message, CancellationToken cancellationToken = default) where T : class;
    Task SubscribeAsync<T>(string channel, NotificationHandler<T> handler, CancellationToken cancellationToken = default) where T : class;
    Task UnsubscribeAsync(string channel, CancellationToken cancellationToken = default);
}
```

### IUnifiedNotificationPublisher

A unified interface that can optionally broadcast to Redis:

```csharp
public interface IUnifiedNotificationPublisher
{
    string InstanceId { get; }
    
    Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : class, INotification;
        
    Task PublishAsync<TNotification>(TNotification notification, bool enableRedisNotification, CancellationToken cancellationToken = default)
        where TNotification : class, INotification;
}
```

## Implementation Details

### RedisNotificationService

The concrete implementation of `INotificationService` using StackExchange.Redis:

**Key Features:**
- **Connection Management**: Uses `ConnectionMultiplexer` for efficient connection pooling
- **JSON Serialization**: Messages are serialized as JSON with camelCase naming
- **Error Handling**: Graceful handling of Redis unavailability with detailed logging
- **Resource Management**: Proper disposal of Redis connections

**Initialization:**
```csharp
private async Task InitializeAsync()
{
    if (!_configuration.Enabled || string.IsNullOrEmpty(_configuration.ConnectionString))
    {
        _logger.LogInformation("Redis notifications are disabled or connection string is not configured.");
        return;
    }

    try
    {
        var configurationOptions = ConfigurationOptions.Parse(_configuration.ConnectionString);
        configurationOptions.AbortOnConnectFail = _configuration.Configuration.AbortOnConnectFail;
        configurationOptions.ConnectRetry = _configuration.Configuration.ConnectRetry;
        configurationOptions.ConnectTimeout = _configuration.Configuration.ConnectTimeout;
        configurationOptions.SyncTimeout = _configuration.Configuration.SyncTimeout;
        configurationOptions.AsyncTimeout = _configuration.Configuration.AsyncTimeout;

        _connection = await ConnectionMultiplexer.ConnectAsync(configurationOptions);
        _subscriber = _connection.GetSubscriber();

        _logger.LogInformation("Redis notification service initialized successfully.");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to initialize Redis notification service.");
        throw;
    }
}
```

**Publishing Messages:**
```csharp
public async Task PublishAsync<T>(string channel, T message, CancellationToken cancellationToken = default)
    where T : class
{
    if (!_configuration.Enabled || _subscriber == null)
    {
        _logger.LogDebug("Redis notifications are disabled. Skipping publish to channel: {Channel}", channel);
        return;
    }

    try
    {
        var serializedMessage = JsonSerializer.Serialize(message, _jsonOptions);
        await _subscriber.PublishAsync(RedisChannel.Literal(channel), serializedMessage);
        _logger.LogDebug("Published notification to channel: {Channel}", channel);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to publish notification to channel: {Channel}", channel);
        throw;
    }
}
```

**Subscribing to Messages:**
```csharp
public async Task SubscribeAsync<T>(string channel, NotificationHandler<T> handler, CancellationToken cancellationToken = default)
    where T : class
{
    if (!_configuration.Enabled || _subscriber == null)
    {
        _logger.LogDebug("Redis notifications are disabled. Skipping subscribe to channel: {Channel}", channel);
        return;
    }

    try
    {
        await _subscriber.SubscribeAsync(RedisChannel.Literal(channel), async (redisChannel, message) =>
        {
            try
            {
                var deserializedMessage = JsonSerializer.Deserialize<T>(message, _jsonOptions);
                if (deserializedMessage != null)
                {
                    await handler(deserializedMessage, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling notification from channel: {Channel}", channel);
            }
        });

        _logger.LogInformation("Subscribed to notifications on channel: {Channel}", channel);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to subscribe to channel: {Channel}", channel);
        throw;
    }
}
```

### UnifiedNotificationPublisher

Orchestrates between local MediatR notifications and Redis notifications:

```csharp
public class UnifiedNotificationPublisher : IUnifiedNotificationPublisher
{
    public string InstanceId => Environment.MachineName;
    
    public async Task PublishAsync<TNotification>(TNotification notification, bool enableRedisNotification, CancellationToken cancellationToken = default)
    {
        if (enableRedisNotification && _redisConfiguration.Enabled)
        {
            await PublishToRedis(notification, cancellationToken);
        }
        else
        {
            await _mediator.Publish(notification, cancellationToken);
        }
    }
}
```

## Background Service Integration

### NotificationBackgroundService

Handles Redis subscriptions and processes incoming notifications with sophisticated debouncing logic:

**Key Features:**
- **Debounced Processing**: Configurable delay to batch multiple notifications
- **Queue Management**: Handles overlapping notifications gracefully
- **Error Recovery**: Retry logic for transient failures
- **Resource Management**: Proper cancellation handling

**Service Execution:**
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    if (!_redisConfiguration.Enabled)
    {
        _logger.LogInformation("Redis notifications are disabled. Notification background service will not start.");
        return;
    }

    using var scope = _serviceProvider.CreateScope();
    var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

    // Subscribe to search parameter change notifications
    await notificationService.SubscribeAsync<SearchParameterChangeNotification>(
        _redisConfiguration.NotificationChannels.SearchParameterUpdates,
        HandleSearchParameterChangeNotification,
        stoppingToken);

    await Task.Delay(Timeout.Infinite, stoppingToken);
}
```

**Debouncing Logic:**
```csharp
private async Task ProcessWithDebounceAndQueue(CancellationToken cancellationToken)
{
    do
    {
        _isProcessingQueued = false;

        // Start debounce delay (can be cancelled by new notifications)
        try
        {
            await Task.Delay(_redisConfiguration.SearchParameterNotificationDelayMs, delayToken);
        }
        catch (OperationCanceledException) when (delayToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Delay was cancelled by a new notification, restart delay
            continue;
        }

        // Process the actual update (cannot be cancelled by notifications)
        await ProcessSearchParameterUpdateWithRetry(cancellationToken);
    }
    while (_isProcessingQueued);
}
```

## Extending the Notification System

### Adding New Notification Types

The Redis notification system is designed to be extensible. Here's how to add support for new notification types (e.g., Profile updates):

#### 1. Define the Notification Message Model

Create a new notification message class in `Microsoft.Health.Fhir.Core.Features.Notifications.Models`:

```csharp
public class ProfileChangeNotification
{
    public string InstanceId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public ProfileChangeType ChangeType { get; set; }
    public IReadOnlyCollection<string> AffectedProfileUrls { get; set; }
    public string TriggerSource { get; set; }
}

public enum ProfileChangeType
{
    Created,
    Updated,
    Deleted,
    StatusChanged
}
```

#### 2. Add a New Notification Channel

Update `RedisNotificationChannels` to include the new channel:

```csharp
public class RedisNotificationChannels
{
    public string SearchParameterUpdates { get; set; } = "fhir:notifications:searchparameters";
    public string ResourceUpdates { get; set; } = "fhir:notifications:resources";
    public string ProfileUpdates { get; set; } = "fhir:notifications:profiles";
}
```

#### 3. Extend UnifiedNotificationPublisher

Add conversion logic in `UnifiedNotificationPublisher` to handle the new notification type:

```csharp
private object ConvertToRedisNotification<TNotification>(TNotification notification)
    where TNotification : class, INotification
{
    return notification switch
    {
        SearchParametersUpdatedNotification updatedNotification => new SearchParameterChangeNotification
        {
            InstanceId = InstanceId,
            Timestamp = DateTimeOffset.UtcNow,
            ChangeType = SearchParameterChangeType.StatusChanged,
            AffectedParameterUris = updatedNotification.SearchParameters
                .Select(sp => sp.Url?.ToString())
                .Where(url => !string.IsNullOrEmpty(url))
                .ToList(),
            TriggerSource = "UnifiedNotificationPublisher",
        },
        ProfileUpdatedNotification profileNotification => new ProfileChangeNotification
        {
            InstanceId = InstanceId,
            Timestamp = DateTimeOffset.UtcNow,
            ChangeType = ProfileChangeType.Updated,
            AffectedProfileUrls = profileNotification.ProfileUrls,
            TriggerSource = "UnifiedNotificationPublisher",
        },
        _ => null,
    };
}
```

#### 4. Create a MediatR Notification

Define the local MediatR notification that triggers Redis broadcasting:

```csharp
public class ProfileUpdatedNotification : INotification
{
    public ProfileUpdatedNotification(IReadOnlyCollection<string> profileUrls)
    {
        ProfileUrls = profileUrls ?? throw new ArgumentNullException(nameof(profileUrls));
    }

    public IReadOnlyCollection<string> ProfileUrls { get; }
}
```

#### 5. Publish Notifications in Your Service

In the service that manages profiles (e.g., `ProfileManager`), publish notifications:

```csharp
public class ProfileManager
{
    private readonly IUnifiedNotificationPublisher _notificationPublisher;
    
    public async Task UpdateProfileAsync(string profileUrl, StructureDefinition profile, CancellationToken cancellationToken)
    {
        // Update profile logic here
        // ...
        
        // Notify other instances via Redis
        await _notificationPublisher.PublishAsync(
            new ProfileUpdatedNotification(new[] { profileUrl }), 
            true, // Enable Redis notification
            cancellationToken);
    }
}
```

#### 6. Subscribe to Notifications

Extend `NotificationBackgroundService` to subscribe to the new channel:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    if (!_redisConfiguration.Enabled) return;

    using var scope = _serviceProvider.CreateScope();
    var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

    // Subscribe to existing channels
    await notificationService.SubscribeAsync<SearchParameterChangeNotification>(
        _redisConfiguration.NotificationChannels.SearchParameterUpdates,
        HandleSearchParameterChangeNotification,
        stoppingToken);

    // Subscribe to new profile change notifications
    await notificationService.SubscribeAsync<ProfileChangeNotification>(
        _redisConfiguration.NotificationChannels.ProfileUpdates,
        HandleProfileChangeNotification,
        stoppingToken);

    await Task.Delay(Timeout.Infinite, stoppingToken);
}

private async Task HandleProfileChangeNotification(
    ProfileChangeNotification notification,
    CancellationToken cancellationToken)
{
    _logger.LogInformation(
        "Received profile change notification from instance {InstanceId} at {Timestamp}. ChangeType: {ChangeType}",
        notification.InstanceId,
        notification.Timestamp,
        notification.ChangeType);

    // Process the profile changes
    using var scope = _serviceProvider.CreateScope();
    var profileManager = scope.ServiceProvider.GetRequiredService<IProfileManager>();
    
    await profileManager.RefreshProfilesAsync(notification.AffectedProfileUrls, cancellationToken);
}
```

#### 7. Configuration Updates

Update configuration files to include the new channel:

```json
{
  "Redis": {
    "Enabled": true,
    "ConnectionString": "localhost:6379",
    "NotificationChannels": {
      "SearchParameterUpdates": "fhir:notifications:searchparameters",
      "ProfileUpdates": "fhir:notifications:profiles"
    }
  }
}
```

#### 8. Add Debouncing Configuration (Optional)

If the new notification type benefits from debouncing, add configuration:

```csharp
public class RedisConfiguration
{
    // Existing properties...
    public int ProfileNotificationDelayMs { get; set; } = 5000; // Default 5 seconds
}
```

### Best Practices for New Notification Types

1. **Message Design**:
   - Keep messages lightweight and focused
   - Include essential context (InstanceId, Timestamp, ChangeType)
   - Use collections for batch operations

2. **Channel Naming**:
   - Use descriptive, hierarchical names
   - Follow the pattern: `fhir:notifications:{domain}`
   - Consider future extensibility

3. **Error Handling**:
   - Implement graceful degradation when Redis is unavailable
   - Log errors without breaking core functionality
   - Provide fallback mechanisms

4. **Performance**:
   - Consider debouncing for high-frequency changes
   - Batch related notifications when possible
   - Monitor message size and frequency

5. **Loop Prevention**:
   - Use instance ID checking to prevent self-processing
   - Implement `isFromRemoteSync` patterns when necessary
   - Consider message deduplication for critical scenarios

6. **Testing**:
   - Write unit tests for notification publishing and handling
   - Test Redis unavailability scenarios
   - Verify cross-instance synchronization behavior

## Configuration Examples

### Basic Configuration

```json
{
  "Redis": {
    "Enabled": true,
    "ConnectionString": "localhost:6379",
    "InstanceName": "fhir-server-01",
    "SearchParameterNotificationDelayMs": 10000
  }
}
```

### Azure Redis Configuration

```json
{
  "Redis": {
    "Enabled": true,
    "ConnectionString": "your-azure-redis.redis.cache.windows.net:6380,password=your-password,ssl=True,abortConnect=False",
    "InstanceName": "fhir-server-azure-01",
    "Configuration": {
      "ConnectTimeout": 10000,
      "SyncTimeout": 10000,
      "AsyncTimeout": 10000
    }
  }
}
```

### Development/Testing Configuration

```json
{
  "Redis": {
    "Enabled": false
  }
}
```

## Error Handling and Resilience

### Connection Failures
- Redis connection failures are logged but don't break core functionality
- Automatic fallback to local processing when Redis is unavailable
- Graceful handling during Redis server maintenance

### Message Handling Errors
- Individual message processing errors are logged and isolated
- Malformed messages are skipped without affecting other notifications
- Subscription failures trigger reconnection attempts

### Resource Management
- Proper disposal of Redis connections and resources
- Cancellation token support for graceful shutdown
- Memory leak prevention through resource cleanup

## Monitoring and Diagnostics

### Logging Levels
- **Information**: Connection status, subscription events
- **Debug**: Message publishing/receiving details
- **Error**: Connection failures, message handling errors

### Key Metrics to Monitor
- Redis connection health and availability
- Message publish/subscribe success rates
- Notification processing latency
- Background service health

## Best Practices

### Configuration
- Always use secure connections in production (TLS/SSL)
- Set appropriate timeout values based on network latency
- Configure reasonable debounce delays

### Error Handling
- Implement proper fallback mechanisms
- Log Redis-related errors without breaking functionality
- Handle Redis unavailability gracefully

### Performance
- Monitor Redis memory usage and performance
- Use connection pooling efficiently
- Consider Redis clustering for high availability

### Security
- Use Redis AUTH for authentication
- Restrict network access to authorized instances
- Encrypt sensitive data in Redis messages

This Redis implementation provides a robust foundation for distributed state synchronization across multiple FHIR server instances while maintaining high availability through intelligent fallback mechanisms.
