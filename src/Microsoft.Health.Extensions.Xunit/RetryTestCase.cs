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

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
        public RetryTestCase()
        {
        }

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
            _maxRetries = maxRetries;
            _delayMs = delayMs;
            _retryOnAssertionFailure = retryOnAssertionFailure;
        }

        public async ValueTask<RunSummary> Run(
            ExplicitOption explicitOption,
            IMessageBus messageBus,
            object[] constructorArguments,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
        {
            // Use System.Diagnostics.Trace for ADO visibility
            Trace.WriteLine($"##vso[task.logdetail]RetryFact starting test '{TestMethod.TestClass.TestClassName}.{TestMethod.MethodName}' with MaxRetries={_maxRetries}, DelayMs={_delayMs}, RetryOnAssertionFailure={_retryOnAssertionFailure}");

            var runSummary = new RunSummary { Total = 1 };
            Exception lastException = null;

            for (int attempt = 1; attempt <= _maxRetries; attempt++)
            {
                var isLastAttempt = attempt == _maxRetries;

                Trace.WriteLine($"##vso[task.logdetail]RetryFact attempt {attempt}/{_maxRetries} for test '{TestMethod.MethodName}'");

                // Create a fresh aggregator for each attempt
                var attemptAggregator = new ExceptionAggregator();

                // Only intercept failure messages on non-final attempts
                // On the final attempt, let everything go through (both success and failure)
                IMessageBus busToUse;
                FailureInterceptingMessageBus interceptingBus = null;

                if (isLastAttempt)
                {
                    busToUse = messageBus;
                }
                else
                {
                    interceptingBus = new FailureInterceptingMessageBus(messageBus);
                    busToUse = interceptingBus;
                }

                try
                {
                    var summary = await XunitRunnerHelper.RunXunitTestCase(
                        this,
                        busToUse,
                        cancellationTokenSource,
                        attemptAggregator,
                        explicitOption,
                        constructorArguments);

                    runSummary.Time = summary.Time;

                    if (summary.Failed == 0)
                    {
                        // Test passed - success message already went through to Test Explorer
                        Trace.WriteLine($"##vso[task.logdetail]RetryFact test '{TestMethod.TestClass.TestClassName}.{TestMethod.MethodName}' passed on attempt {attempt}/{_maxRetries}");
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
                        string failureMsg = interceptingBus?.LastFailureMessage ?? "Test failed but no exception was captured.";
                        string stackTrace = interceptingBus?.LastFailureStackTrace;
                        bool isAssertionFailure = interceptingBus?.IsAssertionFailure ?? false;

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

                        Trace.WriteLine($"##vso[task.logdetail]RetryFact: Test failed but exception is null, created placeholder exception (IsAssertion={isAssertionFailure})");
                    }

                    Trace.WriteLine($"##vso[task.logdetail]RetryFact test failed on attempt {attempt} with exception type: {lastException?.GetType().FullName ?? "null"}, Message: {lastException?.Message ?? "null"}");

                    if (!isLastAttempt)
                    {
                        // Check if we should retry this exception (now handles null)
                        var shouldRetry = ShouldRetry(lastException);
                        Trace.WriteLine($"##vso[task.logdetail]RetryFact ShouldRetry={shouldRetry} for exception type {lastException?.GetType().FullName ?? "null"}");

                        if (!shouldRetry)
                        {
                            Trace.WriteLine($"##vso[task.logissue type=warning]Test '{TestMethod.MethodName}' failed with non-retriable exception. Skipping retries.");
                            messageBus.QueueMessage(
                                new DiagnosticMessage($"[RetryFact] Test '{TestMethod.MethodName}' failed with non-retriable exception. Skipping retries."));

                            if (lastException != null)
                            {
                                aggregator.Add(lastException);
                            }

                            runSummary.Failed = 1;
                            return runSummary;
                        }

                        // Not the last attempt - the failure was intercepted, so retry
                        Trace.WriteLine($"##vso[task.logissue type=warning]Test '{TestMethod.MethodName}' failed on attempt {attempt}/{_maxRetries}, will retry after {_delayMs}ms");
                        messageBus.QueueMessage(
                            new DiagnosticMessage($"[RetryFact] Test '{TestMethod.TestClass.TestClassName}.{TestMethod.MethodName}' failed on attempt {attempt}/{_maxRetries}. Retrying after {_delayMs}ms delay. Error: {lastException?.Message ?? "No exception message"}"));

                        await Task.Delay(_delayMs);
                    }
                    else
                    {
                        // Last attempt - failure message already went through to Test Explorer
                        Trace.WriteLine($"##vso[task.logissue type=error]Test '{TestMethod.TestClass.TestClassName}.{TestMethod.MethodName}' failed after {_maxRetries} attempts. Final error: {lastException?.Message ?? "No exception captured"}");
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
                    Trace.WriteLine($"##vso[task.logissue type=error]RetryFact unexpected exception: {ex.GetType().FullName}: {ex.Message}");
                    throw;
                }
                finally
                {
                    // Dispose the intercepting bus if we created one
                    interceptingBus?.Dispose();
                }
            }

            // Should never reach here
            Trace.WriteLine($"##vso[task.logissue type=error]RetryFact WARNING: Reached end of retry loop unexpectedly");
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
            _maxRetries = info.GetValue<int>(nameof(_maxRetries));
            _delayMs = info.GetValue<int>(nameof(_delayMs));
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
                Trace.WriteLine($"##vso[task.logdetail]RetryFact: Exception is null, will retry");
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
                    Trace.WriteLine($"##vso[task.logdetail]RetryFact: Not retrying XunitException because _retryOnAssertionFailure is false");
                    return false;
                }
                else
                {
                    Trace.WriteLine($"##vso[task.logdetail]RetryFact: Retrying XunitException because _retryOnAssertionFailure is true");
                    return true;
                }
            }

            // Retry everything else (network, timeout, SQL transient, etc.)
            Trace.WriteLine($"##vso[task.logdetail]RetryFact: Retrying non-assertion exception of type {ex.GetType().FullName}");
            return true;
        }

        /// <summary>
        /// Message bus that intercepts ONLY failure messages (ITestFailed).
        /// Used on non-final retry attempts to suppress intermediate failures.
        /// Success messages and all other messages always pass through.
        /// Also captures failure details (messages and stack traces) for diagnostic purposes.
        /// </summary>
        private class FailureInterceptingMessageBus : IMessageBus
        {
            private readonly IMessageBus _innerBus;

            public FailureInterceptingMessageBus(IMessageBus innerBus)
            {
                _innerBus = innerBus;
            }

            public string LastFailureMessage { get; private set; }

            public string LastFailureStackTrace { get; private set; }

            public bool IsAssertionFailure { get; private set; }

            public bool QueueMessage(IMessageSinkMessage message)
            {
                // Intercept ONLY failure messages - suppress them for non-final attempts
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

                    return true; // Swallow the failure - we're going to retry
                }

                // All other messages (ITestPassed, ITestStarting, ITestFinished, etc.) pass through
                return _innerBus.QueueMessage(message);
            }

            public void Dispose()
            {
                // Don't dispose the inner bus - it's owned by the caller
            }
        }
    }
}
