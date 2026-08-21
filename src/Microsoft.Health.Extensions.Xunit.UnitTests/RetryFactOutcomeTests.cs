// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Verifies that <see cref="RetryFactAttribute"/> publishes exactly one result per test, with
    /// the outcome of the attempt that decided it.
    /// </summary>
    public class RetryFactOutcomeTests
    {
        private const string Prefix = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.RetryOutcomes.RetryOutcomeTests.";

        /// <summary>
        /// Covers the outcome matrix: a non-retriable failure, an exhausted retry, a flaky test
        /// that eventually passes, a non-assertion exception, a plain pass, a run-time skip, and a
        /// clamped configuration. Every one of these must appear exactly once, and the failures must
        /// not be swallowed by the retry bookkeeping. The skip pins that a test which excuses itself
        /// is neither retried nor turned into a pass.
        /// </summary>
        [Fact]
        public void GivenRetryingTests_WhenTheRunCompletes_ThenEachTestIsReportedExactlyOnce()
        {
            TestAssetRun run = TestAssetRunner.Run("RetryOutcomes");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [Prefix + "NonRetriableAssertionFailure_IsReportedOnce"] = "Failed",
                    [Prefix + "ExhaustedRetries_IsReportedFailedOnce"] = "Failed",
                    [Prefix + "NonAssertionException_IsReportedFailedOnce"] = "Failed",
                    [Prefix + "FlakyThenPasses_IsReportedPassedOnce"] = "Passed",
                    [Prefix + "AlwaysPasses_IsReportedPassed"] = "Passed",
                    [Prefix + "SkippedAtRunTime_IsReportedSkipped"] = "NotExecuted",
                    [Prefix + "ClampedRetryConfiguration_RunsExactlyOnce"] = "Passed",
                });
        }
    }
}
