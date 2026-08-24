// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Covers what the retrying test attributes do with a test that exceeds its timeout.
    /// </summary>
    /// <remarks>
    /// A timeout is the failure a retry harness exists for: a test that runs long because a
    /// dependency was slow once is the definition of flaky. It reaches the retry decision as
    /// <c>Xunit.Sdk.TestTimeoutException</c>, whose name contains "Xunit" and so matches the
    /// substring test that classifies deterministic assertion failures - which are deliberately not
    /// retried. A timeout is carved out of that match for exactly this reason, and these tests are
    /// what would notice if the carve-out were removed: without it a timeout is denied every attempt
    /// it was configured for.
    /// </remarks>
    public class RetryTimeoutTests
    {
        private const string ClassName = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.RetryTimeouts.RetryTimeoutTests.";

        /// <summary>
        /// A test that times out once and then completes has to be retried into a pass, and one
        /// that times out on every attempt has to be reported failed exactly once.
        /// </summary>
        [Fact]
        public void GivenTestsThatExceedTheirTimeout_WhenTheRunCompletes_ThenTheFlakyOneIsRetriedIntoAPass()
        {
            TestAssetRun run = TestAssetRunner.Run("RetryTimeouts");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ClassName + "FlakyTimeout_IsRetriedAndPasses"] = "Passed",
                    [ClassName + "AlwaysTimesOut_IsReportedFailedOnce"] = "Failed",
                });
        }
    }
}
