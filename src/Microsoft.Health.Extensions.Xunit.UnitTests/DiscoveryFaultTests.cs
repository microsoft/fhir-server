// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Verifies that a class whose fixture argument sets cannot be expanded is reported as a failure
    /// rather than dropped from the run.
    /// </summary>
    public class DiscoveryFaultTests
    {
        private const string ErrorCaseName = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.DiscoveryFault.DiscoveryFaultTests (fixture argument set discovery)";

        /// <summary>
        /// xUnit reports an exception thrown out of discovery only as an internal diagnostic, which
        /// is suppressed by default, and carries on without the class. A run containing any other
        /// healthy class therefore still reports success, so a broken expansion is indistinguishable
        /// from a class that has no tests. The discoverer turns the fault into a failing test case
        /// instead, which puts it in the results and in the exit code.
        /// </summary>
        [Fact]
        public void GivenAClassThatCannotBeExpanded_WhenItIsDiscovered_ThenTheFaultIsReportedAsAFailedTest()
        {
            TestAssetRun run = TestAssetRunner.Run("DiscoveryFault");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ErrorCaseName] = "Failed",
                });
        }
    }
}
