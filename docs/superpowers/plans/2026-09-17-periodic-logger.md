# Periodic Logger Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a drop-in `ILogger` wrapper that emits deduplicated information messages on a periodic schedule with an occurrence count.

**Architecture:** `PeriodicLogger` delegates scopes, enablement checks, and non-information messages to an inner logger. Information calls render and snapshot their state into a lock-protected dictionary; a `PeriodicTimer` driven by an injectable `TimeProvider` atomically swaps and emits each interval's dictionary, while coordinated synchronous/asynchronous disposal cancels the loop and flushes the final partial interval.

**Tech Stack:** C# on .NET 10, `Microsoft.Extensions.Logging`, `PeriodicTimer`, `TimeProvider`, xUnit, `Microsoft.Extensions.Time.Testing.FakeTimeProvider`.

## Global Constraints

- Add the implementation at `src/Microsoft.Health.Fhir.Core/Features/Logging/PeriodicLogger.cs` in namespace `Microsoft.Health.Fhir.Core.Features.Logging`.
- Add the wrapped-provider failure type at `src/Microsoft.Health.Fhir.Core/Features/Logging/PeriodicLoggerFlushException.cs` in the same namespace.
- Add tests at `src/Microsoft.Health.Fhir.Core.UnitTests/Features/Logging/PeriodicLoggerTests.cs`.
- Aggregate exactly `LogLevel.Information`; pass every other enabled level through immediately.
- Identify duplicates by complete `EventId`, rendered message, and `Exception.ToString()`.
- Append ` Occurrence count: N.` and expose structured property `OccurrenceCount` on every periodic emission, including count one.
- Preserve the first occurrence's event identifier, exception instance, and structured values.
- Do not attempt to preserve ambient logger scopes for deferred information messages.
- Do not cap the number of distinct messages retained in an interval.
- Flush pending messages during both synchronous and asynchronous disposal.
- Calls to `Log` after disposal throw `ObjectDisposedException`; `BeginScope` and `IsEnabled` always delegate directly and never throw on behalf of the wrapper.
- Do not swallow inner logger failures; stop periodic scheduling, degrade later `Log` calls to immediate pass-through, attempt the final flush, and surface every recorded failure during disposal.
- Add XML documentation to all public members and follow the repository copyright header and C# conventions.
- Do not add new package dependencies.

---

### Task 1: Logger contract and immediate delegation

**Files:**
- Create: `src/Microsoft.Health.Fhir.Core/Features/Logging/PeriodicLogger.cs`
- Create: `src/Microsoft.Health.Fhir.Core.UnitTests/Features/Logging/PeriodicLoggerTests.cs`

**Interfaces:**
- Consumes: `ILogger`, `TimeProvider`, `TimeSpan`.
- Produces: `public sealed class PeriodicLogger : ILogger, IDisposable, IAsyncDisposable` with constructor `PeriodicLogger(ILogger logger, TimeSpan interval, TimeProvider timeProvider = null)`.

- [ ] **Step 1: Write failing constructor, scope, enablement, and passthrough tests**

Create `PeriodicLoggerTests` with the repository copyright header, namespace `Microsoft.Health.Fhir.Core.UnitTests.Features.Logging`, owning-team trait, and a nested `RecordingLogger`.

Add these tests:

