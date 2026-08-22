// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CancelledDuringPassingAttempt
{
    /// <summary>
    /// Fails partway through the retrying test's second attempt, tripping --stop-on-fail so the
    /// run is cancelled while <see cref="PassesWhileCancellingTests"/> is still executing.
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
            // Waiting for the sibling collection to announce its second attempt is what makes this
            // failure land while that attempt is still running. A sleep here would only be a guess
            // at how long the sibling takes to get there. The wait is asynchronous so that it does
            // not hold a worker thread the sibling collection may need in order to get there at all.
            await Task.WhenAny(
                PassingAttemptHandshake.SecondAttemptRunning,
                Task.Delay(PassingAttemptHandshake.Budget));

            Assert.Fail("ASSET: deliberate failure that cancels the run");
        }
    }
}
