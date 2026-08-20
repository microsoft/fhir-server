// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
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
        public void FailsToTriggerCancellation()
        {
            Thread.Sleep(800);
            Assert.Fail("ASSET: deliberate failure that cancels the run");
        }
    }
}