```csharp
[Theory]
[InlineData(0)]
[InlineData(-1)]
public void GivenNonPositiveInterval_WhenConstructing_ThenThrows(double seconds)
{
    var logger = new RecordingLogger();

    Assert.Throws<ArgumentOutOfRangeException>(
        () => new PeriodicLogger(logger, TimeSpan.FromSeconds(seconds)));
}

[Fact]
public void GivenNullLogger_WhenConstructing_ThenThrows()
{
    Assert.Throws<ArgumentNullException>(
        () => new PeriodicLogger(null, TimeSpan.FromMinutes(1)));
}

[Fact]
public async Task GivenWrappedLogger_WhenCheckingContract_ThenDelegates()
{
    var innerLogger = new RecordingLogger { Enabled = false };
    await using var logger = new PeriodicLogger(innerLogger, TimeSpan.FromMinutes(1));
    object scopeState = new();

    IDisposable scope = logger.BeginScope(scopeState);
    bool enabled = logger.IsEnabled(LogLevel.Warning);

    Assert.Same(innerLogger.Scope, scope);
    Assert.Same(scopeState, innerLogger.ScopeState);
    Assert.False(enabled);
    Assert.Equal(LogLevel.Warning, innerLogger.LastEnabledLevel);
}

[Theory]
[InlineData(LogLevel.Trace)]
[InlineData(LogLevel.Debug)]
[InlineData(LogLevel.Warning)]
[InlineData(LogLevel.Error)]
[InlineData(LogLevel.Critical)]
[InlineData(LogLevel.None)]
public async Task GivenNonInformationMessage_WhenLogging_ThenWritesImmediately(LogLevel level)
{
    var innerLogger = new RecordingLogger();
    await using var logger = new PeriodicLogger(innerLogger, TimeSpan.FromMinutes(1));
    var eventId = new EventId(17, "Immediate");
    var exception = new InvalidOperationException("failure");

    logger.Log(level, eventId, "message", exception, static (state, _) => state);

    RecordedLog record = Assert.Single(innerLogger.Records);
    Assert.Equal(level, record.Level);
    Assert.Equal(eventId, record.EventId);
    Assert.Same(exception, record.Exception);
    Assert.Equal("message", record.Message);
}
```

Define `RecordingLogger` so `BeginScope`, `IsEnabled`, and `Log<TState>` record all inputs. Store records in a thread-safe `ConcurrentQueue<RecordedLog>`. `RecordedLog` must contain `LogLevel`, `EventId`, `Exception`, rendered `Message`, and a copied `IReadOnlyList<KeyValuePair<string, object>>` when the state is structured.

