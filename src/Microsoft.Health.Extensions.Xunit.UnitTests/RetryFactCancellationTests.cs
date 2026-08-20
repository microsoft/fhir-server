// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
