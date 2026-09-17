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
    /// Wraps an <see cref="ILogger"/> and introduces a periodic flush loop for later batching behavior.
    /// </summary>
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
        private ExceptionDispatchInfo _providerFailure;

        /// <summary>
        /// Initializes a new instance of the <see cref="PeriodicLogger"/> class.
        /// </summary>
        /// <param name="logger">The wrapped logger.</param>
        /// <param name="interval">The flush interval.</param>
        /// <param name="timeProvider">The time provider used to drive the periodic timer.</param>
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

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            lock (_syncLock)
            {
                ThrowIfUnavailable();
            }

            return _logger.BeginScope(state);
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel)
        {
            lock (_syncLock)
            {
                ThrowIfUnavailable();
            }

            return _logger.IsEnabled(logLevel);
        }

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            lock (_syncLock)
            {
                ThrowIfUnavailable();
            }

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

            string message = formatter(state, exception);
            var identity = new LogIdentity(eventId.Id, eventId.Name, message, exception?.ToString());

            lock (_syncLock)
            {
                ThrowIfUnavailable();

                if (_entries.TryGetValue(identity, out AggregatedLogEntry existingEntry))
                {
                    existingEntry.Count++;
                }
                else
                {
                    PeriodicLogState periodicState = PeriodicLogState.Create(state, message);
                    _entries.Add(identity, new AggregatedLogEntry(eventId, exception, periodicState));
                }
            }
        }

        /// <summary>
        /// Disposes the logger synchronously.
        /// </summary>
        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        /// <summary>
        /// Disposes the logger asynchronously.
        /// </summary>
        /// <returns>A task that completes when disposal is finished.</returns>
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
                throw;
            }
        }

        /// <summary>
        /// Runs the periodic flush loop until cancellation is requested.
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
                    FlushWithReentrancyGuard();
                }
            }
            catch (Exception exception)
            {
                RecordProviderFailure(exception);
            }
        }

        private async Task CompleteDisposalAsync()
        {
            Exception finalFlushFailure = null;

            await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);
            await _flushLoopTask.ConfigureAwait(false);

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

            lock (_syncLock)
            {
                providerFailure = _providerFailure;
            }

            ThrowDisposalFailure(providerFailure, finalFlushFailure);
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

        private void RecordProviderFailure(Exception exception)
        {
            lock (_syncLock)
            {
                _providerFailure ??= ExceptionDispatchInfo.Capture(exception);
            }
        }

        private void Flush()
        {
            Dictionary<LogIdentity, AggregatedLogEntry> entries;

            lock (_syncLock)
            {
                if (_entries.Count == 0)
                {
                    return;
                }

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

        private void ThrowIfDisposingFromFlush()
        {
            if (ReferenceEquals(_activeFlushLogger.Value, this))
            {
                throw new InvalidOperationException("The periodic logger cannot be disposed while it is flushing through the wrapped logger.");
            }
        }

        private void ThrowIfUnavailable()
        {
            ObjectDisposedException.ThrowIf(_disposeCompletion != null, this);

            if (_providerFailure != null)
            {
                throw new InvalidOperationException(
                    "The periodic logger stopped because the wrapped logger failed.",
                    _providerFailure.SourceException);
            }
        }

        private static void ThrowDisposalFailure(ExceptionDispatchInfo providerFailure, Exception finalFlushFailure)
        {
            if (providerFailure != null && finalFlushFailure != null)
            {
                throw new AggregateException(providerFailure.SourceException, finalFlushFailure);
            }

            providerFailure?.Throw();

            if (finalFlushFailure != null)
            {
                ExceptionDispatchInfo.Capture(finalFlushFailure).Throw();
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
                    if (index < _values.Length)
                    {
                        return _values[index];
                    }

                    if (index == _values.Length)
                    {
                        return new KeyValuePair<string, object>(OccurrenceCountPropertyName, _count);
                    }

                    throw new ArgumentOutOfRangeException(nameof(index));
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
