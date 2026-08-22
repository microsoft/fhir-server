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
    /// <remarks>
    /// Both scenarios need their two collections to make progress at the same time: one cancels the
    /// run while the other is mid-attempt. The scenarios arrange that themselves by waiting
    /// asynchronously for each other, so it holds however many threads the runner uses. Pinning a
    /// thread count from here was measured to make it worse rather than better: with two threads and
    /// a blocking wait, the collection being waited for did not get a thread until the waiter had
    /// already given up.
    /// </remarks>
    public class RetryFactCancellationTests
    {
        private const string RetryDelayPrefix = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CancelledDuringRetryDelay.";
        private const string PassingAttemptPrefix = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CancelledDuringPassingAttempt.";

        /// <summary>
        /// What this budget has to separate is a cancelled run from one that sat through its retry
        /// delays instead. Those are 30s apart and there are three of them, so a run that was not
        /// cancelled cannot finish in less than 30s and in practice takes about 90s. A cancelled run
        /// takes seconds. Setting the line at 60s therefore still distinguishes the two, while
        /// leaving room for a loaded agent to be slow at starting a child process without turning a
        /// correct run red: this assertion is a backstop, and the scenario's own handshake is what
        /// makes the cancellation land where it should.
        /// </summary>
        private static readonly TimeSpan CancellationBudget = TimeSpan.FromSeconds(60);

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
