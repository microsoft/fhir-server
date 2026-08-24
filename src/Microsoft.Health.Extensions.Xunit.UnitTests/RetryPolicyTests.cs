// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Pins the decision <c>RetryOnAssertionFailure</c> makes, rather than only the outcomes either
    /// side of it.
    /// </summary>
    /// <remarks>
    /// The existing retry scenarios use tests that fail on every attempt or on none. Those report
    /// the same result whether the retries were spent or not, so a regression that retried
    /// everything, or nothing, would leave them green. Both tests here recover on their second
    /// attempt, which makes the reported outcome a direct statement about whether that attempt ran.
    /// </remarks>
    public class RetryPolicyTests
    {
        private const string ClassName = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.RetryPolicy.RetryPolicyTests.";

        /// <summary>
        /// Under the default policy an assertion failure is not retried and a non-assertion
        /// exception is, so a test that recovers on its second attempt is reported failed in the
        /// first case and passed in the second.
        /// </summary>
        [Fact]
        public void GivenTestsThatRecoverOnASecondAttempt_WhenTheDefaultPolicyApplies_ThenOnlyTheNonAssertionFailureIsRetried()
        {
            TestAssetRun run = TestAssetRunner.Run("RetryPolicy");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ClassName + "AssertionFailureUnderTheDefaultPolicy_IsNotRetried"] = "Failed",
                    [ClassName + "NonAssertionExceptionUnderTheDefaultPolicy_IsRetried"] = "Passed",
                    [ClassName + "WrappedAssertionFailureUnderTheDefaultPolicy_IsNotRetried"] = "Failed",
                });
        }
    }
}
