# Periodic Logger Design

## Context

Some information-level diagnostics are valuable when troubleshooting but occur too frequently to emit on every execution. Emitting every occurrence clutters logs and increases ingestion volume, while suppressing them entirely removes useful operational evidence.

The repository needs a reusable wrapper around `Microsoft.Extensions.Logging.ILogger<T>` that delays information-level messages until a configured interval, collapses identical messages, and reports how many times each message occurred. Other log levels must retain their existing immediate behavior.

## Goals

- Provide a drop-in `ILogger<T>` wrapper that preserves the logging category type.
- Aggregate only `LogLevel.Information` messages.
- Emit each distinct information message once per interval.
- Append and structure the number of occurrences, including when the count is one.
- Preserve the original event identifier, exception, and structured log state.
- Pass non-information messages through immediately.
- Flush the final partial interval during disposal.
- Support deterministic time-based unit tests.

## Non-Goals

- Aggregating non-information log levels.
- Preserving ambient logging scopes across deferred emission.
- Persisting pending messages across process termination.
- Enforcing a maximum number of distinct messages in an interval.
- Coordinating aggregation across multiple processes or logger instances.

## Public API and Placement

Add a public sealed `PeriodicLogger<T>` class at `Microsoft.Health.Fhir.Core/Features/Logging/PeriodicLogger.cs` in the `Microsoft.Health.Fhir.Core.Features.Logging` namespace.

The class implements:

- `ILogger<T>`
- `IDisposable`
- `IAsyncDisposable`

Its constructor accepts:

- The wrapped `ILogger<T>`.
- A positive `TimeSpan` aggregation interval.
- An optional `TimeProvider`, defaulting to `TimeProvider.System`.

The class begins one background periodic flush loop during construction. `BeginScope` and `IsEnabled` delegate directly to the wrapped logger and never throw on behalf of the wrapper, including after disposal or after a recorded failure.

Because the class implements `ILogger<T>`, callers use the standard `Microsoft.Extensions.Logging` extension overloads for `LogTrace`, `LogDebug`, `LogInformation`, `LogWarning`, `LogError`, and `LogCritical`; the wrapper does not duplicate those overload families as instance methods.

A public sealed `PeriodicLoggerFlushException` is added at `Microsoft.Health.Fhir.Core/Features/Logging/PeriodicLoggerFlushException.cs` in the same namespace. It identifies a failure that originated in the wrapped logging provider while an aggregate batch was being emitted, and it exposes `DiscardedEntryCount` and `DiscardedOccurrenceCount`.

## Logging Behavior

### Non-Information Messages

Enabled messages whose level is not exactly `LogLevel.Information` are passed to the wrapped logger immediately with their original level, event identifier, state, exception, and formatter.

### Information Messages

Enabled information messages are not immediately passed to the wrapped logger. The wrapper:

1. Invokes the supplied formatter once to obtain the rendered message.
2. Creates an identity from:
   - The complete `EventId`, including its numeric identifier and name.
   - The rendered message.
   - `Exception.ToString()`, or no exception text when the exception is null.
3. Adds a new aggregate entry or increments the existing entry under a short lock. The caller-supplied structured state is snapshotted before the lock is entered, so arbitrary caller code is never run while the aggregation lock is held.

The first occurrence supplies the event identifier, original state, exception, formatter, and insertion position used when the aggregate is emitted.

At each interval, the wrapper atomically replaces the active aggregate dictionary with an empty dictionary while holding the lock. It then emits the detached entries outside the lock. Information calls arriving after the swap belong to the next interval.

Each emitted message:

- Uses `LogLevel.Information`.
- Retains the original `EventId`.
- Retains the original exception instance from the first occurrence.
- Preserves the original structured state.
- Adds a structured `OccurrenceCount` property.
- Appends ` Occurrence count: N.` to the rendered message, where `N` includes the first occurrence.

The emitted state adapter exposes the original key/value pairs when the original state implements `IEnumerable<KeyValuePair<string, object>>`, followed by `OccurrenceCount`. `OccurrenceCount` is reserved by the wrapper; an original property with that name is omitted so providers receive one authoritative count. For an arbitrary non-structured state, the adapter exposes only `OccurrenceCount`.

Identical messages are collapsed only within one interval. The same message in a later interval produces a new emission and count.

## Scope Behavior

Standard `ILogger` scopes are ambient and are interpreted by providers when `Log` is called. The `ILogger` abstraction does not expose the active scope chain to a wrapper. Because information messages are emitted later from the periodic loop, scopes active at the original call site cannot be preserved reliably.

Structured values supplied in the log state remain available on the deferred emission. Consumers that require correlation data on periodic messages must include it in the structured message state rather than relying only on an ambient scope.

## Concurrency

The wrapper supports concurrent calls to `Log`, timer ticks, and disposal.

