// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Pins what a retrying test reports when an attempt skips after an earlier one failed.
    /// </summary>
    /// <remarks>
    /// A pass supersedes a failure held over from an earlier attempt, because a pass is a claim that
    /// the code works. A skip makes no such claim - it says the attempt should not have run - so
    /// letting it supersede would erase a failure the test really did show, and report NotExecuted
    /// for a test that failed. The held failure therefore wins and the skip is dropped. This is
    /// asserted rather than assumed because the opposite reading - that the newest result always
    /// wins - is the simpler implementation and would be a silent regression.
    /// </remarks>
    public class SkipAfterFailureTests
    {
        private const string ClassName = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.SkipAfterFailure.SkipAfterFailureTests.";

        /// <summary>
        /// A test that fails, is retried, and then skips itself is reported failed exactly once, with
        /// the abstaining skip discarded rather than reported alongside it.
        /// </summary>
        [Fact]
        public void GivenARetryingTestThatSkipsAfterFailing_WhenTheRunCompletes_ThenItIsReportedFailedOnce()
        {
            TestAssetRun run = TestAssetRunner.Run("SkipAfterFailure");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string> { [ClassName + "FailsThenSkips"] = "Failed" });
        }
    }
}