- [ ] **Step 2: Run the focused tests and verify they fail**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj --filter "FullyQualifiedName~PeriodicLoggerTests"
```

Expected: compilation fails because `PeriodicLogger` does not exist.

- [ ] **Step 3: Implement the public contract and lifecycle scaffold**

Create `PeriodicLogger.cs` with:

```csharp
public sealed class PeriodicLogger : ILogger, IDisposable, IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly TimeSpan _interval;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _flushLoopTask;
    private bool _disposed;

    public PeriodicLogger(ILogger logger, TimeSpan interval, TimeProvider timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "The logging interval must be greater than zero.");
        }

        _logger = logger;
        _interval = interval;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _flushLoopTask = RunFlushLoopAsync(_cancellationTokenSource.Token);
    }

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => _logger.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => _logger.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception exception,
        Func<TState, Exception, string> formatter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(formatter);

        if (!_logger.IsEnabled(logLevel))
        {
            return;
        }

        if (logLevel != LogLevel.Information)
        {
            _logger.Log(logLevel, eventId, state, exception, formatter);
            return;
        }

        return;
    }

    private async Task RunFlushLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval, _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _cancellationTokenSource.CancelAsync();
        await _flushLoopTask;
        _cancellationTokenSource.Dispose();
    }
}
```

Add XML documentation to the class, constructor, and public interface members. This scaffold is intentionally single-disposer only; Task 3 replaces it with concurrency-safe idempotent disposal before completion.

- [ ] **Step 4: Run the focused tests and verify they pass**

Run the Task 1 command again.

Expected: all Task 1 tests pass.

- [ ] **Step 5: Commit the contract**

```powershell
git add .\src\Microsoft.Health.Fhir.Core\Features\Logging\PeriodicLogger.cs .\src\Microsoft.Health.Fhir.Core.UnitTests\Features\Logging\PeriodicLoggerTests.cs
git commit -m "Add periodic logger contract" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 2: Periodic aggregation and structured occurrence counts

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Core/Features/Logging/PeriodicLogger.cs`
- Modify: `src/Microsoft.Health.Fhir.Core.UnitTests/Features/Logging/PeriodicLoggerTests.cs`

**Interfaces:**
- Consumes: `PeriodicLogger` from Task 1 and `FakeTimeProvider`.
- Produces: interval-based information aggregation keyed by `LogIdentity`; internal immutable `PeriodicLogState` exposed to the wrapped logger as `IReadOnlyList<KeyValuePair<string, object>>`.

- [ ] **Step 1: Write failing timing, identity, and structured-state tests**

Add a helper that advances fake time and waits for a target record count:

```csharp
private static async Task AdvanceAndWaitAsync(
    FakeTimeProvider timeProvider,
    RecordingLogger logger,
    TimeSpan interval,
    int expectedCount)
{
    timeProvider.Advance(interval);
    await logger.WaitForRecordCountAsync(expectedCount);
}
```

Add tests with the following assertions:

```csharp
[Fact]
public async Task GivenInformationMessages_WhenIntervalElapses_ThenCollapsesIdenticalEntries()
{
    var timeProvider = new FakeTimeProvider();
    var innerLogger = new RecordingLogger();
    await using var logger = new PeriodicLogger(innerLogger, TimeSpan.FromMinutes(1), timeProvider);
    var eventId = new EventId(23, "Periodic");

    logger.LogInformation(eventId, "Queue depth is {QueueDepth}.", 5);
    logger.LogInformation(eventId, "Queue depth is {QueueDepth}.", 5);
    logger.LogInformation(eventId, "Queue depth is {QueueDepth}.", 5);

    Assert.Empty(innerLogger.Records);
    await AdvanceAndWaitAsync(timeProvider, innerLogger, TimeSpan.FromMinutes(1), 1);

    RecordedLog record = Assert.Single(innerLogger.Records);
    Assert.Equal(LogLevel.Information, record.Level);
    Assert.Equal(eventId, record.EventId);
    Assert.Equal("Queue depth is 5. Occurrence count: 3.", record.Message);
    Assert.Contains(record.State, pair => pair.Key == "QueueDepth" && Equals(pair.Value, 5));
    Assert.Contains(record.State, pair => pair.Key == "OccurrenceCount" && Equals(pair.Value, 3L));
}

[Fact]
public async Task GivenSingleInformationMessage_WhenIntervalElapses_ThenIncludesCountOne()
{
    var timeProvider = new FakeTimeProvider();
    var innerLogger = new RecordingLogger();
    await using var logger = new PeriodicLogger(innerLogger, TimeSpan.FromMinutes(1), timeProvider);

    logger.LogInformation("single");
    await AdvanceAndWaitAsync(timeProvider, innerLogger, TimeSpan.FromMinutes(1), 1);

    RecordedLog record = Assert.Single(innerLogger.Records);
    Assert.Equal("single Occurrence count: 1.", record.Message);
    Assert.Contains(record.State, pair => pair.Key == "OccurrenceCount" && Equals(pair.Value, 1L));
}

[Fact]
public async Task GivenSameTemplateWithDifferentValues_WhenIntervalElapses_ThenWritesDistinctMessages()
{
    var timeProvider = new FakeTimeProvider();
    var innerLogger = new RecordingLogger();
    await using var logger = new PeriodicLogger(innerLogger, TimeSpan.FromMinutes(1), timeProvider);

    logger.LogInformation("Queue depth is {QueueDepth}.", 5);
    logger.LogInformation("Queue depth is {QueueDepth}.", 6);
    await AdvanceAndWaitAsync(timeProvider, innerLogger, TimeSpan.FromMinutes(1), 2);

    Assert.Equal(
        new[] { "Queue depth is 5. Occurrence count: 1.", "Queue depth is 6. Occurrence count: 1." },
        innerLogger.Records.Select(record => record.Message).OrderBy(message => message));
}

