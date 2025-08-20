// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// Simple discoverer for retry facts.
    /// </summary>
    public class SimpleRetryFactDiscoverer : IXunitTestCaseDiscoverer
    {
        private readonly IMessageSink _diagnosticMessageSink;

        public SimpleRetryFactDiscoverer(IMessageSink diagnosticMessageSink)
        {
            _diagnosticMessageSink = diagnosticMessageSink;
        }

        public IEnumerable<IXunitTestCase> Discover(
            ITestFrameworkDiscoveryOptions discoveryOptions,
            ITestMethod testMethod,
            IAttributeInfo factAttribute)
        {
            var maxRetries = factAttribute.GetNamedArgument<int>(nameof(RetryFactAttribute.MaxRetries));
            var delayMs = factAttribute.GetNamedArgument<int>(nameof(RetryFactAttribute.DelayMs));
            var timeoutMs = factAttribute.GetNamedArgument<int>(nameof(RetryFactAttribute.TimeoutMs));

            if (maxRetries <= 1)
            {
                return new[] { new XunitTestCase(_diagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod) };
            }

            return new[]
            {
                new SimpleRetryTestCase(
                    _diagnosticMessageSink,
                    discoveryOptions.MethodDisplayOrDefault(),
                    discoveryOptions.MethodDisplayOptionsOrDefault(),
                    testMethod,
                    maxRetries,
                    delayMs,
                    timeoutMs),
            };
        }
    }
}