- A private lock protects the active dictionary and disposed state.
- Flush work is detached under the lock and emitted after releasing it.
- No wrapped logger call occurs while holding the lock.
- No caller-supplied state is enumerated while holding the lock.
- Disposal prevents new messages from being accepted before detaching the final dictionary.
- Multiple disposal calls are safe and do not emit entries more than once.
- Calls to `Log` after disposal throw `ObjectDisposedException`.

The occurrence count uses a type that will not practically overflow during one interval.

## Scheduling and Disposal

The periodic loop uses `PeriodicTimer` with the supplied `TimeProvider`.

Synchronous and asynchronous disposal follow the same logical sequence:

1. Mark the wrapper disposed so no new messages can be accepted.
2. Cancel the periodic loop.
3. Wait for the loop to finish.
4. Detach and emit the final partial interval exactly once.
5. Release timer and cancellation resources.

The interval must be greater than `TimeSpan.Zero`; construction rejects zero or negative intervals.

## Failure Handling

The wrapper does not silently suppress exceptions thrown by the wrapped logger, and it does not turn a logging-provider failure into an application request failure.

Two failure classes are distinguished:

- **Wrapped-provider emission failures.** The wrapped logger threw while the wrapper was emitting a detached aggregate batch. These are surfaced as `PeriodicLoggerFlushException`, whose `InnerException` is the provider exception and whose `DiscardedEntryCount` and `DiscardedOccurrenceCount` report the aggregated entries and the original occurrences that were discarded because of the failure, starting with the entry whose emission threw.
- **Flush-loop infrastructure failures.** The periodic timer or the loop itself faulted. These are recorded and surfaced with their original exception type, so they are never reported as "the wrapped logger failed."

Either failure class permanently stops aggregation. Once aggregation has stopped:

- `Log` degrades to immediate pass-through for every level, including `Information`. Application request paths keep logging and are not made to fail because a logging provider failed. Because the call is a plain pass-through, an exception observed by the caller now comes from the wrapped logger itself, exactly as it would without the wrapper.
- `BeginScope` and `IsEnabled` continue to delegate directly and never throw on behalf of the wrapper.
- The failure stays recorded so that disposal surfaces it.

Entries detached by a failed flush are not retried, because some providers may have accepted an entry before throwing and retrying the complete batch could create duplicates. They are not retried by a later interval or by the final flush. The implementation does not log through an alternate path or pretend a failed entry was emitted.

Disposal always cancels the loop, waits for it, attempts the final partial-interval flush, and releases its timer and cancellation resources, even when stopping the loop fails. It then surfaces every recorded failure: a single failure is rethrown with its original stack, and multiple failures - for example a periodic provider failure plus a distinct final-flush failure - are combined into one `AggregateException` so that none is lost.

`Log` after disposal throws `ObjectDisposedException`. That is the only wrapper-originated exception raised into an application call path.

This follows the existing `ILogger` expectation that providers normally handle their own operational failures, while ensuring unexpected provider exceptions are neither hidden nor injected into unrelated request processing.

## Testing

Unit tests in `Microsoft.Health.Fhir.Core.UnitTests` will use `FakeTimeProvider` and a recording test logger. Tests will follow Arrange-Act-Assert and cover:

- Constructor argument validation.
- Delegation of `BeginScope` and `IsEnabled`.
- Immediate passthrough for non-information levels.
- No information emission before the interval.
- Emission after the interval.
- Deduplication and occurrence counts, including a count of one.
- Distinct rendered messages, event identifiers, and exception text producing distinct entries.
- Preservation of event identifiers, exception instances, and structured state.
- Separation of the same message across intervals.
- Concurrent logging while a flush swaps the active dictionary.
- Final partial-interval flush during synchronous and asynchronous disposal.
- Idempotent disposal.
- Rejection of logging after disposal.
- Degradation to immediate pass-through after a wrapped logger failure, including that `BeginScope`, `IsEnabled`, `LogError`, and `LogInformation` do not throw because of the recorded failure.
- Absence of retries for a failed detached batch, and the discarded entry and occurrence counts it reports.
- Replacement of a caller-supplied `OccurrenceCount` by exactly one authoritative aggregate count.
- State adapter `Count`, index ordering, `{OriginalFormat}` preservation, out-of-range index handling, and non-structured state.
- Exact aggregate counts under concurrent identical information calls.
- Final-flush attempts and propagation of wrapped logger failures during disposal, including preservation of both a periodic failure and a distinct final-flush failure.

## Consequences

The design reduces repetitive information-log volume while preserving per-interval frequency. It remains compatible with standard `ILogger<T>` call sites and providers, preserves the category used by logging providers, and is testable without wall-clock delays.

The wrapper retains one state and exception object for every distinct message until the next flush, with no configured cap. High-cardinality messages can therefore consume memory proportional to the number and size of distinct entries in an interval. Callers should avoid including unbounded identifiers in messages intended for aggregation.
