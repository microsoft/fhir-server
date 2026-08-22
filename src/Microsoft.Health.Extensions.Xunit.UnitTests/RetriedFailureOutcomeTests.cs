// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Records what the runner reports when a retried test stops failing without passing.
    /// </summary>
    /// <remarks>
    /// Every CI leg runs with retries and treats the runner's exit code as the verdict, because the
    /// results of the earlier attempts are published too and failing on those would cancel out every
    /// retry. That is only sound while a zero exit code means every failure was cleared, and this is
    /// the case where it does not: the retry counts a test that skipped as no longer failing, so the
    /// run exits zero with a real failure inside it.
    ///
    /// The legs close that gap outside the runner, in
    /// <c>build/jobs/scripts/Assert-RetriedFailuresPassed.ps1</c>, which requires every test that
    /// failed in an attempt to be recorded as passing in the final one. This test is what would
    /// notice if the runner ever started reporting this itself, at which point that script has
    /// nothing left to do.
    /// </remarks>
    public class RetriedFailureOutcomeTests
    {
        private const string ClassName = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FailThenSkipOnRetry.FailThenSkipOnRetryTests.";
        private const string AttemptFileVariable = "XUNIT_EXT_ASSET_ATTEMPT_FILE";

        /// <summary>
        /// Runs two tests that both fail first: one then passes, one then skips.
        /// </summary>
        [Fact]
        public void GivenARetriedTestThatSkipsInsteadOfPassing_WhenTheRunFinishes_ThenTheRunnerReportsSuccessAnyway()
        {
            string attemptFile = Path.Combine(Path.GetTempPath(), "xunit-ext-assets", $"attempt-{Guid.NewGuid():N}.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(attemptFile));

            try
            {
                TestAssetRun run = TestAssetRunner.Run(
                    "FailThenSkipOnRetry",
                    retryFailedTests: "2",
                    environment: new Dictionary<string, string> { [AttemptFileVariable] = attemptFile });

                // Both tests failed on the first attempt. The one that went on to skip is reported as
                // NotExecuted, which is indistinguishable from a test that was always going to skip,
                // and the run still exits zero: that is the whole of the exposure.
                TestAssetRunAssertions.PublishedExactly(
                    run,
                    new Dictionary<string, string>
                    {
                        [ClassName + "FailsOnTheFirstAttemptAndSkipsOnTheNext"] = "NotExecuted",
                        [ClassName + "FailsOnTheFirstAttemptAndPassesOnTheNext"] = "Passed",
                    });
            }
            finally
            {
                foreach (string path in new[] { attemptFile, attemptFile + ".passing" })
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        // A leaked temp file is not worth failing an otherwise good test over.
                    }
                }
            }
        }
    }
}
