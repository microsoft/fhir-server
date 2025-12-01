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
            var runSummary = new RunSummary();
            Exception lastException = null;

            for (int attempt = 1; attempt <= _maxRetries; attempt++)
            {
                var summary = await base.RunAsync(
                    diagnosticMessageSink,
                    messageBus,
                    constructorArguments,
                    aggregator,
                    cancellationTokenSource);

                runSummary.Aggregate(summary);

                if (summary.Failed == 0)
                {
                    // Test passed, no need to retry
                    diagnosticMessageSink.OnMessage(
                        new DiagnosticMessage($"[RetryFact] Test '{TestMethod.TestClass.Class.Name}.{TestMethod.Method.Name}' passed on attempt {attempt}/{_maxRetries}"));

                    return runSummary;
                }

                // Capture the exception for logging
                lastException = aggregator.ToException();

                if (attempt < _maxRetries)
                {
                    // Reset the summary and aggregator for the next attempt
                    runSummary = new RunSummary { Total = 1 };
                    aggregator.Clear();

                    diagnosticMessageSink.OnMessage(
                        new DiagnosticMessage($"[RetryFact] Test '{TestMethod.TestClass.Class.Name}.{TestMethod.Method.Name}' failed on attempt {attempt}/{_maxRetries}. Retrying after {_delayMs}ms delay..."));

                    await Task.Delay(_delayMs);
                }
                else
                {
                    diagnosticMessageSink.OnMessage(
                        new DiagnosticMessage($"[RetryFact] Test '{TestMethod.TestClass.Class.Name}.{TestMethod.Method.Name}' failed after {_maxRetries} attempts. Last exception: {lastException?.Message}"));
                }
            }

            return runSummary;
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
    }
}
