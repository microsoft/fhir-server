// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Features.Logging
{
    /// <summary>
    /// Wraps an <see cref="ILogger"/> and collapses repeated <see cref="LogLevel.Information"/> messages into one
    /// emission per aggregation interval.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Enabled messages whose level is not exactly <see cref="LogLevel.Information"/> are passed to the wrapped logger
    /// immediately and are never aggregated. Enabled information messages are buffered and grouped by the complete
    /// <see cref="EventId"/>, the rendered message, and the exception text. A background loop emits each distinct
    /// group once per interval with an added <c>OccurrenceCount</c> structured property.
    /// </para>
    /// <para>
    /// If the wrapped logger throws while a batch is being emitted, aggregation stops permanently: the failure is
    /// recorded so that disposal can surface it, and every later information call degrades to immediate pass-through
    /// instead of throwing into application request paths. Failures raised by the periodic timer or the flush loop
    /// itself stop aggregation the same way and are surfaced with their original exception type. A batch that failed
    /// to emit is discarded and is never retried, because the wrapped provider may already have accepted some of its
    /// entries.
    /// </para>
    /// <para>
    /// <see cref="BeginScope{TState}(TState)"/> and <see cref="IsEnabled(LogLevel)"/> always delegate directly to the
    /// wrapped logger, including after disposal or a recorded failure. Only <see cref="Log{TState}"/> throws
    /// <see cref="ObjectDisposedException"/> once the wrapper has been disposed.
    /// </para>
    /// </remarks>
    public sealed class PeriodicLogger : ILogger, IDisposable, IAsyncDisposable
    {
        private const string OccurrenceCountPropertyName = "OccurrenceCount";
        private static readonly AsyncLocal<PeriodicLogger> _activeFlushLogger = new();

        private readonly ILogger _logger;
        private readonly TimeSpan _interval;
        private readonly TimeProvider _timeProvider;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly object _syncLock = new();
        private readonly Task _flushLoopTask;
        private Dictionary<LogIdentity, AggregatedLogEntry> _entries = new();
        private TaskCompletionSource _disposeCompletion;
        private volatile bool _aggregationSuspended;
        private ExceptionDispatchInfo _providerFailure;
        private ExceptionDispatchInfo _flushLoopFailure;

        /// <summary>
        /// Initializes a new instance of the <see cref="PeriodicLogger"/> class and starts its background flush loop.
        /// </summary>
        /// <param name="logger">The wrapped logger that receives immediate and aggregated messages.</param>
        /// <param name="interval">The aggregation interval. Must be greater than <see cref="TimeSpan.Zero"/>.</param>
        /// <param name="timeProvider">
        /// The time provider used to drive the periodic timer. When <c>null</c>, <see cref="TimeProvider.System"/> is
        /// used, so the flush loop follows wall-clock time.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is not positive.</exception>
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

        private enum AggregationOutcome
        {
            /// <summary>The message was counted against a buffered entry.</summary>
            Aggregated,

            /// <summary>No buffered entry exists yet, so the caller must snapshot the state outside the lock.</summary>
            StateRequired,

            /// <summary>Aggregation has stopped, so the caller must write to the wrapped logger immediately.</summary>
            PassThrough,
        }

        /// <summary>
        /// Gets the background flush loop task. Exposed internally so tests can observe loop termination
        /// deterministically instead of polling.
        /// </summary>
        internal Task FlushLoopTask => _flushLoopTask;

        /// <summary>
        /// Begins a logical operation scope by delegating directly to the wrapped logger.
        /// </summary>
        /// <typeparam name="TState">The type of the scope state.</typeparam>
        /// <param name="state">The scope state.</param>
        /// <returns>The scope returned by the wrapped logger.</returns>
        /// <remarks>
        /// Scope handling is never intercepted, so this call does not throw because the wrapper was disposed or
        /// because a recorded failure stopped aggregation.
        /// </remarks>
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
            => _logger.BeginScope(state);

        /// <summary>
        /// Determines whether the wrapped logger is enabled for the specified level.
        /// </summary>
        /// <param name="logLevel">The level to check.</param>
        /// <returns>The value returned by the wrapped logger.</returns>
        /// <remarks>
        /// Enablement checks are never intercepted, so this call does not throw because the wrapper was disposed or
        /// because a recorded failure stopped aggregation.
        /// </remarks>
        public bool IsEnabled(LogLevel logLevel) => _logger.IsEnabled(logLevel);

        /// <summary>
        /// Writes a log entry, aggregating enabled <see cref="LogLevel.Information"/> messages and passing every other
        /// enabled level through to the wrapped logger immediately.
        /// </summary>
        /// <typeparam name="TState">The type of the log state.</typeparam>
        /// <param name="logLevel">The level of the entry.</param>
        /// <param name="eventId">The event identifier of the entry.</param>
        /// <param name="state">The structured state of the entry.</param>
        /// <param name="exception">The exception associated with the entry, if any.</param>
        /// <param name="formatter">The formatter that renders <paramref name="state"/> and <paramref name="exception"/>.</param>
        /// <exception cref="ObjectDisposedException">Thrown when the wrapper has already been disposed.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="formatter"/> is <c>null</c>.</exception>
        /// <remarks>
        /// After a recorded failure stops aggregation, information messages are passed to the wrapped logger
        /// immediately rather than buffered, so any exception observed here originates from the wrapped logger itself.
        /// </remarks>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            ThrowIfDisposed();

            ArgumentNullException.ThrowIfNull(formatter);

            if (!_logger.IsEnabled(logLevel))
            {
                return;
            }

            if (logLevel != LogLevel.Information || _aggregationSuspended)
            {
                // _aggregationSuspended only ever transitions false -> true. A momentarily stale read costs one extra
                // formatter call because TryAggregate re-checks the flag under the lock and remains authoritative.
                _logger.Log(logLevel, eventId, state, exception, formatter);
                return;
            }

            string message = formatter(state, exception);
            var identity = new LogIdentity(eventId.Id, eventId.Name, message, exception?.ToString());

            AggregationOutcome outcome = TryAggregate(identity, entry: null);

            if (outcome == AggregationOutcome.StateRequired)
            {
                // The caller-supplied state is snapshotted outside the aggregation lock so that arbitrary caller code
                // (enumerators, property getters, ToString overrides) never runs while the lock is held.
                var newEntry = new AggregatedLogEntry(eventId, exception, PeriodicLogState.Create(state, message));
                outcome = TryAggregate(identity, newEntry);
            }

            if (outcome == AggregationOutcome.PassThrough)
            {
                _logger.Log(logLevel, eventId, state, exception, formatter);
            }
        }

        /// <summary>
        /// Disposes the logger synchronously, flushing the final partial interval.
        /// </summary>
        /// <exception cref="PeriodicLoggerFlushException">
        /// Thrown when the wrapped logger failed while emitting an aggregated batch.
        /// </exception>
        /// <exception cref="AggregateException">
        /// Thrown when more than one failure was recorded during the lifetime of the wrapper.
        /// </exception>
        /// <remarks>
        /// Disposal always attempts the final flush and always releases its timer and cancellation resources, then
        /// surfaces every recorded failure.
        /// </remarks>
        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        /// <summary>
        /// Disposes the logger asynchronously, flushing the final partial interval.
        /// </summary>
        /// <returns>A task that completes when disposal is finished.</returns>
        /// <exception cref="PeriodicLoggerFlushException">
        /// Thrown when the wrapped logger failed while emitting an aggregated batch.
        /// </exception>
        /// <exception cref="AggregateException">
        /// Thrown when more than one failure was recorded during the lifetime of the wrapper.
        /// </exception>
        /// <remarks>
        /// Disposal is idempotent and safe to call concurrently. Every caller observes the same outcome, and the final
        /// interval is emitted at most once. Disposal always attempts the final flush and always releases its timer
        /// and cancellation resources, even when stopping the background loop fails.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            ThrowIfDisposingFromFlush();

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
                await completion.Task.ConfigureAwait(false);
                return;
            }

            try
            {
                await CompleteDisposalAsync().ConfigureAwait(false);
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);

                // The owner rethrows below, and no other caller may ever await the completion task. Observe the
                // fault here so a logging-provider failure cannot raise TaskScheduler.UnobservedTaskException.
                _ = completion.Task.Exception;

                throw;
            }
        }

        /// <summary>
        /// Runs the periodic flush loop until cancellation is requested or a failure stops aggregation.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token used to stop the loop.</param>
        /// <returns>A task representing the background loop.</returns>
        private async Task RunFlushLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var timer = new PeriodicTimer(_interval, _timeProvider);

                while (true)
                {
                    bool shouldFlush;

                    try
                    {
                        shouldFlush = await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    if (!shouldFlush)
                    {
                        return;
                    }

                    await Task.Yield();

                    try
                    {
                        FlushWithReentrancyGuard();
                    }
                    catch (PeriodicLoggerFlushException flushException)
                    {
                        RecordProviderFailure(flushException);
                        return;
                    }
                }
            }
            catch (Exception exception)
            {
                // Reached only by failures that did not originate in the wrapped logger, such as timer faults.
                RecordFlushLoopFailure(exception);
            }
        }

        private async Task CompleteDisposalAsync()
        {
            Exception lifecycleFailure = null;
            Exception finalFlushFailure = null;

            try
            {
                await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);
                await _flushLoopTask.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                lifecycleFailure = exception;
            }

            try
            {
                FlushWithReentrancyGuard();
            }
            catch (Exception exception)
            {
                finalFlushFailure = exception;
            }
            finally
            {
                _cancellationTokenSource.Dispose();
            }

            ExceptionDispatchInfo providerFailure;
            ExceptionDispatchInfo flushLoopFailure;

            lock (_syncLock)
            {
                providerFailure = _providerFailure;
                flushLoopFailure = _flushLoopFailure;
            }

            ThrowDisposalFailures(providerFailure, flushLoopFailure, lifecycleFailure, finalFlushFailure);
        }

        private AggregationOutcome TryAggregate(in LogIdentity identity, AggregatedLogEntry entry)
        {
            lock (_syncLock)
            {
                ObjectDisposedException.ThrowIf(_disposeCompletion != null, this);

                if (_aggregationSuspended)
                {
                    return AggregationOutcome.PassThrough;
                }

                if (_entries.TryGetValue(identity, out AggregatedLogEntry existingEntry))
                {
                    existingEntry.Count++;
                    return AggregationOutcome.Aggregated;
                }

                if (entry == null)
                {
                    return AggregationOutcome.StateRequired;
                }

                _entries.Add(identity, entry);
                return AggregationOutcome.Aggregated;
            }
        }

        private void FlushWithReentrancyGuard()
        {
            PeriodicLogger activeFlushLogger = _activeFlushLogger.Value;
            _activeFlushLogger.Value = this;

            try
            {
                Flush();
            }
            finally
            {
                _activeFlushLogger.Value = activeFlushLogger;
            }
        }

        private void RecordProviderFailure(PeriodicLoggerFlushException exception)
        {
            lock (_syncLock)
            {
                _aggregationSuspended = true;
                _providerFailure ??= ExceptionDispatchInfo.Capture(exception);
            }
        }

        private void RecordFlushLoopFailure(Exception exception)
        {
            lock (_syncLock)
            {
                _aggregationSuspended = true;
                _flushLoopFailure ??= ExceptionDispatchInfo.Capture(exception);
            }
        }

        private void Flush()
        {
            AggregatedLogEntry[] batch;

            lock (_syncLock)
            {
                if (_entries.Count == 0)
                {
                    return;
                }

                batch = _entries.Values.ToArray();
                _entries = new Dictionary<LogIdentity, AggregatedLogEntry>();
            }

            for (int index = 0; index < batch.Length; index++)
            {
                AggregatedLogEntry entry = batch[index];
                PeriodicLogState state = entry.State.WithCount(entry.Count);

                try
                {
                    _logger.Log(
                        LogLevel.Information,
                        entry.EventId,
                        state,
                        entry.Exception,
                        static (logState, _) => logState.ToString());
                }
                catch (Exception exception)
                {
                    throw new PeriodicLoggerFlushException(
                        batch.Length - index,
                        CountOccurrences(batch, index),
                        exception);
                }
            }
        }

        private static long CountOccurrences(AggregatedLogEntry[] batch, int startIndex)
        {
            long occurrences = 0;

            for (int index = startIndex; index < batch.Length; index++)
            {
                occurrences += batch[index].Count;
            }

            return occurrences;
        }

        private static void ThrowDisposalFailures(
            ExceptionDispatchInfo providerFailure,
            ExceptionDispatchInfo flushLoopFailure,
            Exception lifecycleFailure,
            Exception finalFlushFailure)
        {
            var failures = new List<Exception>(4);

            if (providerFailure != null)
            {
                failures.Add(providerFailure.SourceException);
            }

            if (flushLoopFailure != null)
            {
                failures.Add(flushLoopFailure.SourceException);
            }

            if (lifecycleFailure != null)
            {
                failures.Add(lifecycleFailure);
            }

            if (finalFlushFailure != null)
            {
                failures.Add(finalFlushFailure);
            }

            switch (failures.Count)
            {
                case 0:
                    return;

                case 1:
                    ExceptionDispatchInfo.Capture(failures[0]).Throw();
                    return;

                default:
                    throw new AggregateException(failures);
            }
        }

        private void ThrowIfDisposingFromFlush()
        {
            if (ReferenceEquals(_activeFlushLogger.Value, this))
            {
                throw new InvalidOperationException("The periodic logger cannot be disposed while it is flushing through the wrapped logger.");
            }
        }

        private void ThrowIfDisposed()
        {
            lock (_syncLock)
            {
                ObjectDisposedException.ThrowIf(_disposeCompletion != null, this);
            }
        }

        private readonly record struct LogIdentity(int EventIdId, string EventIdName, string Message, string ExceptionText);

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

        private sealed class PeriodicLogState : IReadOnlyList<KeyValuePair<string, object>>
        {
            private readonly KeyValuePair<string, object>[] _values;
            private readonly string _renderedMessage;
            private readonly long _count;

            private PeriodicLogState(KeyValuePair<string, object>[] values, string renderedMessage, long count)
            {
                _values = values;
                _renderedMessage = renderedMessage;
                _count = count;
            }

            public int Count => _values.Length + 1;

            public KeyValuePair<string, object> this[int index]
            {
                get
                {
                    if (index < 0 || index > _values.Length)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }

                    return index == _values.Length
                        ? new KeyValuePair<string, object>(OccurrenceCountPropertyName, _count)
                        : _values[index];
                }
            }

            public static PeriodicLogState Create<TState>(TState state, string renderedMessage)
            {
                KeyValuePair<string, object>[] values = state is IEnumerable<KeyValuePair<string, object>> structuredState
                    ? structuredState
                        .Where(pair => !StringComparer.Ordinal.Equals(pair.Key, OccurrenceCountPropertyName))
                        .Select(pair => new KeyValuePair<string, object>(pair.Key, pair.Value))
                        .ToArray()
                    : Array.Empty<KeyValuePair<string, object>>();

                return new PeriodicLogState(values, renderedMessage, 1);
            }

            public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
            {
                foreach (KeyValuePair<string, object> pair in _values)
                {
                    yield return pair;
                }

                yield return new KeyValuePair<string, object>(OccurrenceCountPropertyName, _count);
            }

            public PeriodicLogState WithCount(long count) => new(_values, _renderedMessage, count);

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public override string ToString() => $"{_renderedMessage} Occurrence count: {_count}.";
        }
    }
}
