// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// Test case that implements retry logic.
    /// </summary>
    public class RetryTestCase : XunitTestCase
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
            IMessageSink diagnosticMessageSink,
            TestMethodDisplay defaultMethodDisplay,
            TestMethodDisplayOptions defaultMethodDisplayOptions,
            ITestMethod testMethod,
            int maxRetries,
            int delayMs,
            bool retryOnAssertionFailure = false,
            object[] testMethodArguments = null)
            : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod, testMethodArguments)
        {
            _maxRetries = maxRetries;
            _delayMs = delayMs;
            _retryOnAssertionFailure = retryOnAssertionFailure;
        }

        public override async Task<RunSummary> RunAsync(
            IMessageSink diagnosticMessageSink,
            IMessageBus messageBus,
            object[] constructorArguments,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
        {
            var runSummary = new RunSummary { Total = 1 };
            Exception lastException = null;

            for (int attempt = 1; attempt <= _maxRetries; attempt++)
            {
                var isLastAttempt = attempt == _maxRetries;

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
                    var summary = await base.RunAsync(
                        diagnosticMessageSink,
                        busToUse,
                        constructorArguments,
                        attemptAggregator,
                        cancellationTokenSource);

                    runSummary.Time = summary.Time;

                    if (summary.Failed == 0)
                    {
                        // Test passed - success message already went through to Test Explorer
                        diagnosticMessageSink.OnMessage(
                            new DiagnosticMessage($"[RetryFact] Test '{TestMethod.TestClass.Class.Name}.{TestMethod.Method.Name}' passed on attempt {attempt}/{_maxRetries}"));

                        runSummary.Failed = 0;
                        return runSummary;
                    }

                    // Test failed on this attempt
                    lastException = attemptAggregator.ToException();

                    if (!isLastAttempt)
                    {
                        // Check if we should retry this exception
                        if (!ShouldRetry(lastException))
                        {
                            diagnosticMessageSink.OnMessage(
                                new DiagnosticMessage($"[RetryFact] Test '{TestMethod.Method.Name}' failed with non-retriable exception. Skipping retries."));

                            if (lastException != null)
                            {
                                aggregator.Add(lastException);
                            }

                            runSummary.Failed = 1;
                            return runSummary;
                        }

                        // Not the last attempt - the failure was intercepted, so retry
                        diagnosticMessageSink.OnMessage(
                            new DiagnosticMessage($"[RetryFact] Test '{TestMethod.TestClass.Class.Name}.{TestMethod.Method.Name}' failed on attempt {attempt}/{_maxRetries}. Retrying after {_delayMs}ms delay. Error: {lastException?.Message}"));

                        // Send a custom property to ADO
                        diagnosticMessageSink.OnMessage(
                            new DiagnosticMessage($"##vso[task.logissue type=warning]Test '{TestMethod.Method.Name}' failed on attempt {attempt}, will retry"));

                        await Task.Delay(_delayMs);
                    }
                    else
                    {
                        // Last attempt - failure message already went through to Test Explorer
                        diagnosticMessageSink.OnMessage(
                            new DiagnosticMessage($"[RetryFact] Test '{TestMethod.TestClass.Class.Name}.{TestMethod.Method.Name}' failed after {_maxRetries} attempts. Last exception: {lastException?.Message}"));

                        if (lastException != null)
                        {
                            aggregator.Add(lastException);
                        }

                        runSummary.Failed = 1;
                        return runSummary;
                    }
                }
                finally
                {
                    // Dispose the intercepting bus if we created one
                    interceptingBus?.Dispose();
                }
            }

            // Should never reach here
            runSummary.Failed = 1;
            return runSummary;
        }

        public override void Serialize(IXunitSerializationInfo data)
        {
            base.Serialize(data);
            data.AddValue(nameof(_maxRetries), _maxRetries);
            data.AddValue(nameof(_delayMs), _delayMs);
            data.AddValue(nameof(_retryOnAssertionFailure), _retryOnAssertionFailure);
        }

        public override void Deserialize(IXunitSerializationInfo data)
        {
            base.Deserialize(data);
            _maxRetries = data.GetValue<int>(nameof(_maxRetries));
            _delayMs = data.GetValue<int>(nameof(_delayMs));
            _retryOnAssertionFailure = data.GetValue<bool>(nameof(_retryOnAssertionFailure));
        }

        /// <summary>
        /// Determines if an exception should trigger a retry.
        /// </summary>
        private bool ShouldRetry(Exception ex)
        {
            // Don't retry assertion failures unless explicitly configured
            if (ex is XunitException && !_retryOnAssertionFailure)
            {
                return false;
            }

            // Retry everything else (network, timeout, SQL transient, etc.)
            return true;
        }

        /// <summary>
        /// Message bus that intercepts ONLY failure messages (ITestFailed).
        /// Used on non-final retry attempts to suppress intermediate failures.
        /// Success messages and all other messages always pass through.
        /// </summary>
        private class FailureInterceptingMessageBus : IMessageBus
        {
            private readonly IMessageBus _innerBus;

            public FailureInterceptingMessageBus(IMessageBus innerBus)
            {
                _innerBus = innerBus;
            }

            public bool QueueMessage(IMessageSinkMessage message)
            {
                // Intercept ONLY failure messages - suppress them for non-final attempts
                if (message is ITestFailed)
                {
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
