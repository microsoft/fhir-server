// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Health.Fhir.Core.Features.Logging;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Logging
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    public class PeriodicLoggerTests
    {
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

        [Fact]
        public async Task GivenDisposedLogger_WhenBeginningScope_ThenThrows()
        {
            var innerLogger = new RecordingLogger();
            var logger = new PeriodicLogger(innerLogger, TimeSpan.FromMinutes(1));

            await logger.DisposeAsync();

            Assert.Throws<ObjectDisposedException>(() => logger.BeginScope(new object()));
        }

        [Fact]
        public async Task GivenDisposedLogger_WhenCheckingEnabledState_ThenThrows()
        {
            var innerLogger = new RecordingLogger();
            var logger = new PeriodicLogger(innerLogger, TimeSpan.FromMinutes(1));

            await logger.DisposeAsync();

            Assert.Throws<ObjectDisposedException>(() => logger.IsEnabled(LogLevel.Information));
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
        public async Task GivenSameEventIdValueWithDifferentNames_WhenRenderedMessageMatches_ThenWritesDistinctMessages()
        {
            var timeProvider = new FakeTimeProvider();
            var innerLogger = new RecordingLogger();
            await using var logger = new PeriodicLogger(innerLogger, TimeSpan.FromMinutes(1), timeProvider);

            logger.LogInformation(new EventId(7, "First"), "same");
            logger.LogInformation(new EventId(7, "Second"), "same");
            await AdvanceAndWaitAsync(timeProvider, innerLogger, TimeSpan.FromMinutes(1), 2);

            Assert.Equal(
                new[] { new EventId(7, "First"), new EventId(7, "Second") },
                innerLogger.Records.Select(record => record.EventId).OrderBy(eventId => eventId.Name));
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

        [Fact]
        public async Task GivenBufferedInformationMessage_WhenDisposing_ThenFlushesPendingEntry()
        {
            var timeProvider = new FakeTimeProvider();
            var innerLogger = new RecordingLogger();
            var logger = new PeriodicLogger(innerLogger, TimeSpan.FromMinutes(1), timeProvider);

            logger.LogInformation("pending");
            await logger.DisposeAsync();

            RecordedLog record = Assert.Single(innerLogger.Records);
            Assert.Equal("pending Occurrence count: 1.", record.Message);
        }

        private static async Task AdvanceAndWaitAsync(
            FakeTimeProvider timeProvider,
            RecordingLogger logger,
            TimeSpan interval,
            int expectedCount)
        {
            timeProvider.Advance(interval);
            await logger.WaitForRecordCountAsync(expectedCount);
        }

        private sealed class RecordingLogger : ILogger
        {
            private readonly object _syncLock = new();
            private readonly List<RecordCountWaiter> _waiters = new();

            public ConcurrentQueue<RecordedLog> Records { get; } = new();

            public IDisposable Scope { get; } = new TestScope();

            public object ScopeState { get; private set; }

            public LogLevel LastEnabledLevel { get; private set; }

            public bool Enabled { get; set; } = true;

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
            {
                ScopeState = state;
                return Scope;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                LastEnabledLevel = logLevel;
                return Enabled;
            }

            public Task WaitForRecordCountAsync(int expectedCount)
            {
                lock (_syncLock)
                {
                    if (Records.Count >= expectedCount)
                    {
                        return Task.CompletedTask;
                    }

                    var waiter = new RecordCountWaiter(expectedCount);
                    _waiters.Add(waiter);
                    return waiter.Task;
                }
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                ArgumentNullException.ThrowIfNull(formatter);

                IReadOnlyList<KeyValuePair<string, object>> structuredState = state is IEnumerable<KeyValuePair<string, object>> structuredStateValues
                    ? structuredStateValues.Select(item => new KeyValuePair<string, object>(item.Key, item.Value)).ToArray()
                    : null;

                Records.Enqueue(new RecordedLog(logLevel, eventId, exception, formatter(state, exception), structuredState));
                CompleteSatisfiedWaiters();
            }

            private void CompleteSatisfiedWaiters()
            {
                List<RecordCountWaiter> completedWaiters = null;

                lock (_syncLock)
                {
                    for (int i = _waiters.Count - 1; i >= 0; i--)
                    {
                        RecordCountWaiter waiter = _waiters[i];

                        if (Records.Count >= waiter.ExpectedCount)
                        {
                            completedWaiters ??= new List<RecordCountWaiter>();
                            completedWaiters.Add(waiter);
                            _waiters.RemoveAt(i);
                        }
                    }
                }

                if (completedWaiters is null)
                {
                    return;
                }

                foreach (RecordCountWaiter waiter in completedWaiters)
                {
                    waiter.TrySetCompleted();
                }
            }
        }

        private sealed record RecordedLog(
            LogLevel Level,
            EventId EventId,
            Exception Exception,
            string Message,
            IReadOnlyList<KeyValuePair<string, object>> State);

        private sealed class RecordCountWaiter
        {
            private readonly TaskCompletionSource<bool> _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public RecordCountWaiter(int expectedCount)
            {
                ExpectedCount = expectedCount;
            }

            public int ExpectedCount { get; }

            public Task Task => _completionSource.Task;

            public void TrySetCompleted() => _completionSource.TrySetResult(true);
        }

        private sealed class TestScope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
