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
        public ValueTask<IReadOnlyCollection<IXunitTestCase>> Discover(
            ITestFrameworkDiscoveryOptions discoveryOptions,
            IXunitTestMethod testMethod,
            IFactAttribute factAttribute)
        {
            var attribute = (RetryFactAttribute)factAttribute;

            var maxRetries = attribute.MaxRetries;
            var delayMs = attribute.DelayBetweenRetriesMs;
            var retryOnAssertionFailure = attribute.RetryOnAssertionFailure;

            var testCase = new RetryTestCase(
                testMethod,
                testMethod.GetDisplayName(testMethod.MethodName, label: null, testMethodArguments: null, methodGenericTypes: null),
                UniqueIDGenerator.ForTestCase(testMethod.UniqueID, null, null),
                @explicit: attribute.Explicit,
                skipExceptions: attribute.SkipExceptions,
                skipReason: attribute.Skip,
                skipType: attribute.SkipType,
                skipUnless: attribute.SkipUnless,
                skipWhen: attribute.SkipWhen,
                traits: testMethod.Traits.ToDictionary(kvp => kvp.Key, kvp => new HashSet<string>(kvp.Value)),
                testMethodArguments: null,
                sourceFile: attribute.SourceFilePath,
                sourceLine: attribute.SourceLineNumber,
                timeout: attribute.Timeout,
                maxRetries: maxRetries,
                delayMs: delayMs,
                retryOnAssertionFailure: retryOnAssertionFailure);

            return new ValueTask<IReadOnlyCollection<IXunitTestCase>>(new[] { testCase });
        }
    }
}
