// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// Test case that implements retry logic.
    /// </summary>
    public sealed class RetryTestCase : XunitTestCase, ISelfExecutingXunitTestCase
    {
        private int _maxRetries;
        private int _delayMs;
        private bool _retryOnAssertionFailure;

        /// <summary>
        /// Initializes a new instance of the <see cref="RetryTestCase"/> class. Used only by the deserializer.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
        public RetryTestCase()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RetryTestCase"/> class.
        /// </summary>
        /// <param name="testMethod">The test method this test case belongs to.</param>
        /// <param name="displayName">The display name reported for the test case.</param>
        /// <param name="uniqueId">The unique ID of the test case.</param>
        /// <param name="explicit">Whether the test case is only run when explicitly selected.</param>
        /// <param name="skipExceptions">Exception types that cause the test to be reported as skipped rather than failed.</param>
        /// <param name="skipReason">The static reason the test is skipped, or <c>null</c> if it is not skipped.</param>
        /// <param name="skipType">The type containing the member named by <paramref name="skipUnless"/> or <paramref name="skipWhen"/>.</param>
        /// <param name="skipUnless">The name of a property that must be <c>true</c> for the test to run.</param>
        /// <param name="skipWhen">The name of a property that must be <c>false</c> for the test to run.</param>
        /// <param name="traits">The traits associated with the test case.</param>
        /// <param name="testMethodArguments">The arguments passed to the test method.</param>
        /// <param name="sourceFile">The source file containing the test method.</param>
        /// <param name="sourceLine">The line number of the test method.</param>
        /// <param name="timeout">The per-attempt timeout in milliseconds, or <c>null</c> for none.</param>
        /// <param name="maxRetries">The maximum number of attempts. Values below one are clamped to one.</param>
        /// <param name="delayMs">The delay in milliseconds between attempts.</param>
        /// <param name="retryOnAssertionFailure">Whether assertion failures should be retried, rather than only non-assertion exceptions.</param>
        public RetryTestCase(
            IXunitTestMethod testMethod,
            string displayName,
            string uniqueId,
            bool @explicit,
            Type[] skipExceptions,
            string skipReason,
            Type skipType,
            string skipUnless,
            string skipWhen,
            Dictionary<string, HashSet<string>> traits,
            object[] testMethodArguments,
            string sourceFile,
            int? sourceLine,
            int? timeout,
            int maxRetries,
            int delayMs,
            bool retryOnAssertionFailure)
            : base(testMethod, displayName, uniqueId, @explicit, skipExceptions, skipReason, skipType, skipUnless, skipWhen, traits, testMethodArguments, sourceFile, sourceLine, timeout)
        {
            // A retry count below one would skip the attempt loop entirely and report no result
            // at all, silently dropping the test. Always run at least one attempt.
            _maxRetries = Math.Max(1, maxRetries);

            // A negative delay below -1 makes Task.Delay throw, turning a configuration slip into
            // an unrelated crash inside the retry loop.
            _delayMs = Math.Max(0, delayMs);
            _retryOnAssertionFailure = retryOnAssertionFailure;
        }

        /// <summary>
        /// Runs the test case, retrying transient failures up to the configured number of attempts.
        /// </summary>
        /// <param name="explicitOption">Whether explicit tests should be run.</param>
        /// <param name="messageBus">The message bus that test results are reported to.</param>
        /// <param name="constructorArguments">The arguments to pass to the test class constructor.</param>
        /// <param name="aggregator">The exception aggregator for the containing test class.</param>
        /// <param name="cancellationTokenSource">The cancellation token source for the test run.</param>
        /// <returns>A summary of the run. Reporters derive their results from <paramref name="messageBus"/>
        /// rather than from this summary, so every outcome must also be reported as a message.</returns>
        public async ValueTask<RunSummary> Run(
            ExplicitOption explicitOption,
            IMessageBus messageBus,
            object[] constructorArguments,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
        {
            // Trace output is only visible to an attached debugger, never in CI logs. Anything
            // that needs to show up in a pipeline log has to go to Console instead.
            Trace.WriteLine($"RetryFact starting test '{TestMethod.TestClass.TestClassName}.{TestMethod.MethodName}' with MaxRetries={_maxRetries}, DelayMs={_delayMs}, RetryOnAssertionFailure={_retryOnAssertionFailure}");

            var runSummary = new RunSummary { Total = 1 };
            Exception lastException = null;

            // Holds the deferred failure of an attempt that is about to be retried. It is discarded
            // only once a later attempt has actually reported a result, so that a run cancelled
            // between attempts still reports the failure that was already observed instead of
            // silently dropping the test.
            FailureInterceptingMessageBus pendingFailureBus = null;

            for (int attempt = 1; attempt <= _maxRetries; attempt++)
            {
                var isLastAttempt = attempt == _maxRetries;

                Trace.WriteLine($"RetryFact attempt {attempt}/{_maxRetries} for test '{TestMethod.MethodName}'");

                // Create a fresh aggregator for each attempt
                var attemptAggregator = new ExceptionAggregator();

                FailureInterceptingMessageBus interceptingBus = null;

                try
                {
                    // Always wrap the bus so the failure details are available for the CI log, but only
                    // defer (hide) a failure while a further attempt could still supersede it.
                    interceptingBus = new FailureInterceptingMessageBus(messageBus, deferFailures: !isLastAttempt);

                    var summary = await XunitRunnerHelper.RunXunitTestCase(
                        this,
                        interceptingBus,
                        cancellationTokenSource,
                        attemptAggregator,
                        explicitOption,
                        constructorArguments);

                    runSummary.Time = summary.Time;

                    // A cancelled run short-circuits and reports nothing, which is not the same as
                    // passing. Treating an empty summary as success would drop the test from the
                    // results entirely, taking any failure already seen on an earlier attempt with it.
                    //
                    // An empty summary is the only reliable signal that the attempt reported
                    // nothing. Cancellation on its own is not: an attempt that ran to completion
                    // while cancellation was requested has already reported its own result through
                    // the bus, and replaying an earlier attempt's deferred failure on top of that
                    // adds a second, orphaned result for a test that actually finished.
                    if (summary.Total == 0)
                    {
                        bool hasObservedFailure = (pendingFailureBus?.HasDeferredFailure ?? false) || interceptingBus.HasDeferredFailure;

                        Console.WriteLine($"[RetryFact] Test '{TestMethod.TestClass.TestClassName}.{TestMethod.MethodName}' reported no result for attempt {attempt}/{_maxRetries} (the run was cancelled or aborted). Reporting the last observed result rather than a pass.");

                        pendingFailureBus?.ReplayDeferredMessages();
                        interceptingBus.ReplayDeferredMessages();

                        if (lastException != null)
                        {
                            aggregator.Add(lastException);
                        }

                        runSummary.Failed = hasObservedFailure ? 1 : 0;
                        return runSummary;
                    }

                    // This attempt reported a real result, so a failure held over from an earlier
                    // attempt has been superseded and must not also be reported.
                    pendingFailureBus?.DiscardDeferredMessages();

                    if (summary.Failed == 0)
                    {
                        // Test passed - success message already went through to Test Explorer
                        Trace.WriteLine($"RetryFact test '{TestMethod.TestClass.TestClassName}.{TestMethod.MethodName}' passed on attempt {attempt}/{_maxRetries}");
                        messageBus.QueueMessage(
                            new DiagnosticMessage($"[RetryFact] Test '{TestMethod.TestClass.TestClassName}.{TestMethod.MethodName}' passed on attempt {attempt}/{_maxRetries}"));

                        runSummary.Failed = 0;
                        return runSummary;
                    }

                    // Test failed on this attempt
                    lastException = attemptAggregator.ToException();

                    // If no exception was captured but test failed, create an exception using captured failure details
                    if (lastException == null && summary.Failed > 0)
                    {
                        string failureMsg = interceptingBus.LastFailureMessage ?? "Test failed but no exception was captured.";
                        string stackTrace = interceptingBus.LastFailureStackTrace;
                        bool isAssertionFailure = interceptingBus.IsAssertionFailure;

                        string fullMessage = failureMsg +
                            (stackTrace != null ? Environment.NewLine + "Stack Trace:" + Environment.NewLine + stackTrace : string.Empty);

                        // If this is an assertion failure (based on exception types), create an XunitException
                        // so that RetryOnAssertionFailure logic works correctly
                        if (isAssertionFailure)
                        {
                            lastException = new XunitException(fullMessage);
                        }
                        else
                        {
                            lastException = new InvalidOperationException(fullMessage);
                        }

                        Trace.WriteLine($"RetryFact: Test failed but exception is null, created placeholder exception (IsAssertion={isAssertionFailure})");
                    }

                    Trace.WriteLine($"RetryFact test failed on attempt {attempt} with exception type: {lastException?.GetType().FullName ?? "null"}, Message: {lastException?.Message ?? "null"}");

                    if (!isLastAttempt)
                    {
                        // Check if we should retry this exception (now handles null)
                        var shouldRetry = ShouldRetry(lastException);
                        Trace.WriteLine($"RetryFact ShouldRetry={shouldRetry} for exception type {lastException?.GetType().FullName ?? "null"}");

                        if (!shouldRetry)
                        {
                            Console.WriteLine($"[RetryFact] Test '{TestMethod.TestClass.TestClassName}.{TestMethod.MethodName}' failed with a non-retriable exception on attempt {attempt}/{_maxRetries}. Skipping remaining retries.");
                            messageBus.QueueMessage(
                                new DiagnosticMessage($"[RetryFact] Test '{TestMethod.MethodName}' failed with non-retriable exception. Skipping retries."));

                            // This attempt ran on the intercepting bus, which deferred the
                            // ITestFailed message so the test could be retried. We are not
                            // retrying, so replay it now: the reporters derive their results
                            // solely from the message bus, and without this the failure would
                            // disappear from the test results entirely and the run would pass.
                            interceptingBus.ReplayDeferredMessages();

                            if (lastException != null)
                            {
                                aggregator.Add(lastException);
                            }

                            runSummary.Failed = 1;
                            return runSummary;
                        }

                        // Not the last attempt - the failure was intercepted, so retry.
                        // Carry the deferred failure forward rather than discarding it now: if the
                        // run is cancelled during the delay, or the next attempt never reports a
                        // result, this is the only remaining record that the test failed.
                        pendingFailureBus = interceptingBus;

                        // Ownership moves to pendingFailureBus. Clear the local so this iteration's
                        // finally does not dispose it, which would replay the failure immediately.
                        interceptingBus = null;

                        Console.WriteLine($"[RetryFact] Test '{TestMethod.TestClass.TestClassName}.{TestMethod.MethodName}' failed on attempt {attempt}/{_maxRetries}, retrying after {_delayMs}ms. Error: {lastException?.Message ?? "No exception message"}");
                        messageBus.QueueMessage(
                            new DiagnosticMessage($"[RetryFact] Test '{TestMethod.TestClass.TestClassName}.{TestMethod.MethodName}' failed on attempt {attempt}/{_maxRetries}. Retrying after {_delayMs}ms delay. Error: {lastException?.Message ?? "No exception message"}"));

                        try
                        {
                            await Task.Delay(_delayMs, cancellationTokenSource.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // Cancelled while waiting to retry. The failure from this attempt is
                            // still deferred, so report it instead of letting the test vanish.
                            Console.WriteLine($"[RetryFact] Test '{TestMethod.TestClass.TestClassName}.{TestMethod.MethodName}' was cancelled during the retry delay after attempt {attempt}/{_maxRetries}. Reporting the failure from that attempt.");

                            pendingFailureBus.ReplayDeferredMessages();

                            if (lastException != null)
                            {
                                aggregator.Add(lastException);
                            }

                            runSummary.Failed = 1;
                            return runSummary;
                        }
                    }
                    else
                    {
                        // Last attempt - failure message already went through to Test Explorer
                        Console.WriteLine($"[RetryFact] Test '{TestMethod.TestClass.TestClassName}.{TestMethod.MethodName}' failed after all {_maxRetries} attempts. Final error: {lastException?.Message ?? "No exception captured"}");
                        messageBus.QueueMessage(
                            new DiagnosticMessage($"[RetryFact] Test '{TestMethod.TestClass.TestClassName}.{TestMethod.MethodName}' failed after {_maxRetries} attempts. Last exception: {lastException?.Message ?? "No exception message"}"));

                        if (lastException != null)
                        {
                            aggregator.Add(lastException);
                        }
                        else if (summary.Failed > 0)
                        {
                            // Add an exception with captured failure details if test failed but no exception was captured
                            aggregator.Add(new InvalidOperationException($"Test failed after {_maxRetries} attempts but no exception was captured"));
                        }

                        runSummary.Failed = 1;
                        return runSummary;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RetryFact] Unexpected exception while running '{TestMethod.TestClass.TestClassName}.{TestMethod.MethodName}': {ex.GetType().FullName}: {ex.Message}");

                    // Disposing replays anything still deferred, so a crash cannot swallow a
                    // failure that an earlier attempt already produced.
                    pendingFailureBus?.Dispose();
                    throw;
                }
                finally
                {
                    interceptingBus?.Dispose();
                }
            }

            // Should never reach here
            Console.WriteLine($"[RetryFact] Reached the end of the retry loop unexpectedly for '{TestMethod.TestClass.TestClassName}.{TestMethod.MethodName}'. Reporting the test as failed.");
            runSummary.Failed = 1;
            return runSummary;
        }

        protected override void Serialize(IXunitSerializationInfo info)
        {
            base.Serialize(info);
            info.AddValue(nameof(_maxRetries), _maxRetries);
            info.AddValue(nameof(_delayMs), _delayMs);
            info.AddValue(nameof(_retryOnAssertionFailure), _retryOnAssertionFailure);
        }

        protected override void Deserialize(IXunitSerializationInfo info)
        {
            base.Deserialize(info);
            _maxRetries = Math.Max(1, info.GetValue<int>(nameof(_maxRetries)));
            _delayMs = Math.Max(0, info.GetValue<int>(nameof(_delayMs)));
            _retryOnAssertionFailure = info.GetValue<bool>(nameof(_retryOnAssertionFailure));
        }

        /// <summary>
        /// Determines if an exception should trigger a retry.
        /// </summary>
        private bool ShouldRetry(Exception ex)
        {
            // If exception is null, we should retry (something went wrong with exception capture)
            if (ex == null)
            {
                Trace.WriteLine($"RetryFact: Exception is null, will retry");
                return true; // Retry when we can't determine the exception type
            }

            // Unwrap aggregate exceptions
            while (ex is AggregateException aggEx && aggEx.InnerExceptions.Count == 1)
            {
                ex = aggEx.InnerException;
            }

            // Don't retry assertion failures unless explicitly configured
            if (ex is XunitException)
            {
                if (!_retryOnAssertionFailure)
                {
                    Trace.WriteLine($"RetryFact: Not retrying XunitException because _retryOnAssertionFailure is false");
                    return false;
                }
                else
                {
                    Trace.WriteLine($"RetryFact: Retrying XunitException because _retryOnAssertionFailure is true");
                    return true;
                }
            }

            // Retry everything else (network, timeout, SQL transient, etc.)
            Trace.WriteLine($"RetryFact: Retrying non-assertion exception of type {ex.GetType().FullName}");
            return true;
        }

        /// <summary>
        /// Message bus that defers failure messages (ITestFailed) so that an attempt which is
        /// about to be retried leaves no trace in the test results.
        /// </summary>
        /// <remarks>
        /// Once a failure is seen, every subsequent message for that attempt is buffered too.
        /// Reporters finalize a test when they see ITestFinished, so forwarding a failure after
        /// its ITestFinished has already gone through has no effect - the failure would be
        /// dropped and the test would disappear from the run entirely. Buffering the tail of the
        /// attempt keeps the messages in their original order, so the caller can either discard
        /// them (retrying) or replay them intact (reporting the failure).
        /// </remarks>
        private class FailureInterceptingMessageBus : IMessageBus
        {
            private readonly IMessageBus _innerBus;
            private readonly bool _deferFailures;
            private readonly List<IMessageSinkMessage> _deferredMessages = new List<IMessageSinkMessage>();
            private bool _deferring;

            public FailureInterceptingMessageBus(IMessageBus innerBus, bool deferFailures)
            {
                _innerBus = innerBus;
                _deferFailures = deferFailures;
            }

            public string LastFailureMessage { get; private set; }

            public string LastFailureStackTrace { get; private set; }

            public bool IsAssertionFailure { get; private set; }

            /// <summary>
            /// Gets a value indicating whether a failure was intercepted and is waiting to be
            /// either discarded or replayed.
            /// </summary>
            public bool HasDeferredFailure => _deferredMessages.Count > 0;

            public bool QueueMessage(IMessageSinkMessage message)
            {
                if (_deferring)
                {
                    _deferredMessages.Add(message);
                    return true;
                }

                if (message is ITestFailed failed)
                {
                    // Capture failure details for diagnostics
                    LastFailureMessage = failed.Messages != null && failed.Messages.Length > 0
                        ? string.Join(Environment.NewLine, failed.Messages)
                        : failed.ExceptionTypes != null && failed.ExceptionTypes.Length > 0
                            ? string.Join(", ", failed.ExceptionTypes)
                            : "Unknown failure";

                    LastFailureStackTrace = failed.StackTraces != null && failed.StackTraces.Length > 0
                        ? string.Join(Environment.NewLine, failed.StackTraces)
                        : null;

                    // Detect if this is an assertion failure by checking exception types
                    // XUnit assertion exceptions typically have types containing "Xunit" or "Assert"
                    IsAssertionFailure = failed.ExceptionTypes != null &&
                        failed.ExceptionTypes.Length > 0 &&
                        (failed.ExceptionTypes[0].Contains("Xunit", StringComparison.Ordinal) ||
                         failed.ExceptionTypes[0].Contains("Assert", StringComparison.Ordinal) ||
                         failed.ExceptionTypes[0].Contains("EqualException", StringComparison.Ordinal) ||
                         failed.ExceptionTypes[0].Contains("TrueException", StringComparison.Ordinal) ||
                         failed.ExceptionTypes[0].Contains("FalseException", StringComparison.Ordinal));

                    if (_deferFailures)
                    {
                        _deferring = true;
                        _deferredMessages.Add(message);
                        return true;
                    }
                }

                // All other messages (ITestPassed, ITestStarting, ITestFinished, etc.) pass through
                return _innerBus.QueueMessage(message);
            }

            /// <summary>
            /// Replays the deferred failure and everything that followed it to the underlying bus,
            /// in their original order. Call this when the attempt will not be retried, otherwise
            /// the failure is never reported and the test silently vanishes from the results.
            /// </summary>
            public void ReplayDeferredMessages()
            {
                foreach (IMessageSinkMessage message in _deferredMessages)
                {
                    _innerBus.QueueMessage(message);
                }

                _deferredMessages.Clear();
                _deferring = false;
            }

            /// <summary>
            /// Drops the deferred failure without reporting it. Call this only when a later attempt
            /// has actually reported a result that supersedes it.
            /// </summary>
            public void DiscardDeferredMessages()
            {
                _deferredMessages.Clear();
                _deferring = false;
            }

            public void Dispose()
            {
                // Don't dispose the inner bus - it's owned by the caller.
                // Anything still deferred was never explicitly replayed or discarded. Dropping it
                // would remove the test from the run, so fail safe by reporting it.
                if (_deferredMessages.Count > 0)
                {
                    Console.WriteLine($"[RetryFact] Internal error: {_deferredMessages.Count} deferred test message(s) were neither replayed nor discarded. Replaying them so the failure is not lost.");
                    ReplayDeferredMessages();
                }
            }
        }
    }
}
