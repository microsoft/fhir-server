// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.SkipAfterFailure
{
    /// <summary>
    /// A retrying test that fails on its first attempt and skips itself on the second.
    /// Expected: 1 result, failed.
    /// </summary>
    /// <remarks>
    /// A pass supersedes an earlier failure because it is a claim that the code works - that is what
    /// retrying is for. A skip claims nothing: it says the attempt should not have run. Letting it
    /// supersede would erase a failure the test really did show, so the skip is discarded instead and
    /// the failure stands.
    /// </remarks>
    public class SkipAfterFailureTests
    {
        private static int _attempts;

        /// <summary>
        /// Throws on the first attempt - a non-assertion exception, so it is retried under the
        /// default policy - and skips itself on the second.
        /// </summary>
        [RetryFact(MaxRetries = 3, DelayBetweenRetriesMs = 10)]
        public void FailsThenSkips()
        {
            if (Interlocked.Increment(ref _attempts) == 1)
            {
                throw new InvalidOperationException("ASSET: first attempt fails, the second skips");
            }

            Assert.Skip("ASSET: skipped on the second attempt");
        }
    }
}
