// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FailThenSkipOnRetry
{
    /// <summary>
    /// Fails on its first attempt and skips on the next one, which is what a test guarded by a
    /// run-time condition does when that condition changes between attempts.
    /// </summary>
    /// <remarks>
    /// The platform re-runs failed tests in a new process, so the attempt is counted through a file
    /// named by the harness rather than in memory.
    /// </remarks>
    public class FailThenSkipOnRetryTests
    {
        /// <summary>The environment variable naming the file that counts attempts.</summary>
        public const string AttemptFileVariable = "XUNIT_EXT_ASSET_ATTEMPT_FILE";

        /// <summary>
        /// Fails once, then skips.
        /// </summary>
        [Fact]
        public void FailsOnTheFirstAttemptAndSkipsOnTheNext()
        {
            string attemptFile = Environment.GetEnvironmentVariable(AttemptFileVariable);

            if (string.IsNullOrEmpty(attemptFile))
            {
                throw new InvalidOperationException(
                    $"ASSET: {AttemptFileVariable} was not set, so this scenario cannot tell which attempt it is on.");
            }

            if (File.Exists(attemptFile))
            {
                Assert.Skip("ASSET: skipping on a later attempt, the way a run-time condition would.");
            }

            File.WriteAllText(attemptFile, "attempted");
            Assert.Fail("ASSET: failing on the first attempt.");
        }

        /// <summary>
        /// Fails once, then passes, so that the retry attempt has a test that actually runs.
        /// </summary>
        [Fact]
        public void FailsOnTheFirstAttemptAndPassesOnTheNext()
        {
            string attemptFile = Environment.GetEnvironmentVariable(AttemptFileVariable);

            if (string.IsNullOrEmpty(attemptFile))
            {
                throw new InvalidOperationException(
                    $"ASSET: {AttemptFileVariable} was not set, so this scenario cannot tell which attempt it is on.");
            }

            string companion = attemptFile + ".passing";

            if (File.Exists(companion))
            {
                return;
            }

            File.WriteAllText(companion, "attempted");
            Assert.Fail("ASSET: failing on the first attempt.");
        }
    }
}