[Fact]
public async Task GivenDifferentEventIds_WhenRenderedMessageMatches_ThenWritesDistinctMessages()
{
    var timeProvider = new FakeTimeProvider();
    var innerLogger = new RecordingLogger();
    await using var logger = new PeriodicLogger(innerLogger, TimeSpan.FromMinutes(1), timeProvider);

    logger.LogInformation(new EventId(1, "First"), "same");
    logger.LogInformation(new EventId(2, "Second"), "same");
    await AdvanceAndWaitAsync(timeProvider, innerLogger, TimeSpan.FromMinutes(1), 2);

    Assert.Equal(
        new[] { new EventId(1, "First"), new EventId(2, "Second") },
        innerLogger.Records.Select(record => record.EventId).OrderBy(eventId => eventId.Id));
}

[Fact]
public async Task GivenDifferentExceptionText_WhenRenderedMessageMatches_ThenWritesDistinctMessages()
{
    var timeProvider = new FakeTimeProvider();
    var innerLogger = new RecordingLogger();
    await using var logger = new PeriodicLogger(innerLogger, TimeSpan.FromMinutes(1), timeProvider);

    logger.LogInformation(new InvalidOperationException("first"), "same");
    logger.LogInformation(new InvalidOperationException("second"), "same");
    await AdvanceAndWaitAsync(timeProvider, innerLogger, TimeSpan.FromMinutes(1), 2);

    Assert.Equal(2, innerLogger.Records.Count);
}

[Fact]
public async Task GivenEquivalentExceptionText_WhenMessagesMatch_ThenUsesFirstExceptionInstance()
{
    var timeProvider = new FakeTimeProvider();
    var innerLogger = new RecordingLogger();
    await using var logger = new PeriodicLogger(innerLogger, TimeSpan.FromMinutes(1), timeProvider);
    var firstException = new InvalidOperationException("same");
    var secondException = new InvalidOperationException("same");

    logger.LogInformation(firstException, "message");
    logger.LogInformation(secondException, "message");
    await AdvanceAndWaitAsync(timeProvider, innerLogger, TimeSpan.FromMinutes(1), 1);

    RecordedLog record = Assert.Single(innerLogger.Records);
    Assert.Same(firstException, record.Exception);
    Assert.Equal("message Occurrence count: 2.", record.Message);
}

[Fact]
public async Task GivenSameMessageInDifferentIntervals_WhenFlushed_ThenWritesOncePerInterval()
{
    var timeProvider = new FakeTimeProvider();
    var innerLogger = new RecordingLogger();
    await using var logger = new PeriodicLogger(innerLogger, TimeSpan.FromMinutes(1), timeProvider);

    logger.LogInformation("message");
    await AdvanceAndWaitAsync(timeProvider, innerLogger, TimeSpan.FromMinutes(1), 1);
    logger.LogInformation("message");
    await AdvanceAndWaitAsync(timeProvider, innerLogger, TimeSpan.FromMinutes(1), 2);

    Assert.All(innerLogger.Records, record => Assert.Equal("message Occurrence count: 1.", record.Message));
}

[Fact]
public async Task GivenDisabledInformationLevel_WhenLogging_ThenDoesNotAggregate()
{
    var timeProvider = new FakeTimeProvider();
    var innerLogger = new RecordingLogger { Enabled = false };
    await using var logger = new PeriodicLogger(innerLogger, TimeSpan.FromMinutes(1), timeProvider);

    logger.LogInformation("disabled");
    timeProvider.Advance(TimeSpan.FromMinutes(1));
    await Task.Yield();

    Assert.Empty(innerLogger.Records);
}
```

- [ ] **Step 2: Run the focused tests and verify they fail**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj --filter "FullyQualifiedName~PeriodicLoggerTests"
```

Expected: new information aggregation tests time out or fail because Task 1 does not store or emit information messages.

