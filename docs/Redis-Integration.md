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

The `NotificationBackgroundService` is a hosted background service that subscribes to Redis notifications and coordinates cross-instance updates. It implements intelligent processing strategies including instance isolation and flexible debouncing.

#### Service Architecture

```csharp
public class NotificationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly RedisConfiguration _redisConfiguration;
    private readonly ILogger<NotificationBackgroundService> _logger;
    
    // Debouncing infrastructure
    private readonly SemaphoreSlim _processingGate = new SemaphoreSlim(1, 1);
    private volatile bool _isProcessingQueued = false;
    private CancellationTokenSource _currentDelayTokenSource;
    private readonly object _delayLock = new object();
}
```

#### Redis Integration Features

**1. Instance ID Validation**

Prevents self-processing of notifications:

```csharp
private async Task HandleSearchParameterChangeNotification(
    SearchParameterChangeNotification notification,
    CancellationToken cancellationToken)
{
    // Get current instance ID for validation
    using var scope = _serviceProvider.CreateScope();
    var unifiedPublisher = scope.ServiceProvider.GetRequiredService<IUnifiedNotificationPublisher>();
    var currentInstanceId = unifiedPublisher.InstanceId;

    // Skip processing notifications from the same instance
    if (string.Equals(notification.InstanceId, currentInstanceId, StringComparison.OrdinalIgnoreCase))
    {
        _logger.LogDebug("Skipping search parameter change notification from same instance {InstanceId}", 
            notification.InstanceId);
        return;
    }

    // Process notification with debouncing...
}
```

**2. Flexible Debouncing Framework**

Uses the `DebounceConfig` framework for intelligent notification batching:

```csharp
// Configure processing strategy
var debounceConfig = new DebounceConfig
{
    DelayMs = _redisConfiguration.SearchParameterNotificationDelayMs,
    ProcessingAction = ProcessSearchParameterUpdate,
    ProcessingName = "search parameter updates"
};

// Process with optional debouncing based on configuration
await ProcessWithOptionalDebouncing(debounceConfig, cancellationToken);
```

**3. Service Lifecycle Management**

Automatically subscribes to Redis channels when enabled:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    if (!_redisConfiguration.Enabled)
    {
        _logger.LogInformation("Redis notifications are disabled. Notification background service will not start.");
        return;
    }

    _logger.LogInformation("Starting notification background service.");

    using var scope = _serviceProvider.CreateScope();
    var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

    // Subscribe to search parameter change notifications
    await notificationService.SubscribeAsync<SearchParameterChangeNotification>(
        _redisConfiguration.NotificationChannels.SearchParameterUpdates,
        HandleSearchParameterChangeNotification,
        stoppingToken);

    // Keep service running
    await Task.Delay(Timeout.Infinite, stoppingToken);
}
```

### Performance and Reliability Features

**Instance Isolation:**
- Prevents unnecessary self-processing
- Reduces Redis traffic and database load
- Improves overall system efficiency

**Intelligent Debouncing:**
- Configurable delay timing (0 = immediate, >0 = debounced)
- Efficient queueing with boolean flags
- Dynamic delay cancellation for rapid updates

**Graceful Degradation:**
- Continues operating when Redis is unavailable
- Comprehensive error handling without system disruption
- Proper resource cleanup on service shutdown

**Memory Efficiency:**
- Minimal memory footprint with boolean queuing
- Single semaphore coordination
- Efficient cancellation token management

## Extending the Notification System

### Adding New Notification Types

The Redis notification system is designed to be extensible. The flexible debouncing framework allows new notification types to choose their processing strategy.

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

#### 3. Subscribe to Notifications in NotificationBackgroundService

Extend `NotificationBackgroundService.ExecuteAsync` to subscribe to the new channel:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    // ... existing code ...

    // Subscribe to new profile change notifications
    await notificationService.SubscribeAsync<ProfileChangeNotification>(
        _redisConfiguration.NotificationChannels.ProfileUpdates,
        HandleProfileChangeNotification,
        stoppingToken);

    await Task.Delay(Timeout.Infinite, stoppingToken);
}
```

#### 4. Create Handler Method with Flexible Debouncing

Add a handler method that uses the debouncing framework:

