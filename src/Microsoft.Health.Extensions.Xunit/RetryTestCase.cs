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
            object[] testMethodArguments = null)
            : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod, testMethodArguments)
        {
            _maxRetries = maxRetries;
            _delayMs = delayMs;
        }

        public override async Task<RunSummary> RunAsync(
            IMessageSink diagnosticMessageSink,
            IMessageBus messageBus,
            object[] constructorArguments,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
        {
            var summary = new RunSummary { Total = 1 };
            Exception lastException = null;

            for (int attempt = 1; attempt <= _maxRetries; attempt++)
            {
                // Create a message bus that intercepts messages for all attempts except the last
                using var interceptingMessageBus = new InterceptingMessageBus(messageBus, attempt < _maxRetries);

                // Create a fresh aggregator for each attempt
                var attemptAggregator = new ExceptionAggregator();

                var attemptSummary = await base.RunAsync(
                    diagnosticMessageSink,
                    interceptingMessageBus,
                    constructorArguments,
                    attemptAggregator,
                    cancellationTokenSource);

                summary.Time += attemptSummary.Time;

                if (attemptSummary.Failed == 0)
                {
                    // Test passed, return success
                    diagnosticMessageSink.OnMessage(
                        new DiagnosticMessage($"[RetryFact] Test '{TestMethod.TestClass.Class.Name}.{TestMethod.Method.Name}' passed on attempt {attempt}/{_maxRetries}"));

                    summary.Failed = 0;
                    return summary;
                }

                // Capture the exception for logging
                lastException = attemptAggregator.ToException();

                if (attempt < _maxRetries)
                {
                    diagnosticMessageSink.OnMessage(
                        new DiagnosticMessage($"[RetryFact] Test '{TestMethod.TestClass.Class.Name}.{TestMethod.Method.Name}' failed on attempt {attempt}/{_maxRetries}. Retrying after {_delayMs}ms delay. Error: {lastException?.Message}"));

                    await Task.Delay(_delayMs);
                }
                else
                {
                    // All retries exhausted - add exception to aggregator
                    diagnosticMessageSink.OnMessage(
                        new DiagnosticMessage($"[RetryFact] Test '{TestMethod.TestClass.Class.Name}.{TestMethod.Method.Name}' failed after {_maxRetries} attempts. Last exception: {lastException?.Message}"));

                    if (lastException != null)
                    {
                        aggregator.Add(lastException);
                    }

                    summary.Failed = 1;
                    return summary;
                }
            }

            // Should never reach here, but return failure as fallback
            summary.Failed = 1;
            return summary;
        }

        public override void Serialize(IXunitSerializationInfo data)
        {
            base.Serialize(data);
            data.AddValue(nameof(_maxRetries), _maxRetries);
            data.AddValue(nameof(_delayMs), _delayMs);
        }

        public override void Deserialize(IXunitSerializationInfo data)
        {
            base.Deserialize(data);
            _maxRetries = data.GetValue<int>(nameof(_maxRetries));
            _delayMs = data.GetValue<int>(nameof(_delayMs));
        }

        /// <summary>
        /// Message bus that intercepts test result messages for retry attempts.
        /// </summary>
        private class InterceptingMessageBus : IMessageBus
        {
            private readonly IMessageBus _innerBus;
            private readonly bool _shouldIntercept;

            public InterceptingMessageBus(IMessageBus innerBus, bool shouldIntercept)
            {
                _innerBus = innerBus;
                _shouldIntercept = shouldIntercept;
            }

            public bool QueueMessage(IMessageSinkMessage message)
            {
                // If this is not the final attempt, intercept test result messages
                if (_shouldIntercept)
                {
                    // Suppress test result messages (pass/fail) for non-final attempts
                    if (message is ITestPassed ||
                        message is ITestFailed ||
                        message is ITestSkipped)
                    {
                        return true; // Message handled, don't send to real bus
                    }
                }

                // For the final attempt, or for non-result messages, pass through to the real bus
                return _innerBus.QueueMessage(message);
            }

            public void Dispose()
            {
                // Don't dispose the inner bus - it's owned by the caller
            }
        }
    }
}
