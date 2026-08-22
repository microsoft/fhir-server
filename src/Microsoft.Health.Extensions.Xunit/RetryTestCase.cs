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
        /// Names the test in diagnostics.
        /// </summary>
        /// <remarks>
        /// The display name is used rather than the class and method names because those are not
        /// unique: a method expanded over fixture argument sets, or a theory with several rows,
        /// produces many test cases sharing them. Retry diagnostics are read when something has
        /// already gone wrong, and messages that cannot say which variant retried, or failed after
        /// exhausting its attempts, are worth little at that point. The display name carries the
        /// variant and the row, and falls back to the class and method if it is not set.
        /// </remarks>
        private string TestDescription =>
            !string.IsNullOrEmpty(TestCaseDisplayName)
                ? TestCaseDisplayName
                : $"{TestMethod?.TestClass?.TestClassName}.{TestMethod?.MethodName}";

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
            Trace.WriteLine($"RetryFact starting test '{TestDescription}' with MaxRetries={_maxRetries}, DelayMs={_delayMs}, RetryOnAssertionFailure={_retryOnAssertionFailure}");

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

                Trace.WriteLine($"RetryFact attempt {attempt}/{_maxRetries} for test '{TestDescription}'");

                // Create a fresh aggregator for each attempt
                var attemptAggregator = new ExceptionAggregator();

                FailureInterceptingMessageBus interceptingBus = null;

                try
                {
                    // Always wrap the bus so the failure details are available for the CI log, but only
                    // defer (hide) a failure while a further attempt could still supersede it.
                    interceptingBus = new FailureInterceptingMessageBus(
                        messageBus,
                        deferFailures: !isLastAttempt,
                        deferAbstentions: pendingFailureBus?.HasDeferredFailure ?? false);

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
                        NoResultOutcome outcome = DecideNoResultOutcome(
                            currentAttemptObservedFailure: interceptingBus.HasObservedFailure,
                            earlierAttemptObservedFailure: pendingFailureBus?.HasDeferredFailure ?? false);

                        Console.WriteLine(
                            outcome == NoResultOutcome.ReportNothing
                                ? $"[RetryFact] Test '{TestDescription}' reported no result for attempt {attempt}/{_maxRetries} (the run was cancelled or aborted), and no earlier attempt observed a failure, so there is nothing to report for it."
                                : $"[RetryFact] Test '{TestDescription}' reported no result for attempt {attempt}/{_maxRetries} (the run was cancelled or aborted). Reporting the last observed failure rather than a pass.");

                        // At most one bus is replayed. A failure this attempt saw supersedes anything held
                        // over from an earlier one, and replaying both would publish two results for a
                        // single test.
                        bool continueRunning = true;

                        switch (outcome)
                        {
                            case NoResultOutcome.ReplayCurrentAttempt:
                                pendingFailureBus?.DiscardDeferredMessages();
                                continueRunning = interceptingBus.ReplayDeferredMessages();
                                break;

                            case NoResultOutcome.ReplayEarlierAttempt:
                                continueRunning = pendingFailureBus.ReplayDeferredMessages();
                                break;

                            default:
                                break;
                        }

                        StopRunIfRequested(continueRunning, cancellationTokenSource);

                        Exception noResultException = SelectNoResultException(
                            outcome,
                            currentAttemptException: attemptAggregator.ToException(),
                            earlierAttemptException: lastException);

                        if (noResultException != null)
                        {
                            aggregator.Add(noResultException);
                        }

                        RunSummary noResultSummary = CreateNoResultSummary(outcome);
                        noResultSummary.Time = runSummary.Time;
                        return noResultSummary;
                    }

                    // A skip is not a pass. An attempt that abstains has not shown the test to be
                    // sound, so it must not erase a failure an earlier attempt already demonstrated:
                    // that failure is real, and discarding it here reports the test as skipped or
                    // not run, which no CI leg treats as a failure.
                    if (summary.Failed == 0 &&
                        (summary.Skipped > 0 || summary.NotRun > 0) &&
                        (pendingFailureBus?.HasDeferredFailure ?? false))
                    {
                        Console.WriteLine(
                            $"[RetryFact] Test '{TestDescription}' skipped itself on attempt {attempt}/{_maxRetries} after failing earlier. Reporting the failure rather than the skip.");

                        // The abstention was held rather than published, so dropping it now leaves
                        // the replayed failure as the test's only result.
                        interceptingBus.DiscardDeferredMessages();

                        bool replayed = pendingFailureBus.ReplayDeferredMessages();
                        pendingFailureBus = null;
                        StopRunIfRequested(replayed, cancellationTokenSource);

                        runSummary.Failed = 1;
                        return runSummary;
                    }

                    // This attempt reported a real result, so a failure held over from an earlier
                    // attempt has been superseded and must not also be reported.
                    pendingFailureBus?.DiscardDeferredMessages();

                    if (summary.Failed == 0)
                    {
                        // Test passed - success message already went through to Test Explorer
                        Trace.WriteLine($"RetryFact test '{TestDescription}' passed on attempt {attempt}/{_maxRetries}");
                        messageBus.QueueMessage(
                            new DiagnosticMessage($"[RetryFact] Test '{TestDescription}' passed on attempt {attempt}/{_maxRetries}"));

                        runSummary.Failed = 0;

                        // A test that was skipped or left unrun did not pass, and the counts saying so
                        // live on the attempt's summary. Returning only Total and Failed would report
                        // it to anything reading this summary as a test that ran and passed. The
                        // Microsoft Testing Platform runner is not such a reader - it counts the
                        // messages instead, and reports the skip correctly either way - so this keeps
                        // the summary contract rather than fixing anything visible in a CI report.
                        runSummary.Skipped = summary.Skipped;
                        runSummary.NotRun = summary.NotRun;
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
                            Console.WriteLine($"[RetryFact] Test '{TestDescription}' failed with a non-retriable exception on attempt {attempt}/{_maxRetries}. Skipping remaining retries.");
                            messageBus.QueueMessage(
                                new DiagnosticMessage($"[RetryFact] Test '{TestDescription}' failed with non-retriable exception. Skipping retries."));

                            // This attempt ran on the intercepting bus, which deferred the
                            // ITestFailed message so the test could be retried. We are not
                            // retrying, so replay it now: the reporters derive their results
                            // solely from the message bus, and without this the failure would
                            // disappear from the test results entirely and the run would pass.
                            StopRunIfRequested(interceptingBus.ReplayDeferredMessages(), cancellationTokenSource);

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

                        Console.WriteLine($"[RetryFact] Test '{TestDescription}' failed on attempt {attempt}/{_maxRetries}, retrying after {_delayMs}ms. Error: {lastException?.Message ?? "No exception message"}");
                        messageBus.QueueMessage(
                            new DiagnosticMessage($"[RetryFact] Test '{TestDescription}' failed on attempt {attempt}/{_maxRetries}. Retrying after {_delayMs}ms delay. Error: {lastException?.Message ?? "No exception message"}"));

                        try
                        {
                            await Task.Delay(_delayMs, cancellationTokenSource.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // Cancelled while waiting to retry. The failure from this attempt is
                            // still deferred, so report it instead of letting the test vanish.
                            Console.WriteLine($"[RetryFact] Test '{TestDescription}' was cancelled during the retry delay after attempt {attempt}/{_maxRetries}. Reporting the failure from that attempt.");

                            pendingFailureBus.ReplayDeferredMessages();

                            // The run is already cancelling, so there is nothing further to stop and
                            // the replay result is deliberately ignored here.
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
                        Console.WriteLine($"[RetryFact] Test '{TestDescription}' failed after all {_maxRetries} attempts. Final error: {lastException?.Message ?? "No exception captured"}");
                        messageBus.QueueMessage(
                            new DiagnosticMessage($"[RetryFact] Test '{TestDescription}' failed after {_maxRetries} attempts. Last exception: {lastException?.Message ?? "No exception message"}"));

                        if (lastException != null)
                        {
                            aggregator.Add(lastException);
                        }
                        else if (summary.Failed > 0)
                        {
                            // Defensive only: a failing attempt always has an exception synthesized
                            // for it above, so reaching this means that invariant has been broken.
                            // Reporting something generic is still better than reporting a failure
                            // with nothing attached to explain it.
                            aggregator.Add(new InvalidOperationException($"Test failed after {_maxRetries} attempts but no exception was captured"));
                        }

                        runSummary.Failed = 1;
                        return runSummary;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RetryFact] Unexpected exception while running '{TestDescription}': {ex.GetType().FullName}: {ex.Message}");

                    // A crash must not swallow a failure an earlier attempt already produced, but at
                    // most one attempt's failure may reach the bus: replaying both would publish two
                    // results for a single test. That is the same supersession rule the no-result
                    // path applies, so it is decided the same way here. Disposing replays whatever
                    // is still deferred, and the finally below does that for the current attempt.
                    NoResultOutcome disposition = DecideNoResultOutcome(
                        currentAttemptObservedFailure: interceptingBus?.HasObservedFailure ?? false,
                        earlierAttemptObservedFailure: pendingFailureBus?.HasDeferredFailure ?? false);

                    if (disposition == NoResultOutcome.ReplayEarlierAttempt)
                    {
                        pendingFailureBus?.Dispose();
                    }
                    else
                    {
                        pendingFailureBus?.DiscardDeferredMessages();
                    }

                    throw;
                }
                finally
                {
                    interceptingBus?.Dispose();
                }
            }

            // Should never reach here
            Console.WriteLine($"[RetryFact] Reached the end of the retry loop unexpectedly for '{TestDescription}'. Reporting the test as failed.");
            runSummary.Failed = 1;
            return runSummary;
        }

        /// <summary>
        /// Cancels the run when the message bus asked for it to stop.
        /// </summary>
        /// <param name="continueRunning">The result of replaying deferred messages.</param>
        /// <param name="cancellationTokenSource">The run's cancellation source.</param>
        /// <remarks>
        /// A deferred failure answers <c>true</c> to the runner when it is queued, because at that
        /// point the test may still be retried. Replaying it is therefore the first and only chance
        /// to honour a request to stop, such as the one <c>--stop-on-fail</c> makes.
        /// </remarks>
        private static void StopRunIfRequested(bool continueRunning, CancellationTokenSource cancellationTokenSource)
        {
            if (!continueRunning)
            {
                cancellationTokenSource.Cancel();
            }
        }

        /// <summary>
        /// Decides how an attempt that published no result of its own should be accounted for.
        /// </summary>
        /// <param name="currentAttemptObservedFailure">Whether this attempt saw a failure before it was cut short.</param>
        /// <param name="earlierAttemptObservedFailure">Whether an earlier attempt deferred a failure that has not been superseded.</param>
        /// <returns>Which deferred failure, if any, should reach the results.</returns>
        /// <remarks>
        /// This rule governs both places an attempt can end without reporting: the empty-summary
        /// path, and the crash path where an unexpected exception unwinds the attempt. Both have to
        /// let at most one attempt's failure reach the bus, because replaying two would publish two
        /// results for a single test, so both decide it here rather than each reimplementing it.
        /// </remarks>
        internal static NoResultOutcome DecideNoResultOutcome(bool currentAttemptObservedFailure, bool earlierAttemptObservedFailure)
        {
            if (currentAttemptObservedFailure)
            {
                return NoResultOutcome.ReplayCurrentAttempt;
            }

            return earlierAttemptObservedFailure
                ? NoResultOutcome.ReplayEarlierAttempt
                : NoResultOutcome.ReportNothing;
        }

        /// <summary>
        /// Chooses the exception that belongs with the failure being reported for an attempt that
        /// published no result of its own.
        /// </summary>
        /// <param name="outcome">The outcome chosen by <see cref="DecideNoResultOutcome"/>.</param>
        /// <param name="currentAttemptException">The exception this attempt captured, if any.</param>
        /// <param name="earlierAttemptException">The exception captured by the most recent attempt that failed, if any.</param>
        /// <returns>The exception to attach to the reported failure, or <c>null</c> when nothing should be attached.</returns>
        /// <remarks>
        /// The exception has to describe the same failure that was replayed to the bus, otherwise
        /// the run reports one attempt's failure alongside a different attempt's error text. So the
        /// choice mirrors the bus exactly: the current attempt's failure carries the current
        /// attempt's exception, an earlier attempt's failure carries the earlier one's, and an
        /// attempt that reported nothing at all attaches nothing. Attaching the earlier exception to
        /// a current-attempt failure is the specific mistake this guards against, because
        /// <c>lastException</c> still holds the previous attempt's error at that point.
        /// </remarks>
        internal static Exception SelectNoResultException(NoResultOutcome outcome, Exception currentAttemptException, Exception earlierAttemptException)
        {
            switch (outcome)
            {
                case NoResultOutcome.ReplayCurrentAttempt:
                    return currentAttemptException;

                case NoResultOutcome.ReplayEarlierAttempt:
                    return earlierAttemptException;

                default:
                    return null;
            }
        }

        /// <summary>
        /// Builds the summary for an attempt that published no result of its own.
        /// </summary>
        /// <param name="outcome">The outcome chosen by <see cref="DecideNoResultOutcome"/>.</param>
        /// <returns>The totals this test contributes to the run.</returns>
        /// <remarks>
        /// A replayed failure is a published result, so it counts as one failed test. When nothing
        /// was ever published the test contributes nothing at all: counting it would claim an
        /// outcome that no attempt reported, and a total of one with no failures reads as a pass.
        /// </remarks>
        internal static RunSummary CreateNoResultSummary(NoResultOutcome outcome)
        {
            return outcome == NoResultOutcome.ReportNothing
                ? new RunSummary { Total = 0, Failed = 0 }
                : new RunSummary { Total = 1, Failed = 1 };
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
        /// <remarks>
        /// The whole exception tree is searched, not just the outermost exception or a chain of
        /// single-inner aggregates. An assertion that fails inside <c>Task.WhenAll</c>, a parallel
        /// helper, or any teardown that runs alongside it arrives wrapped in an
        /// <see cref="AggregateException"/> that may hold several inner exceptions, and stopping at
        /// the first fork would classify it as an ordinary exception and retry it - the very thing
        /// <see cref="RetryOnAssertionFailure"/> was set to prevent. This matches how the message bus
        /// classifies the same failure, which reads the full chain of reported type names; the two
        /// have to agree, or whether the policy is honoured would depend on which of them happened to
        /// see the failure first.
        /// <para>
        /// A timeout needs no carve-out here. The bus classifies by type name and so has to exclude
        /// <c>Xunit.Sdk.TestTimeoutException</c> explicitly, but that type derives from
        /// <see cref="Exception"/> rather than <see cref="XunitException"/>, so it never matches the
        /// test below and is retried, which is what a timeout deserves.
        /// </para>
        /// </remarks>
        private bool ShouldRetry(Exception ex)
        {
            // If exception is null, we should retry (something went wrong with exception capture)
            if (ex == null)
            {
                Trace.WriteLine($"RetryFact: Exception is null, will retry");
                return true; // Retry when we can't determine the exception type
            }

            // Don't retry assertion failures unless explicitly configured
            if (ContainsAssertionFailure(ex))
            {
                Trace.WriteLine($"RetryFact: {(_retryOnAssertionFailure ? "Retrying" : "Not retrying")} XunitException because _retryOnAssertionFailure is {_retryOnAssertionFailure.ToString().ToLowerInvariant()}");
                return _retryOnAssertionFailure;
            }

            // Retry everything else (network, timeout, SQL transient, etc.)
            Trace.WriteLine($"RetryFact: Retrying non-assertion exception of type {ex.GetType().FullName}");
            return true;
        }

        /// <summary>
        /// Reports whether an exception is, or contains anywhere within it, an assertion failure.
        /// </summary>
        /// <remarks>
        /// Separate and internal so the decision can be pinned directly. In a real run the message bus
        /// almost always classifies the failure first, because xunit reports failures through the bus
        /// rather than letting them out of the attempt, which leaves this path reachable but rarely
        /// taken - and a rarely taken path that disagrees with the common one is exactly the kind of
        /// difference that only shows up as an unexplained retry long after the fact.
        /// </remarks>
        /// <param name="exception">The exception to classify.</param>
        /// <returns><c>true</c> if an assertion failure appears anywhere in the exception tree.</returns>
        internal static bool ContainsAssertionFailure(Exception exception) =>
            exception != null && EnumerateExceptionTree(exception).Any(inner => inner is XunitException);

        /// <summary>
        /// Walks an exception and everything nested inside it, including every branch of an
        /// <see cref="AggregateException"/>.
        /// </summary>
        /// <param name="exception">The exception to walk.</param>
        /// <returns>The exception followed by each exception nested within it.</returns>
        private static IEnumerable<Exception> EnumerateExceptionTree(Exception exception)
        {
            var pending = new Stack<Exception>();
            pending.Push(exception);

            while (pending.Count > 0)
            {
                Exception current = pending.Pop();
                yield return current;

                if (current is AggregateException aggregate)
                {
                    foreach (Exception inner in aggregate.InnerExceptions)
                    {
                        if (inner != null)
                        {
                            pending.Push(inner);
                        }
                    }
                }
                else if (current.InnerException != null)
                {
                    pending.Push(current.InnerException);
                }
            }
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
            private readonly bool _deferAbstentions;
            private readonly List<IMessageSinkMessage> _deferredMessages = new List<IMessageSinkMessage>();
            private bool _deferring;

            public FailureInterceptingMessageBus(IMessageBus innerBus, bool deferFailures, bool deferAbstentions = false)
            {
                _innerBus = innerBus;
                _deferFailures = deferFailures;
                _deferAbstentions = deferAbstentions;
            }

            public string LastFailureMessage { get; private set; }

            public string LastFailureStackTrace { get; private set; }

            public bool IsAssertionFailure { get; private set; }

            /// <summary>
            /// Gets a value indicating whether a failure was intercepted and is waiting to be
            /// either discarded or replayed.
            /// </summary>
            public bool HasDeferredFailure => _deferredMessages.Count > 0;

            /// <summary>
            /// Gets a value indicating whether this attempt saw a failure at all, whether or not it
            /// was deferred.
            /// </summary>
            /// <remarks>
            /// This is not the same question as <see cref="HasDeferredFailure"/>, and the difference
            /// falls exactly on the last attempt: that one is constructed with
            /// <c>deferFailures: false</c>, so its failures go straight through to the inner bus and
            /// it never has one deferred. Asking <see cref="HasDeferredFailure"/> whether the
            /// current attempt failed therefore always answers no on the last attempt, and an
            /// earlier attempt's held-over failure would then be replayed on top of the failure this
            /// one already published - two results for a single test.
            /// </remarks>
            public bool HasObservedFailure => LastFailureMessage != null;

            public bool QueueMessage(IMessageSinkMessage message)
            {
                if (_deferring)
                {
                    _deferredMessages.Add(message);
                    return true;
                }

                if (_deferAbstentions && (message is ITestSkipped || message is ITestNotRun))
                {
                    // An attempt that skips itself has abstained rather than shown the test to be
                    // sound, and this attempt follows one that failed. Hold the abstention so the
                    // caller can choose between it and that earlier failure; publishing it here
                    // would leave the failure no way to be reported without a second result.
                    _deferring = true;
                    _deferredMessages.Add(message);
                    return true;
                }

                if (message is ITestFailed failed)
                {
                    // Capture failure details for diagnostics
                    if (failed.Messages != null && failed.Messages.Length > 0)
                    {
                        LastFailureMessage = string.Join(Environment.NewLine, failed.Messages);
                    }
                    else if (failed.ExceptionTypes != null && failed.ExceptionTypes.Length > 0)
                    {
                        LastFailureMessage = string.Join(", ", failed.ExceptionTypes);
                    }
                    else
                    {
                        LastFailureMessage = "Unknown failure";
                    }

                    LastFailureStackTrace = failed.StackTraces != null && failed.StackTraces.Length > 0
                        ? string.Join(Environment.NewLine, failed.StackTraces)
                        : null;

                    // Classify the failure as an assertion failure so RetryOnAssertionFailure can decide
                    // whether it is worth another attempt. This is a substring match, not a namespace
                    // check: xUnit's own assertion exceptions live under Xunit.Sdk, but assertion
                    // libraries used alongside it do not, and matching "Assert" anywhere in the type
                    // name catches those too. The trade is that an unrelated type whose name happens to
                    // contain either word is classified the same way, and with RetryOnAssertionFailure
                    // left at its default of false that costs the test its remaining attempts rather
                    // than spending one: an assertion failure is taken to be deterministic, so it is
                    // not retried. The failure is still reported red either way, so the cost is a
                    // flaky test going unretried, never a failure going unseen.
                    //
                    // A timeout is excluded because it is the one failure that trade cannot be made
                    // for. Xunit.Sdk.TestTimeoutException contains "Xunit" and so matched here, but a
                    // test that ran long because a dependency was slow once is the definition of the
                    // flakiness these attributes exist to absorb -- and unlike the cases above, it is
                    // not an assertion at all. It derives from System.Exception rather than
                    // XunitException, so it is only ever mistaken for one by way of this match.
                    // A wrapped assertion failure is still an assertion failure. Xunit reports the
                    // whole exception chain here, outer type first, so reading only the first entry
                    // classifies AggregateException(XunitException) -- what an assertion failing
                    // inside Task.WhenAll or a parallel helper produces -- as an ordinary exception
                    // and retries it, which is exactly what the policy was set to prevent.
                    string[] failedExceptionTypes = failed.ExceptionTypes != null && failed.ExceptionTypes.Length > 0
                        ? failed.ExceptionTypes
                        : new[] { string.Empty };

                    // The timeout carve-out is applied to the whole chain rather than one entry: a
                    // timeout wrapped in anything is still a timeout, and still the failure that most
                    // deserves another attempt.
                    IsAssertionFailure =
                        !failedExceptionTypes.Any(type => type != null && type.Contains("Timeout", StringComparison.Ordinal)) &&
                        failedExceptionTypes.Any(type => type != null &&
                            (type.Contains("Xunit", StringComparison.Ordinal) ||
                             type.Contains("Assert", StringComparison.Ordinal)));

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
            /// <returns>
            /// <c>false</c> when the underlying bus asked for the run to stop. Deferring a failure
            /// forces a <c>true</c> back to the runner at the time it is queued, so this is the only
            /// point at which a request to stop -- from --stop-on-fail, for instance -- can still be
            /// honoured.
            /// </returns>
            public bool ReplayDeferredMessages()
            {
                bool continueRunning = true;

                foreach (IMessageSinkMessage message in _deferredMessages)
                {
                    continueRunning &= _innerBus.QueueMessage(message);
                }

                _deferredMessages.Clear();
                _deferring = false;

                return continueRunning;
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

                    try
                    {
                        ReplayDeferredMessages();
                    }
                    catch (Exception e)
                    {
                        // Dispose runs on the way out of the attempt, including while an exception is
                        // already unwinding. Throwing from here would replace whatever actually went
                        // wrong with a reporting error, so the failure to replay is logged and the
                        // original exception is left to propagate.
                        Console.WriteLine($"[RetryFact] Failed to replay {_deferredMessages.Count} deferred test message(s): {e.GetType().FullName}: {e.Message}");
                    }
                }
            }
        }
    }
}
