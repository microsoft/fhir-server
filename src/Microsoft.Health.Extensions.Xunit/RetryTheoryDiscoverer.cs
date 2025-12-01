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
    /// Test case discoverer for <see cref="RetryTheoryAttribute"/>.
    /// </summary>
    public class RetryTheoryDiscoverer : IXunitTestCaseDiscoverer
    {
        private readonly TheoryDiscoverer _theoryDiscoverer;
        private readonly IMessageSink _diagnosticMessageSink;

        public RetryTheoryDiscoverer(IMessageSink diagnosticMessageSink)
        {
            _diagnosticMessageSink = diagnosticMessageSink;
            _theoryDiscoverer = new TheoryDiscoverer(diagnosticMessageSink);
        }

        public IEnumerable<IXunitTestCase> Discover(
            ITestFrameworkDiscoveryOptions discoveryOptions,
            ITestMethod testMethod,
            IAttributeInfo factAttribute)
        {
            var maxRetries = factAttribute.GetNamedArgument<int>(nameof(RetryTheoryAttribute.MaxRetries));
            var delayMs = factAttribute.GetNamedArgument<int>(nameof(RetryTheoryAttribute.DelayBetweenRetriesMs));

            // Use default values if not specified
            if (maxRetries == 0)
            {
                maxRetries = 3;
            }

            if (delayMs == 0)
            {
                delayMs = 5000;
            }

            // Use the theory discoverer to get the test cases (one per data row)
            foreach (var testCase in _theoryDiscoverer.Discover(discoveryOptions, testMethod, factAttribute))
            {
                // Wrap each test case in a RetryTestCase
                yield return new RetryTestCase(
                    _diagnosticMessageSink,
                    discoveryOptions.MethodDisplayOrDefault(),
                    discoveryOptions.MethodDisplayOptionsOrDefault(),
                    testCase.TestMethod,
                    maxRetries,
                    delayMs,
                    testCase.TestMethodArguments);
            }
        }
    }
}