```csharp
private async Task HandleProfileChangeNotification(
    ProfileChangeNotification notification,
    CancellationToken cancellationToken)
{
    _logger.LogInformation(
        "Received profile change notification from instance {InstanceId} at {Timestamp}. ChangeType: {ChangeType}",
        notification.InstanceId,
        notification.Timestamp,
        notification.ChangeType);

    // Choose appropriate processing strategy:
    
    // Option 1: Use debouncing (for high-frequency changes)
    var debounceConfig = new DebounceConfig
    {
        DelayMs = 5000, // 5 second delay
        ProcessingAction = (ct) => ProcessProfileChangeUpdate(notification, ct),
        ProcessingName = "profile updates"
    };

    // Option 2: Process immediately (for critical changes)
    // DelayMs = 0 for immediate processing
    
    // Option 3: Use custom delay based on change type
    // DelayMs = notification.ChangeType == ProfileChangeType.Deleted ? 0 : 3000

    await ProcessWithOptionalDebouncing(debounceConfig, cancellationToken);
}
```

#### 5. Create Processing Method

Implement the actual processing logic:

```csharp
private async Task ProcessProfileChangeUpdate(ProfileChangeNotification notification, CancellationToken cancellationToken)
{
    using var scope = _serviceProvider.CreateScope();
    var profileManager = scope.ServiceProvider.GetRequiredService<IProfileManager>();
    
    await profileManager.RefreshProfilesAsync(notification.AffectedProfileUrls, cancellationToken);
    
    _logger.LogInformation("Successfully processed profile change notification");
}
```

#### 6. Extend UnifiedNotificationPublisher

Add conversion logic to handle the new notification type:

```csharp
private object ConvertToRedisNotification<TNotification>(TNotification notification)
    where TNotification : class, INotification
{
    return notification switch
    {
        SearchParametersUpdatedNotification updatedNotification => new SearchParameterChangeNotification
        {
            // ... existing mapping ...
        },
        ProfileUpdatedNotification profileNotification => new ProfileChangeNotification
        {
            InstanceId = InstanceId,
            Timestamp = DateTimeOffset.UtcNow,
            ChangeType = ProfileChangeType.Updated,
            AffectedProfileUrls = profileNotification.ProfileUrls,
            TriggerSource = "UnifiedNotificationPublisher"
        },
        _ => null,
    };
}
```

#### 7. Publish Notifications from Your Service

This is the crucial step - actually publishing notifications from your business logic. Here's how to integrate notification publishing into your service:

**Inject the Publisher:**
```csharp
public class ProfileManager
{
    private readonly IUnifiedNotificationPublisher _notificationPublisher;
    private readonly IProfileRepository _profileRepository;
    private readonly ILogger<ProfileManager> _logger;
    
    public ProfileManager(
        IUnifiedNotificationPublisher notificationPublisher,
        IProfileRepository profileRepository,
        ILogger<ProfileManager> logger)
    {
        _notificationPublisher = notificationPublisher;
        _profileRepository = profileRepository;
        _logger = logger;
    }
    
    // ... service methods ...
}
```

**Publish Notifications on State Changes:**

Option 1 - **Always Enable Redis** (for critical cross-instance synchronization):
```csharp
public async Task UpdateProfileAsync(string profileUrl, StructureDefinition profile, CancellationToken cancellationToken)
{
    try
    {
        // Update profile in database
        await _profileRepository.UpdateAsync(profileUrl, profile, cancellationToken);
        
        _logger.LogInformation("Updated profile {ProfileUrl}", profileUrl);
        
        // Notify other instances via Redis (always enabled)
        await _notificationPublisher.PublishAsync(
            new ProfileUpdatedNotification(new[] { profileUrl }), 
            enableRedisNotification: true, // Always broadcast to other instances
            cancellationToken);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to update profile {ProfileUrl}", profileUrl);
        throw;
    }
}
```

Option 2 - **Conditional Redis** (based on operation type):
```csharp
public async Task DeleteProfileAsync(string profileUrl, CancellationToken cancellationToken)
{
    try
    {
        await _profileRepository.DeleteAsync(profileUrl, cancellationToken);
        
        _logger.LogInformation("Deleted profile {ProfileUrl}", profileUrl);
        
        // Critical operations always use Redis, minor updates may not
        bool useRedis = true; // Deletions are critical
        
        await _notificationPublisher.PublishAsync(
            new ProfileUpdatedNotification(new[] { profileUrl }) 
            { 
                ChangeType = ProfileChangeType.Deleted 
            },
            enableRedisNotification: useRedis,
            cancellationToken);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to delete profile {ProfileUrl}", profileUrl);
        throw;
    }
}
```

