// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
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
        public void FailsToTriggerCancellation()
        {
            Thread.Sleep(700);
            Assert.Fail("ASSET: deliberate failure that cancels the run");
        }
    }
}
