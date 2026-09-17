// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Health.Fhir.Core.Features.Logging;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Logging
{
    // Categories had no logging or diagnostics value, so Categories.Logging was added rather than reusing
    // Categories.ServiceRuntimeState, which is reserved for FHIR runtime/storage configuration tests.
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Logging)]
    public class PeriodicLoggerTests
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

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
                () => new PeriodicLogger(null, Interval));
        }

        [Fact]
        public async Task GivenWrappedLogger_WhenCheckingContract_ThenDelegates()
        {
            var innerLogger = new RecordingLogger { Enabled = false };
            await using var logger = new PeriodicLogger(innerLogger, Interval, new FakeTimeProvider());
            object scopeState = new();

            IDisposable scope = logger.BeginScope(scopeState);
            bool enabled = logger.IsEnabled(LogLevel.Warning);

            Assert.Same(innerLogger.Scope, scope);
            Assert.Same(scopeState, innerLogger.ScopeState);
            Assert.False(enabled);
            Assert.Equal(LogLevel.Warning, innerLogger.LastEnabledLevel);
        }

        [Fact]
        public async Task GivenDisposedLogger_WhenBeginningScope_ThenStillDelegates()
        {
            var innerLogger = new RecordingLogger();
            var logger = new PeriodicLogger(innerLogger, Interval, new FakeTimeProvider());
            await logger.DisposeAsync();
            object scopeState = new();

            IDisposable scope = logger.BeginScope(scopeState);

            Assert.Same(innerLogger.Scope, scope);
            Assert.Same(scopeState, innerLogger.ScopeState);
        }

        [Fact]
        public async Task GivenDisposedLogger_WhenCheckingEnabledState_ThenStillDelegates()
        {
            var innerLogger = new RecordingLogger { Enabled = false };
            var logger = new PeriodicLogger(innerLogger, Interval, new FakeTimeProvider());
            await logger.DisposeAsync();

            bool enabled = logger.IsEnabled(LogLevel.Information);

            Assert.False(enabled);
            Assert.Equal(LogLevel.Information, innerLogger.LastEnabledLevel);
        }

        [Fact]
        public async Task GivenDisposedLogger_WhenLogging_ThenThrows()
        {
            var logger = new PeriodicLogger(new RecordingLogger(), Interval, new FakeTimeProvider());
            await logger.DisposeAsync();

            Assert.Throws<ObjectDisposedException>(() => logger.LogInformation("late"));
            Assert.Throws<ObjectDisposedException>(() => logger.LogError("late"));
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
            await using var logger = new PeriodicLogger(innerLogger, Interval, new FakeTimeProvider());
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
            await using var logger = new PeriodicLogger(innerLogger, Interval, timeProvider);
            var eventId = new EventId(23, "Periodic");

            logger.LogInformation(eventId, "Queue depth is {QueueDepth}.", 5);
            logger.LogInformation(eventId, "Queue depth is {QueueDepth}.", 5);
            logger.LogInformation(eventId, "Queue depth is {QueueDepth}.", 5);

            Assert.Empty(innerLogger.Records);
            await AdvanceAndWaitAsync(timeProvider, innerLogger, 1);

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
            await using var logger = new PeriodicLogger(innerLogger, Interval, timeProvider);

            logger.LogInformation("single");
            await AdvanceAndWaitAsync(timeProvider, innerLogger, 1);

            RecordedLog record = Assert.Single(innerLogger.Records);
            Assert.Equal("single Occurrence count: 1.", record.Message);
            Assert.Contains(record.State, pair => pair.Key == "OccurrenceCount" && Equals(pair.Value, 1L));
        }

        [Fact]
        public async Task GivenSameTemplateWithDifferentValues_WhenIntervalElapses_ThenWritesDistinctMessages()
        {
            var timeProvider = new FakeTimeProvider();
            var innerLogger = new RecordingLogger();
            await using var logger = new PeriodicLogger(innerLogger, Interval, timeProvider);

            logger.LogInformation("Queue depth is {QueueDepth}.", 5);
            logger.LogInformation("Queue depth is {QueueDepth}.", 6);
            await AdvanceAndWaitAsync(timeProvider, innerLogger, 2);

            Assert.Equal(
                new[] { "Queue depth is 5. Occurrence count: 1.", "Queue depth is 6. Occurrence count: 1." },
                innerLogger.Records.Select(record => record.Message).OrderBy(message => message, StringComparer.Ordinal));
        }

        [Fact]
        public async Task GivenDifferentEventIds_WhenRenderedMessageMatches_ThenWritesDistinctMessages()
        {
            var timeProvider = new FakeTimeProvider();
            var innerLogger = new RecordingLogger();
            await using var logger = new PeriodicLogger(innerLogger, Interval, timeProvider);

            logger.LogInformation(new EventId(1, "First"), "same");
            logger.LogInformation(new EventId(2, "Second"), "same");
            await AdvanceAndWaitAsync(timeProvider, innerLogger, 2);

            Assert.Equal(
                new[] { new EventId(1, "First"), new EventId(2, "Second") },
                innerLogger.Records.Select(record => record.EventId).OrderBy(eventId => eventId.Id));
        }

        [Fact]
        public async Task GivenSameEventIdValueWithDifferentNames_WhenRenderedMessageMatches_ThenWritesDistinctMessages()
        {
            var timeProvider = new FakeTimeProvider();
            var innerLogger = new RecordingLogger();
            await using var logger = new PeriodicLogger(innerLogger, Interval, timeProvider);

            logger.LogInformation(new EventId(7, "First"), "same");
            logger.LogInformation(new EventId(7, "Second"), "same");
            await AdvanceAndWaitAsync(timeProvider, innerLogger, 2);

            Assert.Equal(
                new[] { new EventId(7, "First"), new EventId(7, "Second") },
                innerLogger.Records.Select(record => record.EventId).OrderBy(eventId => eventId.Name, StringComparer.Ordinal));
        }

        [Fact]
        public async Task GivenDifferentExceptionText_WhenRenderedMessageMatches_ThenWritesDistinctMessages()
        {
            var timeProvider = new FakeTimeProvider();
            var innerLogger = new RecordingLogger();
            await using var logger = new PeriodicLogger(innerLogger, Interval, timeProvider);

            logger.LogInformation(new InvalidOperationException("first"), "same");
            logger.LogInformation(new InvalidOperationException("second"), "same");
            await AdvanceAndWaitAsync(timeProvider, innerLogger, 2);

            Assert.Equal(2, innerLogger.Records.Count);
        }

        [Fact]
        public async Task GivenEquivalentExceptionText_WhenMessagesMatch_ThenUsesFirstExceptionInstance()
        {
            var timeProvider = new FakeTimeProvider();
            var innerLogger = new RecordingLogger();
            await using var logger = new PeriodicLogger(innerLogger, Interval, timeProvider);
            var firstException = new InvalidOperationException("same");
            var secondException = new InvalidOperationException("same");

            logger.LogInformation(firstException, "message");
            logger.LogInformation(secondException, "message");
            await AdvanceAndWaitAsync(timeProvider, innerLogger, 1);

            RecordedLog record = Assert.Single(innerLogger.Records);
            Assert.Same(firstException, record.Exception);
            Assert.Equal("message Occurrence count: 2.", record.Message);
        }

        [Fact]
        public async Task GivenSameMessageInDifferentIntervals_WhenFlushed_ThenWritesOncePerInterval()
        {
            var timeProvider = new FakeTimeProvider();
            var innerLogger = new RecordingLogger();
            await using var logger = new PeriodicLogger(innerLogger, Interval, timeProvider);

            logger.LogInformation("message");
            await AdvanceAndWaitAsync(timeProvider, innerLogger, 1);
            logger.LogInformation("message");
            await AdvanceAndWaitAsync(timeProvider, innerLogger, 2);

            Assert.All(innerLogger.Records, record => Assert.Equal("message Occurrence count: 1.", record.Message));
        }

        [Fact]
        public async Task GivenDisabledInformationLevel_WhenIntervalElapsesAndDisposing_ThenNeverReachesWrappedLogger()
        {
            var timeProvider = new FakeTimeProvider();
            var innerLogger = new RecordingLogger { Enabled = false };
            var logger = new PeriodicLogger(innerLogger, Interval, timeProvider);

            logger.LogInformation("disabled");
            timeProvider.Advance(Interval);

            // Disposal cancels the loop, waits for it, and performs the final flush, so an aggregated entry would
            // have to surface here. A single Task.Yield could pass even if the entry had been buffered.
            await logger.DisposeAsync().AsTask().WaitAsync(TestTimeout);

            Assert.Empty(innerLogger.Records);
            Assert.Equal(0, innerLogger.LogAttemptCount);
        }

        [Fact]
        public async Task GivenBufferedInformationMessage_WhenDisposing_ThenFlushesPendingEntry()
        {
            var innerLogger = new RecordingLogger();
            var logger = new PeriodicLogger(innerLogger, Interval, new FakeTimeProvider());

            logger.LogInformation("pending");
            await logger.DisposeAsync().AsTask().WaitAsync(TestTimeout);

            RecordedLog record = Assert.Single(innerLogger.Records);
            Assert.Equal("pending Occurrence count: 1.", record.Message);
        }

        [Fact]
        public void GivenPendingMessage_WhenDisposed_ThenFlushesFinalInterval()
        {
            var innerLogger = new RecordingLogger();
            var logger = new PeriodicLogger(innerLogger, Interval, new FakeTimeProvider());
            logger.LogInformation("pending");

            logger.Dispose();

            Assert.Equal("pending Occurrence count: 1.", Assert.Single(innerLogger.Records).Message);
        }

        [Fact]
        public async Task GivenConcurrentDisposal_WhenCalledMultipleTimes_ThenFlushesOnlyOnce()
        {
            var innerLogger = new RecordingLogger();
            var logger = new PeriodicLogger(innerLogger, Interval, new FakeTimeProvider());
            logger.LogInformation("pending");

            await Task.WhenAll(
                logger.DisposeAsync().AsTask(),
                logger.DisposeAsync().AsTask(),
                Task.Run(logger.Dispose)).WaitAsync(TestTimeout);

            Assert.Single(innerLogger.Records);
        }

        [Fact]
        public async Task GivenConcurrentIdenticalInformationMessages_WhenFlushed_ThenCountsEveryOccurrence()
        {
            const int callerCount = 4;
            const int callsPerCaller = 500;
            const long expectedCount = callerCount * callsPerCaller;

            var innerLogger = new RecordingLogger();
            var logger = new PeriodicLogger(innerLogger, Interval, new FakeTimeProvider());
            using var startBarrier = new Barrier(callerCount);

            Task[] callers = Enumerable.Range(0, callerCount)
                .Select(_ => Task.Factory.StartNew(
                    () =>
                    {
                        startBarrier.SignalAndWait();

                        for (int call = 0; call < callsPerCaller; call++)
                        {
                            logger.LogInformation("concurrent");
                        }
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default))
                .ToArray();

            await Task.WhenAll(callers).WaitAsync(TestTimeout);
            await logger.DisposeAsync().AsTask().WaitAsync(TestTimeout);

            RecordedLog record = Assert.Single(innerLogger.Records);
            Assert.Equal($"concurrent Occurrence count: {expectedCount}.", record.Message);
            Assert.Contains(record.State, pair => pair.Key == "OccurrenceCount" && Equals(pair.Value, expectedCount));
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
                releaseEmission.Task.WaitAsync(TestTimeout).GetAwaiter().GetResult();
            };
            await using var logger = new PeriodicLogger(innerLogger, Interval, timeProvider);
            logger.LogInformation("message");

            timeProvider.Advance(Interval);
            await emissionStarted.Task.WaitAsync(TestTimeout);
            logger.LogInformation("message");
            releaseEmission.SetResult();
            await innerLogger.WaitForRecordCountAsync(1).WaitAsync(TestTimeout);

            Assert.Equal("message Occurrence count: 1.", Assert.Single(innerLogger.Records).Message);

            innerLogger.OnLog = null;
            await AdvanceAndWaitAsync(timeProvider, innerLogger, 2);

            Assert.All(innerLogger.Records, record => Assert.Equal("message Occurrence count: 1.", record.Message));
        }

        [Fact]
        public async Task GivenProviderFailure_WhenPeriodicFlushRuns_ThenLaterCallsDegradeToImmediatePassthrough()
        {
            var timeProvider = new FakeTimeProvider();
            var providerException = new InvalidOperationException("provider failed");
            var innerLogger = new RecordingLogger { ExceptionToThrow = providerException };
            var logger = new PeriodicLogger(innerLogger, Interval, timeProvider);
            logger.LogInformation("message");

            timeProvider.Advance(Interval);
            await logger.FlushLoopTask.WaitAsync(TestTimeout);
            innerLogger.ExceptionToThrow = null;
            object scopeState = new();

            bool enabled = logger.IsEnabled(LogLevel.Information);
            IDisposable scope = logger.BeginScope(scopeState);
            logger.LogError("error");
            logger.LogInformation("later");

            Assert.True(enabled);
            Assert.Same(innerLogger.Scope, scope);
            Assert.Same(scopeState, innerLogger.ScopeState);

            // Information is written straight through, so it carries no aggregated occurrence suffix.
            Assert.Equal(new[] { "error", "later" }, innerLogger.Records.Select(record => record.Message));
            Assert.Equal(
                new[] { LogLevel.Error, LogLevel.Information },
                innerLogger.Records.Select(record => record.Level));

            PeriodicLoggerFlushException flushException = await ThrowsDisposalFailureAsync<PeriodicLoggerFlushException>(logger);
            Assert.Same(providerException, flushException.InnerException);
            Assert.Equal(1, flushException.DiscardedEntryCount);
            Assert.Equal(1L, flushException.DiscardedOccurrenceCount);
        }

        [Fact]
        public async Task GivenProviderFailurePartwayThroughBatch_WhenFlushRuns_ThenReportsDiscardedEntryAndOccurrenceCounts()
        {
            var timeProvider = new FakeTimeProvider();
            var providerException = new InvalidOperationException("provider failed");
            var innerLogger = new RecordingLogger { ExceptionForAttempt = attempt => attempt >= 2 ? providerException : null };
            var logger = new PeriodicLogger(innerLogger, Interval, timeProvider);

            foreach (string message in new[] { "first", "second", "third" })
            {
                logger.LogInformation(message);
                logger.LogInformation(message);
            }

            timeProvider.Advance(Interval);
            await logger.FlushLoopTask.WaitAsync(TestTimeout);

            Assert.Single(innerLogger.Records);

            PeriodicLoggerFlushException flushException = await ThrowsDisposalFailureAsync<PeriodicLoggerFlushException>(logger);
            Assert.Same(providerException, flushException.InnerException);
            Assert.Equal(2, flushException.DiscardedEntryCount);
            Assert.Equal(4L, flushException.DiscardedOccurrenceCount);
            Assert.Contains("2 aggregated entries representing 4 occurrences", flushException.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GivenFailedDetachedBatch_WhenTimeAdvancesAndDisposing_ThenBatchIsNotRetried()
        {
            var timeProvider = new FakeTimeProvider();
            var providerException = new InvalidOperationException("provider failed");
            var innerLogger = new RecordingLogger { ExceptionToThrow = providerException };
            var logger = new PeriodicLogger(innerLogger, Interval, timeProvider);
            logger.LogInformation("message");
            logger.LogInformation("message");

            timeProvider.Advance(Interval);
            await logger.FlushLoopTask.WaitAsync(TestTimeout);

            Assert.Equal(1, innerLogger.LogAttemptCount);

            innerLogger.ExceptionToThrow = null;
            timeProvider.Advance(TimeSpan.FromMinutes(5));

            PeriodicLoggerFlushException flushException = await ThrowsDisposalFailureAsync<PeriodicLoggerFlushException>(logger);

            Assert.Equal(1, flushException.DiscardedEntryCount);
            Assert.Equal(2L, flushException.DiscardedOccurrenceCount);
            Assert.Equal(1, innerLogger.LogAttemptCount);
            Assert.Empty(innerLogger.Records);
        }

        [Fact]
        public async Task GivenPeriodicAndFinalFlushFailures_WhenDisposing_ThenBothAreSurfaced()
        {
            var timeProvider = new FakeTimeProvider();
            var providerException = new InvalidOperationException("provider failed");
            var emissionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseEmission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var innerLogger = new RecordingLogger { ExceptionToThrow = providerException };
            innerLogger.OnLog = () =>
            {
                emissionStarted.TrySetResult();
                releaseEmission.Task.WaitAsync(TestTimeout).GetAwaiter().GetResult();
            };
            var logger = new PeriodicLogger(innerLogger, Interval, timeProvider);
            logger.LogInformation("first");

            timeProvider.Advance(Interval);
            await emissionStarted.Task.WaitAsync(TestTimeout);

            // The dictionary has already been swapped, so this entry belongs to the next interval and survives the
            // failed batch. It can only be emitted by the final flush during disposal.
            logger.LogInformation("second");
            releaseEmission.SetResult();
            await logger.FlushLoopTask.WaitAsync(TestTimeout);

            AggregateException disposeException = await ThrowsDisposalFailureAsync<AggregateException>(logger);

            Assert.Equal(2, disposeException.InnerExceptions.Count);
            Assert.All(
                disposeException.InnerExceptions,
                exception => Assert.Same(providerException, Assert.IsType<PeriodicLoggerFlushException>(exception).InnerException));
            Assert.Equal(new[] { "first Occurrence count: 1.", "second Occurrence count: 1." }, innerLogger.AttemptedMessages);
        }

        [Fact]
        public async Task GivenFailedDisposal_WhenDisposedAgain_ThenSurfacesTheSameFailureWithoutReflushing()
        {
            var timeProvider = new FakeTimeProvider();
            var providerException = new InvalidOperationException("provider failed");
            var innerLogger = new RecordingLogger { ExceptionToThrow = providerException };
            var logger = new PeriodicLogger(innerLogger, Interval, timeProvider);
            logger.LogInformation("message");

            timeProvider.Advance(Interval);
            await logger.FlushLoopTask.WaitAsync(TestTimeout);

            PeriodicLoggerFlushException first = await ThrowsDisposalFailureAsync<PeriodicLoggerFlushException>(logger);
            PeriodicLoggerFlushException second = await ThrowsDisposalFailureAsync<PeriodicLoggerFlushException>(logger);

            Assert.Same(first, second);
            Assert.Same(providerException, second.InnerException);
            Assert.Equal(1, innerLogger.LogAttemptCount);
        }

        [Fact]
        public async Task GivenProviderCancellationFailureDuringPeriodicFlush_WhenDisposing_ThenDisposalSurfacesFailure()
        {
            var timeProvider = new FakeTimeProvider();
            var providerException = new OperationCanceledException("provider canceled");
            var innerLogger = new RecordingLogger { ExceptionToThrow = providerException };
            var emissionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseEmission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            innerLogger.OnLog = () =>
            {
                emissionStarted.TrySetResult();
                releaseEmission.Task.WaitAsync(TestTimeout).GetAwaiter().GetResult();
            };
            var logger = new PeriodicLogger(innerLogger, Interval, timeProvider);
            logger.LogInformation("message");

            timeProvider.Advance(Interval);
            await emissionStarted.Task.WaitAsync(TestTimeout);
            Task disposeTask = logger.DisposeAsync().AsTask();
            releaseEmission.SetResult();

            Exception disposeException = await Assert.ThrowsAnyAsync<Exception>(() => disposeTask.WaitAsync(TestTimeout));

            // A cancellation thrown by the wrapped provider must not be mistaken for loop cancellation.
            PeriodicLoggerFlushException flushException = Assert.IsType<PeriodicLoggerFlushException>(disposeException);
            Assert.Same(providerException, flushException.InnerException);
        }

        [Fact]
        public async Task GivenWrappedLoggerDisposesPeriodicLoggerDuringFlush_WhenFlushRuns_ThenThrowsInsteadOfDeadlocking()
        {
            var timeProvider = new FakeTimeProvider();
            var innerLogger = new RecordingLogger();
            var disposeExceptionSource = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            PeriodicLogger logger = null;
            innerLogger.OnLog = () =>
            {
                try
                {
                    logger.Dispose();
                }
                catch (Exception exception)
                {
                    disposeExceptionSource.TrySetResult(exception);
                }
            };
            logger = new PeriodicLogger(innerLogger, Interval, timeProvider);
            logger.LogInformation("message");

            timeProvider.Advance(Interval);

            Exception disposeException = await disposeExceptionSource.Task.WaitAsync(TestTimeout);
            Assert.IsType<InvalidOperationException>(disposeException);

            innerLogger.OnLog = null;
            await logger.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }

        [Fact]
        public async Task GivenCallerSuppliedOccurrenceCount_WhenFlushed_ThenReplacedByOneAuthoritativeCount()
        {
            var timeProvider = new FakeTimeProvider();
            var innerLogger = new RecordingLogger();
            await using var logger = new PeriodicLogger(innerLogger, Interval, timeProvider);

            logger.LogInformation("Reported {OccurrenceCount} times.", 99);
            logger.LogInformation("Reported {OccurrenceCount} times.", 99);
            await AdvanceAndWaitAsync(timeProvider, innerLogger, 1);

            RecordedLog record = Assert.Single(innerLogger.Records);
            KeyValuePair<string, object>[] occurrenceCounts = record.State
                .Where(pair => pair.Key == "OccurrenceCount")
                .ToArray();

            Assert.Single(occurrenceCounts);
            Assert.Equal(2L, occurrenceCounts[0].Value);
            Assert.DoesNotContain(record.State, pair => Equals(pair.Value, 99));
        }

        [Fact]
        public async Task GivenStructuredState_WhenFlushed_ThenExposesOriginalPairsInOrderFollowedByOccurrenceCount()
        {
            var timeProvider = new FakeTimeProvider();
            var innerLogger = new RecordingLogger();
            await using var logger = new PeriodicLogger(innerLogger, Interval, timeProvider);

            logger.LogInformation("Queue {QueueName} depth is {QueueDepth}.", "import", 5);
            await AdvanceAndWaitAsync(timeProvider, innerLogger, 1);

            IReadOnlyList<KeyValuePair<string, object>> state = GetAggregatedState(Assert.Single(innerLogger.Records));

            Assert.Equal(4, state.Count);
            Assert.Equal(
                new[] { "QueueName", "QueueDepth", "{OriginalFormat}", "OccurrenceCount" },
                state.Select(pair => pair.Key));
            Assert.Equal("import", state[0].Value);
            Assert.Equal(5, state[1].Value);
            Assert.Equal("Queue {QueueName} depth is {QueueDepth}.", state[2].Value);
            Assert.Equal(1L, state[3].Value);

            // The enumerator and the indexer must agree.
            Assert.Equal(
                Enumerable.Range(0, state.Count).Select(index => state[index]),
                state);
        }

        [Fact]
        public async Task GivenAggregatedState_WhenIndexIsOutOfRange_ThenThrowsArgumentOutOfRangeException()
        {
            var timeProvider = new FakeTimeProvider();
            var innerLogger = new RecordingLogger();
            await using var logger = new PeriodicLogger(innerLogger, Interval, timeProvider);

            logger.LogInformation("Queue depth is {QueueDepth}.", 5);
            await AdvanceAndWaitAsync(timeProvider, innerLogger, 1);

            IReadOnlyList<KeyValuePair<string, object>> state = GetAggregatedState(Assert.Single(innerLogger.Records));

            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = state[-1]; });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = state[int.MinValue]; });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = state[state.Count]; });
        }

        [Fact]
        public async Task GivenNonStructuredState_WhenFlushed_ThenExposesOnlyOccurrenceCount()
        {
            var timeProvider = new FakeTimeProvider();
            var innerLogger = new RecordingLogger();
            await using var logger = new PeriodicLogger(innerLogger, Interval, timeProvider);

            logger.Log(LogLevel.Information, new EventId(3, "Plain"), "plain state", null, static (state, _) => state);
            await AdvanceAndWaitAsync(timeProvider, innerLogger, 1);

            RecordedLog record = Assert.Single(innerLogger.Records);
            IReadOnlyList<KeyValuePair<string, object>> state = GetAggregatedState(record);

            Assert.Single(state);
            Assert.Equal("OccurrenceCount", state[0].Key);
            Assert.Equal(1L, state[0].Value);
            Assert.Equal("plain state Occurrence count: 1.", record.Message);
        }

        private static IReadOnlyList<KeyValuePair<string, object>> GetAggregatedState(RecordedLog record)
            => Assert.IsAssignableFrom<IReadOnlyList<KeyValuePair<string, object>>>(record.RawState);

        private static async Task<TException> ThrowsDisposalFailureAsync<TException>(PeriodicLogger logger)
            where TException : Exception
        {
            Exception exception = await Assert.ThrowsAnyAsync<Exception>(
                () => logger.DisposeAsync().AsTask().WaitAsync(TestTimeout));

            return Assert.IsType<TException>(exception);
        }

        private static async Task AdvanceAndWaitAsync(
            FakeTimeProvider timeProvider,
            RecordingLogger logger,
            int expectedCount)
        {
            timeProvider.Advance(Interval);
            await logger.WaitForRecordCountAsync(expectedCount).WaitAsync(TestTimeout);
        }

        private sealed class RecordingLogger : ILogger
        {
            private readonly object _syncLock = new();
            private readonly List<RecordedLog> _records = new();
            private readonly List<string> _attemptedMessages = new();
            private readonly List<RecordCountWaiter> _waiters = new();
            private int _logAttemptCount;

            public IReadOnlyList<RecordedLog> Records
            {
                get
                {
                    lock (_syncLock)
                    {
                        return _records.ToArray();
                    }
                }
            }

            public IReadOnlyList<string> AttemptedMessages
            {
                get
                {
                    lock (_syncLock)
                    {
                        return _attemptedMessages.ToArray();
                    }
                }
            }

            public int LogAttemptCount
            {
                get
                {
                    lock (_syncLock)
                    {
                        return _logAttemptCount;
                    }
                }
            }

            public IDisposable Scope { get; } = new TestScope();

            public object ScopeState { get; private set; }

            public LogLevel LastEnabledLevel { get; private set; }

            public bool Enabled { get; set; } = true;

            public Action OnLog { get; set; }

            public Exception ExceptionToThrow { get; set; }

            public Func<int, Exception> ExceptionForAttempt { get; set; }

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
                    if (_records.Count >= expectedCount)
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

                string message = formatter(state, exception);
                int attempt;

                lock (_syncLock)
                {
                    attempt = ++_logAttemptCount;
                    _attemptedMessages.Add(message);
                }

                OnLog?.Invoke();

                Exception exceptionToThrow = ExceptionToThrow ?? ExceptionForAttempt?.Invoke(attempt);

                if (exceptionToThrow != null)
                {
                    throw exceptionToThrow;
                }

                IReadOnlyList<KeyValuePair<string, object>> structuredState = state is IEnumerable<KeyValuePair<string, object>> structuredStateValues
                    ? structuredStateValues.Select(item => new KeyValuePair<string, object>(item.Key, item.Value)).ToArray()
                    : null;

                List<RecordCountWaiter> completedWaiters;

                lock (_syncLock)
                {
                    _records.Add(new RecordedLog(logLevel, eventId, exception, message, structuredState, state));
                    completedWaiters = GetSatisfiedWaiters();
                }

                CompleteWaiters(completedWaiters);
            }

            private List<RecordCountWaiter> GetSatisfiedWaiters()
            {
                List<RecordCountWaiter> completedWaiters = null;

                for (int i = _waiters.Count - 1; i >= 0; i--)
                {
                    RecordCountWaiter waiter = _waiters[i];

                    if (_records.Count >= waiter.ExpectedCount)
                    {
                        completedWaiters ??= new List<RecordCountWaiter>();
                        completedWaiters.Add(waiter);
                        _waiters.RemoveAt(i);
                    }
                }

                return completedWaiters;
            }

            private static void CompleteWaiters(List<RecordCountWaiter> completedWaiters)
            {
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
            IReadOnlyList<KeyValuePair<string, object>> State,
            object RawState);

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
