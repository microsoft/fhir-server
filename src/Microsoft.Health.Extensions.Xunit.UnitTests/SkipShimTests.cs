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
    /// Verifies the source-compatibility shims that keep the repository's legacy
    /// <c>SkippableFact</c> and <c>Skip.If</c> call sites working on xunit.v3.
    /// </summary>
    public class SkipShimTests
    {
        private const string Prefix = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.SkipShims.SkipShimTests.";

        /// <summary>
        /// The shims replace a package that has no xunit.v3 release, so their behaviour is defined
        /// entirely by this repository and nothing else checks it. The outcome is what matters: a
        /// conditional skip reported as a failure would break every leg that skips tests it cannot
        /// run, and one reported as a pass would let a test that never ran count as one that did.
        /// </summary>
        [Fact]
        public void GivenTestsUsingTheSkipShims_WhenTheyRun_ThenEachIsReportedWithTheOutcomeItsConditionAsksFor()
        {
            TestAssetRun run = TestAssetRunner.Run("SkipShims");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [Prefix + "SkipIfTrue_IsSkipped"] = "NotExecuted",
                    [Prefix + "SkipIfFalse_Runs"] = "Passed",
                    [Prefix + "SkipIfNotFalse_IsSkipped"] = "NotExecuted",
                    [Prefix + "SkipIfNotTrue_Runs"] = "Passed",
                    [Prefix + "SkipWithReason_IsSkipped"] = "NotExecuted",
                    [Prefix + "SkippableTheory_SkipsPerRow(skip: True)"] = "NotExecuted",
                    [Prefix + "SkippableTheory_SkipsPerRow(skip: False)"] = "Passed",
                });

            Assert.Equal(0, run.ExitCode);
        }

        /// <summary>
        /// A skipped test is only actionable if the report says why it was skipped, so the reason
        /// given at the call site has to survive as far as the output.
        /// </summary>
        [Fact]
        public void GivenATestSkippedWithAReason_WhenItRuns_ThenTheReasonReachesTheOutput()
        {
            TestAssetRun run = TestAssetRunner.Run("SkipShims");

            Assert.Contains("a distinctive skip reason", run.Output, StringComparison.Ordinal);
        }
    }
}