- [ ] **Step 3: Implement aggregation identity and state snapshot**

Add:

```csharp
private const string OccurrenceCountPropertyName = "OccurrenceCount";
private readonly object _syncLock = new();
private Dictionary<LogIdentity, AggregatedLogEntry> _entries = new();

private readonly record struct LogIdentity(EventId EventId, string Message, string ExceptionText);

private sealed class AggregatedLogEntry
{
    public AggregatedLogEntry(EventId eventId, Exception exception, PeriodicLogState state)
    {
        EventId = eventId;
        Exception = exception;
        State = state;
    }

    public EventId EventId { get; }

    public Exception Exception { get; }

    public long Count { get; set; } = 1;

    public PeriodicLogState State { get; }
}
```

Implement `PeriodicLogState` as an immutable `IReadOnlyList<KeyValuePair<string, object>>` that:

- Copies the first occurrence's structured key/value pairs at capture time.
- Excludes any original `OccurrenceCount` key so the wrapper's count is authoritative.
- Stores the already-rendered message.
- Creates an emission-specific view with the current `long` count.
- Enumerates original pairs followed by `("OccurrenceCount", count)`.
- Formats as `$"{renderedMessage} Occurrence count: {count}."`.

In `Log<TState>`, render once and copy structured state before acquiring the lock:

```csharp
string message = formatter(state, exception);
var identity = new LogIdentity(eventId, message, exception?.ToString());
PeriodicLogState periodicState = PeriodicLogState.Create(state, message);

lock (_syncLock)
{
    ThrowIfUnavailable();

    if (_entries.TryGetValue(identity, out AggregatedLogEntry existingEntry))
    {
        existingEntry.Count++;
    }
    else
    {
        _entries.Add(identity, new AggregatedLogEntry(eventId, exception, periodicState));
    }
}
```

Implement `Flush()` by swapping dictionaries under the lock and logging outside it:

```csharp
private void Flush()
{
    Dictionary<LogIdentity, AggregatedLogEntry> entries;

    lock (_syncLock)
    {
        entries = _entries;
        _entries = new Dictionary<LogIdentity, AggregatedLogEntry>();
    }

    foreach (AggregatedLogEntry entry in entries.Values)
    {
        PeriodicLogState state = entry.State.WithCount(entry.Count);
        _logger.Log(
            LogLevel.Information,
            entry.EventId,
            state,
            entry.Exception,
            static (logState, _) => logState.ToString());
    }
}
```

Call `Flush()` after every successful timer tick. Keep the wrapped logger call outside `_syncLock`.

- [ ] **Step 4: Run the focused tests and verify they pass**

Run the Task 2 command again.

Expected: all Task 1 and Task 2 tests pass without wall-clock delay.

- [ ] **Step 5: Commit periodic aggregation**

```powershell
git add .\src\Microsoft.Health.Fhir.Core\Features\Logging\PeriodicLogger.cs .\src\Microsoft.Health.Fhir.Core.UnitTests\Features\Logging\PeriodicLoggerTests.cs
git commit -m "Aggregate periodic information logs" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 3: Concurrent disposal and provider-failure handling

**Files:**
- Modify: `src/Microsoft.Health.Fhir.Core/Features/Logging/PeriodicLogger.cs`
- Create: `src/Microsoft.Health.Fhir.Core/Features/Logging/PeriodicLoggerFlushException.cs`
- Modify: `src/Microsoft.Health.Fhir.Core.UnitTests/Features/Logging/PeriodicLoggerTests.cs`

**Interfaces:**
- Consumes: periodic aggregation from Task 2.
- Produces: concurrency-safe idempotent `Dispose`/`DisposeAsync`, final partial-interval flushing, classified failure recording, and degradation to immediate pass-through after a failure.

- [ ] **Step 1: Write failing disposal, concurrency, and failure tests**

Add these tests:

```csharp
[Fact]
public async Task GivenPendingMessage_WhenDisposedAsync_ThenFlushesFinalInterval()
{
    var innerLogger = new RecordingLogger();
    var logger = new PeriodicLogger(innerLogger, TimeSpan.FromMinutes(1), new FakeTimeProvider());
    logger.LogInformation("pending");

    await logger.DisposeAsync();

    RecordedLog record = Assert.Single(innerLogger.Records);
    Assert.Equal("pending Occurrence count: 1.", record.Message);
}

