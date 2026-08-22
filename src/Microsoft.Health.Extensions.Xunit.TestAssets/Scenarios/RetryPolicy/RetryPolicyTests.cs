// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.RetryPolicy
{
    /// <summary>
    /// Tests that tell the two <c>RetryOnAssertionFailure</c> policies apart.
    /// Expected: 2 results, 1 failed and 1 passed.
    /// </summary>
    /// <remarks>
    /// A test that fails on every attempt is reported failed whether or not it was retried, and one
    /// that fails on none is reported passed either way, so neither pins the policy that decides
    /// between them. Both tests here fail once and would pass on a second attempt, so the reported
    /// outcome states plainly whether the second attempt was spent.
    /// </remarks>
    public class RetryPolicyTests
    {
        private static int _assertionAttempts;
        private static int _exceptionAttempts;

        /// <summary>
        /// An assertion failure is taken to be deterministic, so with the default policy the
        /// remaining attempts are not spent and the recovery never happens: this must be reported
        /// failed. Were it retried it would pass, so the outcome distinguishes the two.
        /// </summary>
        [RetryFact(MaxRetries = 3, DelayBetweenRetriesMs = 10, RetryOnAssertionFailure = false)]
        public void AssertionFailureUnderTheDefaultPolicy_IsNotRetried()
        {
            if (Interlocked.Increment(ref _assertionAttempts) == 1)
            {
                Assert.Fail("ASSET: first attempt fails an assertion, a second attempt would pass");
            }
        }

        /// <summary>
        /// A non-assertion exception is the transient kind these attributes exist for, so it is
        /// retried even under the default policy: this must be reported passed. Were the policy
        /// applied to every failure alike it would be reported failed.
        /// </summary>
        [RetryFact(MaxRetries = 3, DelayBetweenRetriesMs = 10, RetryOnAssertionFailure = false)]
        public void NonAssertionExceptionUnderTheDefaultPolicy_IsRetried()
        {
            if (Interlocked.Increment(ref _exceptionAttempts) == 1)
            {
                throw new InvalidOperationException("ASSET: first attempt throws, a second attempt would pass");
            }
        }
    }
}
