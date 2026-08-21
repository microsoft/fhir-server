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

            Assert.NotEqual(0, run.ExitCode);
        }

        /// <summary>
        /// The failing test case cannot be relied on to carry the cause, because the class fixture it
        /// inherits fails to build first and that failure is what the report shows. The console line
        /// the discoverer writes is the only place the original exception survives, so it is the part
        /// worth pinning: without it a fault would be reported with no way to find out what it was.
        /// </summary>
        [Fact]
        public void GivenAClassThatCannotBeExpanded_WhenItIsDiscovered_ThenTheCauseIsWrittenToTheOutput()
        {
            TestAssetRun run = TestAssetRunner.Run("DiscoveryFault");

            Assert.Contains("[FixtureArgumentSets] ERROR", run.Output, StringComparison.Ordinal);
            Assert.Contains("IndexOutOfRangeException", run.Output, StringComparison.Ordinal);
        }

        /// <summary>
        /// Reporting the fault as a test is only worth anything if the legs that would have run the
        /// class still select it. CI legs pick their tests with a positive trait filter, and the
        /// variants this class never produced are what would have carried those traits, so a fault
        /// case with no traits of its own is filtered out and the leg passes with the class silently
        /// missing - which is the failure this whole mechanism exists to prevent, only harder to see.
        /// The case therefore declares every argument set value the class asked for.
        /// </summary>
        [Fact]
        public void GivenAClassThatCannotBeExpanded_WhenTestsAreSelectedByArgumentSetTrait_ThenTheFaultIsStillReported()
        {
            TestAssetRun run = TestAssetRunner.Run("DiscoveryFault", filterTrait: "AssetDataStore=Sql");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ErrorCaseName] = "Failed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }
    }
}