[Fact]
public void GivenPendingMessage_WhenDisposed_ThenFlushesFinalInterval()
{
    var innerLogger = new RecordingLogger();
    var logger = new PeriodicLogger(innerLogger, TimeSpan.FromMinutes(1), new FakeTimeProvider());
    logger.LogInformation("pending");

    logger.Dispose();

    Assert.Single(innerLogger.Records);
}

[Fact]
public async Task GivenConcurrentDisposal_WhenCalledMultipleTimes_ThenFlushesOnlyOnce()
{
    var innerLogger = new RecordingLogger();
    var logger = new PeriodicLogger(innerLogger, TimeSpan.FromMinutes(1), new FakeTimeProvider());
    logger.LogInformation("pending");

    await Task.WhenAll(
        logger.DisposeAsync().AsTask(),
        logger.DisposeAsync().AsTask(),
        Task.Run(logger.Dispose));

    Assert.Single(innerLogger.Records);
}

[Fact]
public async Task GivenDisposedLogger_WhenLogging_ThenThrows()
{
    var logger = new PeriodicLogger(new RecordingLogger(), TimeSpan.FromMinutes(1), new FakeTimeProvider());
    await logger.DisposeAsync();

    Assert.Throws<ObjectDisposedException>(() => logger.LogInformation("late"));
}

[Fact]
public async Task GivenLoggingDuringFlush_WhenDictionaryIsSwapped_ThenMessageMovesToNextInterval()
{
    var timeProvider = new FakeTimeProvider();
    var innerLogger = new RecordingLogger();
    var emissionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseEmission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    innerLogger.OnLog = () =>
    {
        emissionStarted.TrySetResult();
        releaseEmission.Task.GetAwaiter().GetResult();
    };
    await using var logger = new PeriodicLogger(innerLogger, TimeSpan.FromMinutes(1), timeProvider);
    logger.LogInformation("message");

    timeProvider.Advance(TimeSpan.FromMinutes(1));
    await emissionStarted.Task;
    logger.LogInformation("message");
    releaseEmission.SetResult();
    await innerLogger.WaitForRecordCountAsync(1);

    Assert.Equal("message Occurrence count: 1.", Assert.Single(innerLogger.Records).Message);

    innerLogger.OnLog = null;
    await AdvanceAndWaitAsync(timeProvider, innerLogger, TimeSpan.FromMinutes(1), 2);

    Assert.All(innerLogger.Records, record => Assert.Equal("message Occurrence count: 1.", record.Message));
}

