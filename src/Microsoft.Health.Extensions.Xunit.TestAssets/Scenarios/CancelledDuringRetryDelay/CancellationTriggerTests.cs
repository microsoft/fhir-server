// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CancelledDuringRetryDelay
{
    /// <summary>
    /// Fails shortly after the run starts so that --stop-on-fail cancels the run while
    /// <see cref="DeferredFailureTests"/> is still waiting to retry.
    /// </summary>
    [Collection("CancellationTrigger")]
    public class CancellationTriggerTests
    {
        /// <summary>
        /// Deliberately fails to trigger cancellation of the whole run.
        /// </summary>
        [Fact]
        public async Task FailsToTriggerCancellation()
        {
            // Wait to be told the sibling has failed an attempt, so this failure cannot land before
            // there is a retry delay to cancel during. The wait is asynchronous so it does not hold
            // a worker thread the sibling collection may need.
            await Task.WhenAny(RetryDelayHandshake.AttemptFailing, Task.Delay(RetryDelayHandshake.Budget));

            // A short settle on top of that ordering, so the sibling is inside its 30s delay rather
            // than on the boundary of entering it. This is a margin, not a race: the ordering above
            // already happened.
            await Task.Delay(700);

            Assert.Fail("ASSET: deliberate failure that cancels the run");
        }
    }
}
