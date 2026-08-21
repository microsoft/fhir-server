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
    }
}