[Fact]
public async Task GivenProviderFailure_WhenPeriodicFlushRuns_ThenLaterCallsDegradeToImmediatePassthrough()
{
    var timeProvider = new FakeTimeProvider();
    var providerException = new InvalidOperationException("provider failed");
    var innerLogger = new RecordingLogger { ExceptionToThrow = providerException };
    var logger = new PeriodicLogger(innerLogger, TimeSpan.FromMinutes(1), timeProvider);
    logger.LogInformation("message");

    timeProvider.Advance(TimeSpan.FromMinutes(1));
    await logger.FlushLoopTask.WaitAsync(TestTimeout);
    innerLogger.ExceptionToThrow = null;

    // None of these throw because of the latched failure.
    bool enabled = logger.IsEnabled(LogLevel.Information);
    IDisposable scope = logger.BeginScope(new object());
    logger.LogError("error");
    logger.LogInformation("later");

    Assert.True(enabled);
    Assert.Same(innerLogger.Scope, scope);
    Assert.Equal(new[] { "error", "later" }, innerLogger.Records.Select(record => record.Message));

    PeriodicLoggerFlushException flushException = await ThrowsDisposalFailureAsync<PeriodicLoggerFlushException>(logger);
    Assert.Same(providerException, flushException.InnerException);
    Assert.Equal(1, flushException.DiscardedEntryCount);
    Assert.Equal(1L, flushException.DiscardedOccurrenceCount);
}
```

Extend `RecordingLogger` with `Action OnLog`, `Exception ExceptionToThrow`, `Func<int, Exception> ExceptionForAttempt`, an attempt counter, attempted-message capture, raw-state capture, and thread-safe record snapshots. Invoke `OnLog` and record the attempt before throwing. Expose the flush loop task from `PeriodicLogger` as an `internal` member so tests can await loop termination deterministically instead of polling, and bound every asynchronous wait with `WaitAsync(TestTimeout)` so regressions fail instead of hanging.

- [ ] **Step 2: Run the focused tests and verify they fail**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj --filter "FullyQualifiedName~PeriodicLoggerTests"
```

Expected: final flush, concurrent disposal, and provider-failure tests fail against the Task 2 lifecycle scaffold.

- [ ] **Step 3: Implement one-owner disposal coordination**

Replace the `_disposed`-only scaffold with:

```csharp
private TaskCompletionSource _disposeCompletion;
private volatile bool _aggregationSuspended;
private ExceptionDispatchInfo _providerFailure;
private ExceptionDispatchInfo _flushLoopFailure;
```

`BeginScope` and `IsEnabled` delegate directly to the wrapped logger and must not consult disposal or failure state. Only `Log<TState>` checks disposal:

```csharp
private void ThrowIfDisposed()
{
    lock (_syncLock)
    {
        ObjectDisposedException.ThrowIf(_disposeCompletion != null, this);
    }
}
```

Implement `DisposeAsync` so exactly one caller becomes the owner:

```csharp
public async ValueTask DisposeAsync()
{
    TaskCompletionSource completion;
    bool ownsDisposal = false;

    lock (_syncLock)
    {
        completion = _disposeCompletion;

        if (completion == null)
        {
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeCompletion = completion;
            ownsDisposal = true;
        }
    }

    if (!ownsDisposal)
    {
        await completion.Task;
        return;
    }

    try
    {
        await CompleteDisposalAsync();
        completion.SetResult();
    }
    catch (Exception exception)
    {
        completion.SetException(exception);
        throw;
    }
}
```

`CompleteDisposalAsync` must:

1. Cancel `_cancellationTokenSource` and await `_flushLoopTask` inside a `try`/`catch` so a cancellation or loop fault cannot skip the remaining steps.
2. Attempt `Flush()` in its own `try`/`catch`, even if the periodic loop recorded a failure.
3. Dispose `_cancellationTokenSource` in a `finally`, so it is always released.
4. Rethrow a single recorded failure with its original stack, or throw one `AggregateException` when more than one failure exists, so a periodic failure and a distinct final-flush failure are both preserved.

Keep `Dispose()` as a blocking call to `DisposeAsync`.

- [ ] **Step 4: Record failures by class and degrade to immediate pass-through**

`Flush()` emits the detached batch entry by entry. When the wrapped logger throws, wrap the provider exception in a `PeriodicLoggerFlushException` that reports the number of aggregated entries and the total occurrences discarded from the failing entry onward, and abandon the batch. Never retry a failed detached batch, because the provider may have accepted some entries before throwing.

In `RunFlushLoopAsync`, keep the cancellation catch, catch `PeriodicLoggerFlushException` around the flush call to record a wrapped-provider failure, and keep an outer catch for everything else to record a flush-loop infrastructure failure with its original exception type. Loop faults must not be reported as "the wrapped logger failed."

