// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Covers the merge of a method-level fixture argument set declaration with its class's. The
    /// merge runs dimension by dimension - a dimension naming at least one flag overrides the
    /// class's, one naming none inherits it - and it decides which variants of a test exist at all.
    /// Getting it wrong drops tests from a run that still reports success, which is the one failure
    /// mode this whole mechanism exists to prevent.
    /// </summary>
    public class ArgumentSetOverrideTests
    {
        private const string ScenarioClass = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ArgumentSetOverride.ArgumentSetOverrideTests";

        /// <summary>
        /// The whole merge in one run: a method declaring nothing keeps both of the class's
        /// dimensions, and each method narrowing one dimension keeps the class's values in the other.
        /// Asserting the exact set is what makes a variant that quietly stopped being discovered
        /// visible - a count alone would still pass if one variant were replaced by another.
        /// </summary>
        [Fact]
        public void GivenMethodsNarrowingOneDimension_WhenTheyAreDiscovered_ThenEachInheritsTheClassValuesForTheOther()
        {
            TestAssetRun run = TestAssetRunner.Run("ArgumentSetOverride");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ScenarioClass + ".InheritsBothDimensions (Sql, Some)"] = "Passed",
                    [ScenarioClass + ".InheritsBothDimensions (Cosmos, Some)"] = "Passed",
                    [ScenarioClass + ".OverridesTheFirstDimensionOnly (Cosmos, Some)"] = "Passed",
                    [ScenarioClass + ".OverridesTheSecondDimensionOnly (Sql, Some)"] = "Passed",
                    [ScenarioClass + ".OverridesTheSecondDimensionOnly (Cosmos, Some)"] = "Passed",
                });

            Assert.Equal(0, run.ExitCode);
        }

        /// <summary>
        /// Each variant asserts that the fixture it was handed matches the name it runs under, so a
        /// passing run is also evidence the merged values reached the fixture rather than only the
        /// display name. Selecting by one dimension's value shows the pairing from the other side:
        /// only the variants built for that value may answer, including the ones that inherited it
        /// rather than declaring it.
        /// </summary>
        [Fact]
        public void GivenMergedArgumentSets_WhenTestsAreSelectedByOneDimension_ThenOnlyTheVariantsBuiltForItRun()
        {
            TestAssetRun run = TestAssetRunner.Run("ArgumentSetOverride", filterTrait: "AssetDataStore=Sql");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ScenarioClass + ".InheritsBothDimensions (Sql, Some)"] = "Passed",
                    [ScenarioClass + ".OverridesTheSecondDimensionOnly (Sql, Some)"] = "Passed",
                });

            Assert.Equal(0, run.ExitCode);
        }
    }
}
