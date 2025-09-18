# Optional Debouncing and Queueing for Notification Handlers

The `NotificationBackgroundService` provides a flexible approach for notification handlers to optionally use debouncing and queueing functionality. This allows different notification types to have different processing strategies based on their needs.

## How It Works

### Core Components

1. **`ProcessingAction` delegate**: Defines the work to be done after debouncing
2. **`DebounceConfig` class**: Configures debouncing behavior, delay, and processing action
3. **`ProcessWithOptionalDebouncing` method**: The main entry point that handles debouncing, queueing, and processing

### Current Implementation

#### **Standard Debouncing** (Search Parameters)
The current implementation uses debouncing for search parameter notifications:

```csharp
var debounceConfig = new DebounceConfig
{
    DelayMs = _redisConfiguration.SearchParameterNotificationDelayMs, // Default: 10 seconds
    ProcessingAction = ProcessSearchParameterUpdate,
    ProcessingName = "search parameter updates"
};

await ProcessWithOptionalDebouncing(debounceConfig, cancellationToken);
```

## Benefits

1. **Flexibility**: Each notification type can choose its own processing strategy
2. **Reusability**: Common debouncing logic is shared across all notification types
3. **Consistency**: All notifications get the same error handling, logging, and cancellation support
4. **Performance**: Reduces redundant processing through intelligent queueing and debouncing
5. **Type Safety**: `DebounceConfig` validation ensures all required properties are set

## Adding New Notification Types

To extend the system with new notification types:

### 1. Define Your Notification Class

```csharp
public class CustomNotification
{
    public string InstanceId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Data { get; set; }
}
```

### 2. Add Subscription in ExecuteAsync

In `NotificationBackgroundService.ExecuteAsync`, add a new subscription:

```csharp
// Subscribe to your custom notification
await notificationService.SubscribeAsync<CustomNotification>(
    "custom-notification-channel",
    HandleCustomNotification,
    stoppingToken);
```

### 3. Create Handler Method

Add a handler method that uses the debouncing framework:

```csharp
private async Task HandleCustomNotification(
    CustomNotification notification, 
    CancellationToken cancellationToken)
{
    _logger.LogInformation("Received custom notification from instance {InstanceId}", notification.InstanceId);

    var debounceConfig = new DebounceConfig
    {
        DelayMs = 5000, // Choose appropriate delay based on requirements
        ProcessingAction = (ct) => ProcessCustomNotification(notification, ct),
        ProcessingName = "custom notification processing"
    };

    await ProcessWithOptionalDebouncing(debounceConfig, cancellationToken);
}
```

### 4. Create Processing Method

Implement the actual processing logic:

```csharp
private async Task ProcessCustomNotification(CustomNotification notification, CancellationToken cancellationToken)
{
    using var scope = _serviceProvider.CreateScope();
    var customService = scope.ServiceProvider.GetRequiredService<ICustomService>();
    
    await customService.ProcessAsync(notification, cancellationToken);
    
    _logger.LogInformation("Successfully processed custom notification");
}
```

## Important Implementation Notes

- **`DebounceConfig` cannot be null**: The configuration object is always required as it contains the processing action and name
- **"Optional" refers to debouncing**: The debouncing behavior is optional based on the `DelayMs` value (0 = immediate, >0 = debounced)
- **Validation**: The `DebounceConfig.Validate()` method ensures all required properties are properly set
- **Thread Safety**: The semaphore and queueing logic is shared across all notification types for consistency
- **Resource Management**: Proper disposal of cancellation tokens and semaphores

## 4. Real-World Implementation: NotificationBackgroundService

The `NotificationBackgroundService` demonstrates the practical implementation of the DebounceConfig framework with Redis notifications:

### Service Architecture

```csharp
public class NotificationBackgroundService : BackgroundService
{
    private readonly SemaphoreSlim _processingGate = new SemaphoreSlim(1, 1);
    private volatile bool _isProcessingQueued = false;
    private CancellationTokenSource _currentDelayTokenSource;
    private readonly object _delayLock = new object();
    
    // Redis configuration for debounce timing
    private readonly RedisConfiguration _redisConfiguration;
}
```

### Instance ID Validation and Notification Processing

The service includes intelligent self-processing prevention:

```csharp
private async Task HandleSearchParameterChangeNotification(
    SearchParameterChangeNotification notification,
    CancellationToken cancellationToken)
{
    // Log notification receipt
    _logger.LogInformation(
        "Received search parameter change notification from instance {InstanceId} at {Timestamp}. ChangeType: {ChangeType}",
        notification.InstanceId, notification.Timestamp, notification.ChangeType);

    // Instance ID validation to prevent self-processing
    using var scope = _serviceProvider.CreateScope();
    var unifiedPublisher = scope.ServiceProvider.GetRequiredService<IUnifiedNotificationPublisher>();
    var currentInstanceId = unifiedPublisher.InstanceId;

    if (string.Equals(notification.InstanceId, currentInstanceId, StringComparison.OrdinalIgnoreCase))
    {
        _logger.LogDebug("Skipping search parameter change notification from same instance {InstanceId}", 
            notification.InstanceId);
        return;
    }

    // Configure debouncing using DebounceConfig framework
    var debounceConfig = new DebounceConfig
    {
        DelayMs = _redisConfiguration.SearchParameterNotificationDelayMs,
        ProcessingAction = ProcessSearchParameterUpdate,
        ProcessingName = "search parameter updates"
    };

    // Process with intelligent debouncing
    await ProcessWithOptionalDebouncing(debounceConfig, cancellationToken);
}
```

