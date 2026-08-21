// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CancelledDuringPassingAttempt
{
    /// <summary>
    /// An attempt that runs to completion while cancellation is already requested has reported
    /// its own result, so an earlier attempt's deferred failure must not also be reported.
    /// </summary>
    /// <remarks>
    /// Run this namespace with --stop-on-fail on. When cancellation alone was treated as "the
    /// attempt reported nothing", the earlier failure was replayed on top of the pass,
    /// producing a second orphaned result with no display name and inflating the run total.
    /// </remarks>
    [Collection("PassesWhileCancelling")]
    public class PassesWhileCancellingTests
    {
        private static int _attempts;

        /// <summary>
        /// Fails on the first attempt, then stays alive long enough on the second attempt for
        /// the sibling collection to cancel the run, and passes anyway.
        /// </summary>
        [RetryFact(MaxRetries = 2, DelayBetweenRetriesMs = 50, RetryOnAssertionFailure = true)]
        public void PassingAttemptIsNotOverriddenByEarlierFailure()
        {
            if (Interlocked.Increment(ref _attempts) == 1)
            {
                Assert.Fail("ASSET: first attempt fails so a failure is deferred");
            }

            // Stay in the second attempt while the sibling collection cancels the run, then prove
            // that is really what happened. Without this the attempt simply passes on its own and
            // the scenario would report the expected result while exercising nothing.
            Thread.Sleep(3000);

            Assert.True(
                TestContext.Current.CancellationToken.IsCancellationRequested,
                "ASSET: the run was not cancelled while this attempt was running, so this scenario did not exercise a pass during cancellation.");
        }
    }
}
