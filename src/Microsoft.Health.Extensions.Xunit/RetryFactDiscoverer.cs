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
    /// Test case discoverer for <see cref="RetryFactAttribute"/>.
    /// </summary>
    public class RetryFactDiscoverer : IXunitTestCaseDiscoverer
    {
        private readonly IMessageSink _diagnosticMessageSink;

        public RetryFactDiscoverer(IMessageSink diagnosticMessageSink)
        {
            _diagnosticMessageSink = diagnosticMessageSink;
        }

        public IEnumerable<IXunitTestCase> Discover(
            ITestFrameworkDiscoveryOptions discoveryOptions,
            ITestMethod testMethod,
            IAttributeInfo factAttribute)
        {
            var maxRetries = factAttribute.GetNamedArgument<int>(nameof(RetryFactAttribute.MaxRetries));
            var delayMs = factAttribute.GetNamedArgument<int>(nameof(RetryFactAttribute.DelayBetweenRetriesMs));
            var retryOnAssertionFailure = factAttribute.GetNamedArgument<bool>(nameof(RetryFactAttribute.RetryOnAssertionFailure));

            // Use default values if not specified
            if (maxRetries == 0)
            {
                maxRetries = 3;
            }

            if (delayMs == 0)
            {
                delayMs = 5000;
            }

            yield return new RetryTestCase(
                _diagnosticMessageSink,
                discoveryOptions.MethodDisplayOrDefault(),
                discoveryOptions.MethodDisplayOptionsOrDefault(),
                testMethod,
                maxRetries,
                delayMs,
                retryOnAssertionFailure);
        }
    }
}