### Flexible Processing Strategy

The `ProcessWithOptionalDebouncing` method adapts behavior based on configuration:

```csharp
public async Task ProcessWithOptionalDebouncing(DebounceConfig debounceConfig, CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(debounceConfig);
    debounceConfig.Validate();

    // Immediate processing for critical operations (DelayMs = 0)
    if (debounceConfig.DelayMs <= 0)
    {
        _logger.LogDebug("Processing {ProcessingName} immediately (no debouncing)", debounceConfig.ProcessingName);
        await ProcessWithRetry(debounceConfig, cancellationToken);
        return;
    }

    // Debounced processing with semaphore-based queueing
    if (!await _processingGate.WaitAsync(0, cancellationToken))
    {
        _logger.LogInformation("{ProcessingName} is currently processing. Queueing new notification.", 
            debounceConfig.ProcessingName);
        
        // Efficient queueing using boolean flag
        _isProcessingQueued = true;
        
        // Cancel current delay and restart with new timing
        lock (_delayLock)
        {
            _ = _currentDelayTokenSource?.CancelAsync();
            _currentDelayTokenSource?.Dispose();
            _currentDelayTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }
        return;
    }

    try
    {
        await ProcessWithDebounceAndQueue(debounceConfig, cancellationToken);
    }
    finally
    {
        _processingGate.Release();
    }
}
```

### Intelligent Delay Management

The debouncing loop handles dynamic delay cancellation:

```csharp
private async Task ProcessWithDebounceAndQueue(DebounceConfig debounceConfig, CancellationToken cancellationToken)
{
    do
    {
        _isProcessingQueued = false;

        // Set up cancellable delay
        lock (_delayLock)
        {
            _currentDelayTokenSource?.Dispose();
            _currentDelayTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        var delayToken = _currentDelayTokenSource.Token;

        try
        {
            _logger.LogDebug("Starting debounce delay of {DelayMs}ms for {ProcessingName}", 
                debounceConfig.DelayMs, debounceConfig.ProcessingName);
            await Task.Delay(debounceConfig.DelayMs, delayToken);
        }
        catch (OperationCanceledException) when (delayToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Delay was cancelled by newer notification, restart delay
            _logger.LogDebug("Debounce delay for {ProcessingName} was cancelled by newer notification, restarting delay", 
                debounceConfig.ProcessingName);
            continue;
        }

        // Process the actual update
        await ProcessWithRetry(debounceConfig, cancellationToken);
    }
    while (_isProcessingQueued); // Continue if more notifications queued during processing
}
```

### Error Handling and Resilience

Comprehensive error handling preserves system stability:

```csharp
private async Task ProcessWithRetry(DebounceConfig debounceConfig, CancellationToken cancellationToken)
{
    try
    {
        _logger.LogInformation("Processing {ProcessingName} after {DelayMs}ms delay", 
            debounceConfig.ProcessingName, debounceConfig.DelayMs);

        await debounceConfig.ProcessingAction(cancellationToken);
        _logger.LogInformation("Successfully processed {ProcessingName}", debounceConfig.ProcessingName);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        _logger.LogDebug("{ProcessingName} processing was cancelled due to service shutdown", 
            debounceConfig.ProcessingName);
        throw; // Re-throw for proper service shutdown handling
    }
    catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        _logger.LogDebug("{ProcessingName} processing was cancelled due to service shutdown", 
            debounceConfig.ProcessingName);
        throw; // Re-throw for proper service shutdown handling
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to process {ProcessingName}", debounceConfig.ProcessingName);
        // Log error but don't throw - preserves system stability
    }
}
```

### Resource Management and Cleanup

Proper disposal prevents resource leaks:

```csharp
public override void Dispose()
{
    lock (_delayLock)
    {
        _ = _currentDelayTokenSource?.CancelAsync();
        _currentDelayTokenSource?.Dispose();
    }
    
    _processingGate?.Dispose();
    base.Dispose();
    GC.SuppressFinalize(this);
}
```

### Configuration-Driven Behavior

The service adapts its behavior based on Redis configuration:

```json
{
  "Redis": {
    "SearchParameterNotificationDelayMs": 10000,  // 10 second debounce
    "SearchParameterNotificationDelayMs": 0       // Immediate processing
  }
}
```

### Performance Characteristics

**Memory Efficiency:**
- Boolean flags instead of notification queues
- Single semaphore for processing coordination
- Efficient cancellation token management

**Scalability Features:**
- Instance isolation prevents self-processing
- Configurable debounce timing per notification type
- Graceful degradation when Redis is unavailable

**Resilience Properties:**
- Automatic fallback on Redis failures
- Comprehensive error handling without system disruption
- Proper resource cleanup on shutdown

### Integration with SearchParameterOperations

The actual processing delegates to domain-specific operations:

```csharp
private async Task ProcessSearchParameterUpdate(CancellationToken cancellationToken)
{
    using var statusScope = _serviceProvider.CreateScope();
    var searchParameterOperations = statusScope.ServiceProvider.GetRequiredService<ISearchParameterOperations>();

    // Apply updates with remote sync flag to prevent loops
    await searchParameterOperations.GetAndApplySearchParameterUpdates(cancellationToken, true);
}
```

This implementation demonstrates how the DebounceConfig framework enables sophisticated notification processing strategies while maintaining simplicity and performance.
