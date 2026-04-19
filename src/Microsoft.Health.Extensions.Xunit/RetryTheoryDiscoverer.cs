// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// Test case discoverer for <see cref="RetryTheoryAttribute"/>.
    /// For Theory tests, we need to let xUnit discover the data-driven test cases first,
    /// then wrap each one with retry logic.
    /// </summary>
    public sealed class RetryTheoryDiscoverer : TheoryDiscoverer
    {
        protected override async ValueTask<IReadOnlyCollection<IXunitTestCase>> CreateTestCasesForDataRow(
            ITestFrameworkDiscoveryOptions discoveryOptions,
            IXunitTestMethod testMethod,
            ITheoryAttribute theoryAttribute,
            ITheoryDataRow dataRow,
            object[] testMethodArguments)
        {
            var attribute = (RetryTheoryAttribute)theoryAttribute;

            var maxRetries = attribute.MaxRetries;
            var delayMs = attribute.DelayBetweenRetriesMs;
            var retryOnAssertionFailure = attribute.RetryOnAssertionFailure;

            var baseCases = await base.CreateTestCasesForDataRow(discoveryOptions, testMethod, theoryAttribute, dataRow, testMethodArguments);
            return baseCases
                .Select(testCase => WrapTestCase(testMethod, testCase, attribute, maxRetries, delayMs, retryOnAssertionFailure))
                .ToArray();
        }

        protected override async ValueTask<IReadOnlyCollection<IXunitTestCase>> CreateTestCasesForTheory(
            ITestFrameworkDiscoveryOptions discoveryOptions,
            IXunitTestMethod testMethod,
            ITheoryAttribute theoryAttribute)
        {
            var attribute = (RetryTheoryAttribute)theoryAttribute;

            var maxRetries = attribute.MaxRetries;
            var delayMs = attribute.DelayBetweenRetriesMs;
            var retryOnAssertionFailure = attribute.RetryOnAssertionFailure;

            var baseCases = await base.CreateTestCasesForTheory(discoveryOptions, testMethod, theoryAttribute);
            return baseCases
                .Select(testCase => WrapTestCase(testMethod, testCase, attribute, maxRetries, delayMs, retryOnAssertionFailure))
                .ToArray();
        }

        private static RetryTestCase WrapTestCase(IXunitTestMethod testMethod, IXunitTestCase testCase, RetryTheoryAttribute attribute, int maxRetries, int delayMs, bool retryOnAssertionFailure)
        {
            var xunitTestCase = (XunitTestCase)testCase;

            return new RetryTestCase(
                testMethod,
                xunitTestCase.TestCaseDisplayName,
                xunitTestCase.UniqueID,
                xunitTestCase.Explicit,
                xunitTestCase.SkipExceptions,
                xunitTestCase.SkipReason,
                xunitTestCase.SkipType,
                xunitTestCase.SkipUnless,
                xunitTestCase.SkipWhen,
                new Dictionary<string, HashSet<string>>(xunitTestCase.Traits.ToDictionary(kvp => kvp.Key, kvp => new HashSet<string>(kvp.Value))),
                xunitTestCase.TestMethodArguments,
                xunitTestCase.SourceFilePath,
                xunitTestCase.SourceLineNumber,
                xunitTestCase.Timeout == 0 ? null : xunitTestCase.Timeout,
                maxRetries,
                delayMs,
                retryOnAssertionFailure);
        }
    }
}