Option 3 - **Batch Operations** (for multiple changes):
```csharp
public async Task UpdateMultipleProfilesAsync(
    IReadOnlyCollection<(string Url, StructureDefinition Profile)> profiles, 
    CancellationToken cancellationToken)
{
    var updatedUrls = new List<string>();
    
    try
    {
        foreach (var (url, profile) in profiles)
        {
            await _profileRepository.UpdateAsync(url, profile, cancellationToken);
            updatedUrls.Add(url);
        }
        
        _logger.LogInformation("Updated {Count} profiles", profiles.Count);
        
        // Single notification for batch operations
        await _notificationPublisher.PublishAsync(
            new ProfileUpdatedNotification(updatedUrls), 
            enableRedisNotification: true,
            cancellationToken);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to update profiles. Successfully updated: {UpdatedCount}/{TotalCount}", 
            updatedUrls.Count, profiles.Count);
        throw;
    }
}
```

#### 8. Configuration for New Notification Channel

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

#### 9. Add Debouncing Configuration (Optional)

If the new notification type benefits from debouncing, add configuration to `RedisConfiguration`:

```csharp
public class RedisConfiguration
{
    // Existing properties...
    public int ProfileNotificationDelayMs { get; set; } = 5000; // Default 5 seconds
}
```

### Processing Strategy Examples

The flexible debouncing framework supports multiple processing strategies:

#### Immediate Processing (Critical Updates)
```csharp
var immediateConfig = new DebounceConfig
{
    DelayMs = 0, // Process immediately
    ProcessingAction = (ct) => ProcessCriticalUpdate(notification, ct),
    ProcessingName = "critical update"
};
```

#### Custom Debouncing (Different Delays)
```csharp
var customConfig = new DebounceConfig
{
    DelayMs = 3000, // 3 seconds
    ProcessingAction = (ct) => ProcessCustomUpdate(notification, ct),
    ProcessingName = "custom update"
};
```

#### Conditional Processing (Based on Notification Content)
```csharp
var conditionalDelay = notification.Priority == "High" ? 0 : 5000;
var conditionalConfig = new DebounceConfig
{
    DelayMs = conditionalDelay,
    ProcessingAction = (ct) => ProcessConditionalUpdate(notification, ct),
    ProcessingName = "conditional update"
};
```

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
- **Information**: Connection status, subscription events, processing activities
- **Debug**: Message publishing/receiving details, debouncing events
- **Error**: Connection failures, message handling errors

### Key Metrics to Monitor
- Redis connection health and availability
- Message publish/subscribe success rates
- Notification processing latency and debouncing effectiveness
- Background service health and semaphore usage

## Best Practices

### Configuration
- Always use secure connections in production (TLS/SSL)
- Set appropriate timeout values based on network latency
- Configure reasonable debounce delays based on notification frequency

### Error Handling
- Implement proper fallback mechanisms
- Log Redis-related errors without breaking functionality
- Handle Redis unavailability gracefully

### Performance
- Monitor Redis memory usage and performance
- Use connection pooling efficiently
- Consider Redis clustering for high availability
- Choose appropriate debouncing strategies for each notification type

### Security
- Use Redis AUTH for authentication
- Restrict network access to authorized instances
- Encrypt sensitive data in Redis messages

### Debouncing Strategy Selection
- **Immediate processing** (DelayMs = 0): For critical updates that must be processed immediately
- **Short debouncing** (DelayMs = 1-5 seconds): For moderate-frequency updates
- **Long debouncing** (DelayMs = 10+ seconds): For high-frequency updates that can be batched
- **Conditional debouncing**: Based on notification content, priority, or type

### Publishing Best Practices
- **Always enable Redis for critical state changes** that need cross-instance synchronization
- **Use batch operations** when updating multiple related items
- **Choose appropriate timing** for when to publish (after successful database operations)
- **Handle publisher errors gracefully** - the `UnifiedNotificationPublisher` provides automatic fallback
- **Consider the business impact** of each notification type when deciding on Redis vs local publishing

This Redis implementation provides a robust and flexible foundation for distributed state synchronization across multiple FHIR server instances, with intelligent debouncing strategies that can be tailored to each notification type's specific requirements.
