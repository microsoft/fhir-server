// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.ComponentModel;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// Simple retry test case that handles retries with minimal complexity.
    /// </summary>
    public class SimpleRetryTestCase : XunitTestCase
    {
        private int _maxRetries;
        private int _delayMs;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
        public SimpleRetryTestCase()
        {
        }

        public SimpleRetryTestCase(
            IMessageSink diagnosticMessageSink,
            TestMethodDisplay defaultMethodDisplay,
            TestMethodDisplayOptions defaultMethodDisplayOptions,
            ITestMethod testMethod,
            int maxRetries = 3,
            int delayMs = 1000,
            object[] testMethodArguments = null)
            : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod, testMethodArguments)
        {
            _maxRetries = maxRetries;
            _delayMs = delayMs;
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

        public override async Task<RunSummary> RunAsync(
            IMessageSink diagnosticMessageSink,
            IMessageBus messageBus,
            object[] constructorArguments,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
        {
            var attempt = 1;
            var maxAttempts = _maxRetries + 1;
            RunSummary summary = null;

            while (attempt <= maxAttempts)
            {
                summary = await base.RunAsync(
                    diagnosticMessageSink,
                    messageBus,
                    constructorArguments,
                    aggregator,
                    cancellationTokenSource);

                if (summary.Failed == 0 || attempt >= maxAttempts)
                {
                    return summary;
                }

                diagnosticMessageSink.OnMessage(new DiagnosticMessage(
                    $"Test {DisplayName} failed on attempt {attempt}/{maxAttempts}. Retrying in {_delayMs}ms..."));

                if (_delayMs > 0)
                {
                    await Task.Delay(_delayMs, cancellationTokenSource.Token);
                }

                attempt++;
            }

            return summary;
        }
    }
}
