// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Covers the traits a fixture argument set variant puts on the test cases discovered from it.
    /// </summary>
    /// <remarks>
    /// Every CI leg selects by trait, so these decide what a leg runs. A test case that reaches the
    /// runner without the injected data store trait cannot be selected by a leg naming one, and a
    /// variant that kept only the injected trait would lose the category the export and E2E legs
    /// name alongside it. Either way the leg reports success with tests it was meant to run absent,
    /// and nothing in its output says so.
    /// </remarks>
    public class VariantTraitsTests
    {
        private const string ScenarioClass = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.VariantTraits.VariantTraitsTests";

        /// <summary>
        /// Unfiltered, both tests are present in both variants and the malformed one fails.
        /// </summary>
        [Fact]
        public void GivenAClassWithAMalformedTest_WhenItIsRunUnfiltered_ThenEveryVariantOfBothTestsIsReported()
        {
            TestAssetRun run = TestAssetRunner.Run("VariantTraits");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ScenarioClass + ".MalformedFactIsStillReportedToAFilteringLeg (Sql)"] = "Failed",
                    [ScenarioClass + ".MalformedTheoryIsStillReportedToAFilteringLeg (Sql)"] = "Failed",
                    [ScenarioClass + ".MalformedFactIsStillReportedToAFilteringLeg (Cosmos)"] = "Failed",
                    [ScenarioClass + ".MalformedTheoryIsStillReportedToAFilteringLeg (Cosmos)"] = "Failed",
                    [ScenarioClass + ".HealthyTestKeepsBothItsOwnTraitAndTheInjectedOne (Sql)"] = "Passed",
                    [ScenarioClass + ".HealthyTestKeepsBothItsOwnTraitAndTheInjectedOne (Cosmos)"] = "Passed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }

        /// <summary>
        /// xunit builds an error test case for a test it cannot run, and builds it with no traits at
        /// all. A leg naming a data store would then select neither variant of the malformed test and
        /// report success, so the traits of the variant it was discovered from have to be put back.
        /// </summary>
        [Fact]
        public void GivenAMalformedTest_WhenALegSelectsOneDataStore_ThenItsFailureIsStillSelected()
        {
            TestAssetRun run = TestAssetRunner.Run("VariantTraits", filterTrait: "AssetDataStore=Sql");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ScenarioClass + ".MalformedFactIsStillReportedToAFilteringLeg (Sql)"] = "Failed",
                    [ScenarioClass + ".MalformedTheoryIsStillReportedToAFilteringLeg (Sql)"] = "Failed",
                    [ScenarioClass + ".HealthyTestKeepsBothItsOwnTraitAndTheInjectedOne (Sql)"] = "Passed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }

        /// <summary>
        /// The compound shape the export and E2E legs select by. A test runs only if it carries every
        /// named trait, so injecting the data store trait must add to the test's own rather than
        /// replace them - for the error case xunit built as much as for the healthy one, since the
        /// error case gets every trait it has this way and would otherwise carry only the injected one.
        /// </summary>
        [Fact]
        public void GivenTestsCarryingTheirOwnTrait_WhenALegSelectsOnBothTraits_ThenBothAreStillSelected()
        {
            TestAssetRun run = TestAssetRunner.Run("VariantTraits", filterQueryTraits: "(AssetDataStore=Sql)&(Category=ExportLongRunning)");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ScenarioClass + ".MalformedFactIsStillReportedToAFilteringLeg (Sql)"] = "Failed",
                    [ScenarioClass + ".MalformedTheoryIsStillReportedToAFilteringLeg (Sql)"] = "Failed",
                    [ScenarioClass + ".HealthyTestKeepsBothItsOwnTraitAndTheInjectedOne (Sql)"] = "Passed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }
    }
}
