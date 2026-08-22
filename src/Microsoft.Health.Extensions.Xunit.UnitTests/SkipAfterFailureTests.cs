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
    /// An attempt that reports a real result supersedes a failure held over from an earlier one, and
    /// a runtime skip is a real result. The earlier failure is therefore discarded and the test is
    /// reported skipped: the same supersession that lets a retry turn a failure into a pass. This is
    /// asserted rather than assumed because the alternative reading - that a failure seen once must
    /// always be reported - would be a defensible design and a silent change if it were made by
    /// accident.
    /// </remarks>
    public class SkipAfterFailureTests
    {
        private const string ClassName = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.SkipAfterFailure.SkipAfterFailureTests.";

        /// <summary>
        /// A test that fails, is retried, and then skips itself is reported skipped exactly once,
        /// with the superseded failure discarded rather than also reported.
        /// </summary>
        [Fact]
        public void GivenARetryingTestThatSkipsAfterFailing_WhenTheRunCompletes_ThenItIsReportedSkippedOnce()
        {
            TestAssetRun run = TestAssetRunner.Run("SkipAfterFailure");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string> { [ClassName + "FailsThenSkips"] = "Failed" });
        }
    }
}