Both failure classes set `_aggregationSuspended`. Once it is set, `Log<TState>` stops buffering and writes every level - including `Information` - straight through to the wrapped logger, so a logging provider failure never throws into an application request path. The recorded failure stays latched for disposal to surface.

In `Log<TState>`, snapshot the caller-supplied state into `PeriodicLogState` *before* entering `_syncLock`: take the lock once to try to increment an existing entry, build the state outside the lock when none exists, then take the lock again to add or increment. Caller-supplied enumerators must never run while the aggregation lock is held.

- [ ] **Step 5: Run focused tests and the Core project build**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj --filter "FullyQualifiedName~PeriodicLoggerTests"
dotnet build .\src\Microsoft.Health.Fhir.Core\Microsoft.Health.Fhir.Core.csproj --no-restore
```

Expected: all `PeriodicLoggerTests` pass and the Core project builds with zero warnings and errors.

- [ ] **Step 6: Commit lifecycle hardening**

```powershell
git add .\src\Microsoft.Health.Fhir.Core\Features\Logging\PeriodicLogger.cs .\src\Microsoft.Health.Fhir.Core.UnitTests\Features\Logging\PeriodicLoggerTests.cs
git commit -m "Harden periodic logger lifecycle" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 4: Final review and regression validation

**Files:**
- Review: `src/Microsoft.Health.Fhir.Core/Features/Logging/PeriodicLogger.cs`
- Review: `src/Microsoft.Health.Fhir.Core.UnitTests/Features/Logging/PeriodicLoggerTests.cs`
- Reference: `docs/superpowers/specs/2026-09-17-periodic-logger-design.md`

**Interfaces:**
- Consumes: completed `PeriodicLogger`.
- Produces: verified implementation matching every approved design requirement.

- [ ] **Step 1: Review the implementation against the specification**

Confirm explicitly in the diff that:

- No wrapped logger call occurs while `_syncLock` is held.
- No caller-supplied state is enumerated while `_syncLock` is held.
- Information messages are rendered once at capture time.
- The first occurrence's structured state is copied rather than retained as mutable provider state.
- Complete `EventId`, rendered text, and exception text form the identity.
- `OccurrenceCount` is a `long`, is always emitted exactly once, and replaces any caller-supplied value with the same name.
- A timer tick swaps the active dictionary before provider calls.
- Disposal blocks new logging before cancellation and final flushing.
- Both sync and async disposal share the same one-owner path, always attempt the final flush, and always release the cancellation source.
- A failed periodic provider write stops the loop, is never retried, and degrades later `Log` calls to immediate pass-through rather than throwing into request paths.
- Wrapped-provider emission failures are reported as `PeriodicLoggerFlushException` with discarded entry/occurrence counts, and flush-loop infrastructure failures keep their original exception type.
- `BeginScope` and `IsEnabled` never throw on behalf of the wrapper.

- [ ] **Step 2: Run the complete Core unit-test project**

Run:

```powershell
dotnet test .\src\Microsoft.Health.Fhir.Core.UnitTests\Microsoft.Health.Fhir.Core.UnitTests.csproj --no-restore
```

Expected: the complete Core unit-test project passes.

- [ ] **Step 3: Run repository diff checks**

Run:

```powershell
git --no-pager diff --check
git --no-pager status --short
```

Expected: `diff --check` reports no errors, and status contains only the intended periodic logger implementation/test changes plus the committed design and plan history.

- [ ] **Step 4: Request code review**

Invoke the `requesting-code-review` skill, address any correctness findings, and rerun the smallest affected validation command.

- [ ] **Step 5: Commit review fixes if needed**

If review required code changes:

```powershell
git add .\src\Microsoft.Health.Fhir.Core\Features\Logging\PeriodicLogger.cs .\src\Microsoft.Health.Fhir.Core.UnitTests\Features\Logging\PeriodicLoggerTests.cs
git commit -m "Address periodic logger review" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

If no changes were required, do not create an empty commit.
