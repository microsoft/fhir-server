// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit.Sdk;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// Test case discoverer for <see cref="RetryFactAttribute"/>.
    /// </summary>
    public sealed class RetryFactDiscoverer : IXunitTestCaseDiscoverer
    {
        /// <summary>
        /// Discovers the test case for a method marked with <see cref="RetryFactAttribute"/>.
        /// </summary>
        /// <param name="discoveryOptions">The discovery options for the test assembly.</param>
        /// <param name="testMethod">The test method being discovered.</param>
        /// <param name="factAttribute">The <see cref="RetryFactAttribute"/> applied to the method.</param>
        /// <returns>A single <see cref="RetryTestCase"/> wrapping the method.</returns>
        public ValueTask<IReadOnlyCollection<IXunitTestCase>> Discover(
            ITestFrameworkDiscoveryOptions discoveryOptions,
            IXunitTestMethod testMethod,
            IFactAttribute factAttribute)
        {
            var attribute = (RetryFactAttribute)factAttribute;

            // Let xUnit compute the test case details the same way it does for a plain [Fact],
            // so that the display name honors the configured MethodDisplay setting instead of
            // being reported as a bare, unqualified method name.
            var details = TestIntrospectionHelper.GetTestCaseDetails(
                discoveryOptions,
                testMethod,
                factAttribute,
                testMethodArguments: null,
                timeout: null,
                baseDisplayName: null);

            var testCase = new RetryTestCase(
                details.ResolvedTestMethod,
                details.TestCaseDisplayName,
                details.UniqueID,
                @explicit: details.Explicit,
                skipExceptions: details.SkipExceptions,
                skipReason: details.SkipReason,
                skipType: details.SkipType,
                skipUnless: details.SkipUnless,
                skipWhen: details.SkipWhen,
                traits: testMethod.Traits.ToDictionary(kvp => kvp.Key, kvp => new HashSet<string>(kvp.Value)),
                testMethodArguments: null,
                sourceFile: details.SourceFilePath,
                sourceLine: details.SourceLineNumber,
                timeout: details.Timeout,
                maxRetries: attribute.MaxRetries,
                delayMs: attribute.DelayBetweenRetriesMs,
                retryOnAssertionFailure: attribute.RetryOnAssertionFailure);

            return new ValueTask<IReadOnlyCollection<IXunitTestCase>>(new[] { testCase });
        }
    }
}
