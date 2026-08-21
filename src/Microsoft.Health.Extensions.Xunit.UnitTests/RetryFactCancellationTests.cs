// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Verifies how <see cref="RetryFactAttribute"/> resolves a deferred failure when the run is
    /// cancelled part way through, which is where a retrying test can either lose a real failure
    /// or invent one that never happened.
    /// </summary>
    public class RetryFactCancellationTests
    {
        private const string RetryDelayPrefix = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CancelledDuringRetryDelay.";
        private const string PassingAttemptPrefix = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CancelledDuringPassingAttempt.";

        /// <summary>
        /// Comfortably above what a cancelled run needs, and comfortably below the 30s retry delay
        /// the scenario would otherwise sit through, so the assertion distinguishes the two without
        /// being sensitive to how quickly the run starts up.
        /// </summary>
        private static readonly TimeSpan CancellationBudget = TimeSpan.FromSeconds(20);

        /// <summary>
        /// A failing attempt's result is held back in case a later attempt supersedes it. If the
        /// run is cancelled during the retry delay there is no later attempt, so the held-back
        /// failure is the only record the test ever ran and must still be published.
        /// </summary>
        [Fact]
        public void GivenARetryingTest_WhenTheRunIsCancelledDuringTheRetryDelay_ThenTheDeferredFailureIsStillReported()
        {
            TestAssetRun run = TestAssetRunner.Run("CancelledDuringRetryDelay", stopOnFail: true);

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [RetryDelayPrefix + "CancellationTriggerTests.FailsToTriggerCancellation"] = "Failed",
                    [RetryDelayPrefix + "DeferredFailureTests.FailureSurvivesCancellationDuringRetryDelay"] = "Failed",
                });

            // The same failed result would be reported by simply exhausting every retry, so without
            // this the scenario would still pass if cancellation never happened. Three attempts
            // spaced by the 30s delay cannot complete in anything close to this budget.
            string message = $"The run took {run.Duration.TotalSeconds:F1}s, which is long enough to have exhausted "
                + $"the retry delays rather than being cancelled during one. The expected results were reported, but "
                + $"not for the reason this scenario exists to check.{Environment.NewLine}Actual: {run}";

            Assert.True(run.Duration < CancellationBudget, message);
        }

        /// <summary>
        /// An attempt can run to completion while cancellation is already requested. It has
        /// published its own result, so the earlier attempt's deferred failure must be discarded:
        /// republishing it would add a second, unattributable result for a test that passed.
        /// </summary>
        [Fact]
        public void GivenARetryingTest_WhenAnAttemptPassesWhileCancellationIsRequested_ThenOnlyThePassIsReported()
        {
            TestAssetRun run = TestAssetRunner.Run("CancelledDuringPassingAttempt", stopOnFail: true);

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [PassingAttemptPrefix + "CancellationTriggerTests.FailsToTriggerCancellation"] = "Failed",
                    [PassingAttemptPrefix + "PassesWhileCancellingTests.PassingAttemptIsNotOverriddenByEarlierFailure"] = "Passed",
                });
        }
    }
}
