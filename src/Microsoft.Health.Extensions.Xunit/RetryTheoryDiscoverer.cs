// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// Test case discoverer for <see cref="RetryTheoryAttribute"/>.
    /// For Theory tests, we need to let xUnit discover the data-driven test cases first,
    /// then wrap each one with retry logic.
    /// </summary>
    public class RetryTheoryDiscoverer : TheoryDiscoverer
    {
        public RetryTheoryDiscoverer(IMessageSink diagnosticMessageSink)
            : base(diagnosticMessageSink)
        {
        }

        protected override IEnumerable<IXunitTestCase> CreateTestCasesForDataRow(
            ITestFrameworkDiscoveryOptions discoveryOptions,
            ITestMethod testMethod,
            IAttributeInfo theoryAttribute,
            object[] dataRow)
        {
            var maxRetries = theoryAttribute.GetNamedArgument<int>(nameof(RetryTheoryAttribute.MaxRetries));
            var delayMs = theoryAttribute.GetNamedArgument<int>(nameof(RetryTheoryAttribute.DelayBetweenRetriesMs));

            // Use default values if not specified
            if (maxRetries == 0)
            {
                maxRetries = 3;
            }

            if (delayMs == 0)
            {
                delayMs = 5000;
            }

            // Create a RetryTestCase for each data row
            yield return new RetryTestCase(
                DiagnosticMessageSink,
                discoveryOptions.MethodDisplayOrDefault(),
                discoveryOptions.MethodDisplayOptionsOrDefault(),
                testMethod,
                maxRetries,
                delayMs,
                dataRow);
        }

        protected override IEnumerable<IXunitTestCase> CreateTestCasesForTheory(
            ITestFrameworkDiscoveryOptions discoveryOptions,
            ITestMethod testMethod,
            IAttributeInfo theoryAttribute)
        {
            var maxRetries = theoryAttribute.GetNamedArgument<int>(nameof(RetryTheoryAttribute.MaxRetries));
            var delayMs = theoryAttribute.GetNamedArgument<int>(nameof(RetryTheoryAttribute.DelayBetweenRetriesMs));

            // Use default values if not specified
            if (maxRetries == 0)
            {
                maxRetries = 3;
            }

            if (delayMs == 0)
            {
                delayMs = 5000;
            }

            // For theories without data (will be skipped), wrap in retry
            return base.CreateTestCasesForTheory(discoveryOptions, testMethod, theoryAttribute)
                .Select(testCase => new RetryTestCase(
                    DiagnosticMessageSink,
                    discoveryOptions.MethodDisplayOrDefault(),
                    discoveryOptions.MethodDisplayOptionsOrDefault(),
                    testMethod,
                    maxRetries,
                    delayMs,
                    testCase.TestMethodArguments));
        }
    }
}
