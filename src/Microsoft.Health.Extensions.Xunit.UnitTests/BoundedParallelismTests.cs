// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Verifies that substituting the collection runner does not remove the limit on how many
    /// collections run at once.
    /// </summary>
    public class BoundedParallelismTests
    {
        private const string ScenarioNamespace = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.BoundedParallelism.";

        /// <summary>
        /// xunit bounds collection parallelism inside its own assembly runner context, in the very
        /// dispatch this framework replaces in order to substitute its collection runner. Replacing
        /// it without carrying the bound over leaves the thread limit unenforced, so every collection
        /// in an assembly starts at once however the run was configured. That does not fail anything
        /// by itself, which is what makes it worth pinning: it shows up only as a machine under far
        /// more load than asked for, and as the flaky timing-sensitive tests that come with it.
        /// </summary>
        [Fact]
        public void GivenAThreadLimitOfOne_WhenCollectionsRun_ThenTheyDoNotOverlap()
        {
            TestAssetRun run = TestAssetRunner.Run("BoundedParallelism", maxThreads: "1");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ScenarioNamespace + "BoundedParallelismOneTests.Runs"] = "Passed",
                    [ScenarioNamespace + "BoundedParallelismTwoTests.Runs"] = "Passed",
                    [ScenarioNamespace + "BoundedParallelismThreeTests.Runs"] = "Passed",
                    [ScenarioNamespace + "BoundedParallelismFourTests.Runs"] = "Passed",
                });

            Assert.Equal(0, run.ExitCode);
        }
    }
}
