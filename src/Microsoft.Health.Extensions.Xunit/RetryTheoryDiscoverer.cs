// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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

            var baseCases = await base.CreateTestCasesForDataRow(discoveryOptions, testMethod, theoryAttribute, dataRow, testMethodArguments);
            return baseCases
                .Select(testCase => WrapTestCase(testCase, attribute))
                .ToArray();
        }

        protected override async ValueTask<IReadOnlyCollection<IXunitTestCase>> CreateTestCasesForTheory(
            ITestFrameworkDiscoveryOptions discoveryOptions,
            IXunitTestMethod testMethod,
            ITheoryAttribute theoryAttribute)
        {
            var attribute = (RetryTheoryAttribute)theoryAttribute;

            var baseCases = await base.CreateTestCasesForTheory(discoveryOptions, testMethod, theoryAttribute);
            return baseCases
                .Select(testCase => WrapTestCase(testCase, attribute))
                .ToArray();
        }

        private static IXunitTestCase WrapTestCase(IXunitTestCase testCase, RetryTheoryAttribute attribute)
        {
            if (testCase is IXunitDelayEnumeratedTestCase)
            {
                // A delay-enumerated case has not resolved its data rows yet, and it resolves them
                // itself at run time. RetryTestCase cannot stand in for that: it would run the
                // method with the arguments it was constructed with -- none -- and every row of the
                // theory would be lost with an arity error rather than run. Returning the case
                // unwrapped costs the retries but keeps the tests, which is the better trade.
                // Theories reach this path when their data cannot be pre-enumerated, or when
                // pre-enumeration is turned off.
                Console.WriteLine(
                    $"[RetryTheory] WARNING: Test case '{testCase.TestCaseDisplayName}' resolves its data at run time, so retry logic will NOT be applied to it. " +
                    "Use pre-enumerable theory data (for example [InlineData], or [MemberData] whose values are serializable) if the rows need to be retried.");
                return testCase;
            }

            if (testCase is not XunitTestCase xunitTestCase)
            {
                // Trace output only reaches an attached debugger, so a Trace-only warning here
                // means retries are silently not applied in CI. Every test case type xunit.v3
                // produces derives from XunitTestCase, so this only happens if a custom
                // discoverer introduces its own type.
                Console.WriteLine(
                    $"[RetryTheory] WARNING: Test case '{testCase.TestCaseDisplayName}' is {testCase.GetType().Name}, not {nameof(XunitTestCase)}. Retry logic will NOT be applied to it.");
                return testCase;
            }

            // The base case carries the method xUnit resolved for this row, including any generic
            // arguments closed from the row's data. The method passed to the discoverer is still
            // open, so using it here would discard that resolution.
            return new RetryTestCase(
                xunitTestCase.TestMethod,
                xunitTestCase.TestCaseDisplayName,
                xunitTestCase.UniqueID,
                xunitTestCase.Explicit,
                xunitTestCase.SkipExceptions,
                xunitTestCase.SkipReason,
                xunitTestCase.SkipType,
                xunitTestCase.SkipUnless,
                xunitTestCase.SkipWhen,
                xunitTestCase.Traits.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new HashSet<string>(kvp.Value, StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase),
                xunitTestCase.TestMethodArguments,
                xunitTestCase.SourceFilePath,
                xunitTestCase.SourceLineNumber,
                xunitTestCase.Timeout == 0 ? null : xunitTestCase.Timeout,
                attribute.MaxRetries,
                attribute.DelayBetweenRetriesMs,
                attribute.RetryOnAssertionFailure);
        }
    }
}
