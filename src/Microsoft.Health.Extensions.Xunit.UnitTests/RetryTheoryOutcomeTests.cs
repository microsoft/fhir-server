// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Verifies that <see cref="RetryTheoryAttribute"/> publishes exactly one result per data row,
    /// with the outcome of the attempt that decided that row.
    /// </summary>
    public class RetryTheoryOutcomeTests
    {
        private const string Prefix = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.RetryTheoryOutcomes.RetryTheoryOutcomeTests.";

        /// <summary>
        /// Retries are applied per row, not per method: two rows recover on their second attempt and
        /// must be reported as passed, while the row that fails every attempt must be reported as
        /// failed. A discoverer that lost rows, shared a single test case between them, or dropped
        /// the retry wrapper would change this result set.
        /// </summary>
        [Fact]
        public void GivenARetryingTheory_WhenTheRunCompletes_ThenEachRowIsReportedExactlyOnce()
        {
            TestAssetRun run = TestAssetRunner.Run("RetryTheoryOutcomes");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [Prefix + "FlakyRow(row: 1, recovers: True)"] = "Passed",
                    [Prefix + "FlakyRow(row: 2, recovers: False)"] = "Failed",
                    [Prefix + "FlakyRow(row: 3, recovers: True)"] = "Passed",
                });
        }

        /// <summary>
        /// A theory whose data is resolved at run time rather than at discovery cannot be wrapped for
        /// retry, because the wrapper would supply the arguments it was built with -- none -- and every
        /// row would be lost to an arity error instead of running. The discoverer leaves such a case
        /// alone, so the rows still run, each with its own arguments, and simply do not retry: all
        /// three fail here because each one fails its first attempt. Names carrying the argument values
        /// are what separates "ran without retrying" from "lost its data".
        /// </summary>
        [Fact]
        public void GivenARetryingTheoryResolvedAtRunTime_WhenTheRunCompletes_ThenEveryRowStillRunsWithItsArguments()
        {
            TestAssetRun run = TestAssetRunner.Run("RetryTheoryOutcomes", preEnumerateTheories: false);

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [Prefix + "FlakyRow(row: 1, recovers: True)"] = "Failed",
                    [Prefix + "FlakyRow(row: 2, recovers: False)"] = "Failed",
                    [Prefix + "FlakyRow(row: 3, recovers: True)"] = "Failed",
                });
        }
    }
}
