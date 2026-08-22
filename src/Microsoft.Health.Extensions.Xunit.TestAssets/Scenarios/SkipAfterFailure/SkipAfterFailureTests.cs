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
    /// Expected: 1 result, skipped.
    /// </summary>
    /// <remarks>
    /// A skip is a definitive result, so it supersedes the earlier attempt's failure the same way a
    /// pass does. This scenario exists to state that outcome rather than leave it to be discovered:
    /// the first attempt's failure is discarded, so a test that fails and then decides it should not
    /// have run is reported skipped, not failed.
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
