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
    /// Covers an argument set attribute declaring two dimensions of the same enum type.
    /// </summary>
    /// <remarks>
    /// Dimensions are declared by position but bound to the fixture's constructor by type, so two of
    /// the same type cannot be told apart: the second is dropped and every variant is built with the
    /// first value, while still being named - and traited - for the combination it was supposed to
    /// be. Every combination would report as having run, none of them would have, and the run would
    /// be green. On <c>origin/main</c> the merge used <c>Dictionary.Add</c> and this threw; the
    /// rewrite made it a silent overwrite, so the refusal below is what keeps it loud.
    /// </remarks>
    public class DuplicateDimensionTests
    {
        private const string ScenarioClass = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.DuplicateDimension.DuplicateDimensionTests";

        /// <summary>
        /// The refusal is reported as a failure standing in for the offending method, under each of
        /// the combinations it would have claimed, while the method that declared nothing unusual
        /// runs normally.
        /// </summary>
        [Fact]
        public void GivenAMethodDeclaringTwoDimensionsOfOneType_WhenItIsDiscovered_ThenItIsReportedAndItsSiblingStillRuns()
        {
            TestAssetRun run = TestAssetRunner.Run("DuplicateDimension");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ScenarioClass + ".NeverRuns (fixture argument set discovery: Sql)"] = "Failed",
                    [ScenarioClass + ".NeverRuns (fixture argument set discovery: Cosmos)"] = "Failed",
                    [ScenarioClass + ".NeverRuns (fixture argument set discovery: Sql, Cosmos)"] = "Failed",
                    [ScenarioClass + ".SiblingStillRuns (Sql)"] = "Passed",
                    [ScenarioClass + ".SiblingStillRuns (Cosmos)"] = "Passed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }

        /// <summary>
        /// The failure names the attribute and the type it repeated, because the declaration that
        /// caused it is legal C# and reads as though it asks for every pairing of the two.
        /// </summary>
        [Fact]
        public void GivenAMethodDeclaringTwoDimensionsOfOneType_WhenItIsDiscovered_ThenTheFailureNamesTheRepeatedType()
        {
            TestAssetRun run = TestAssetRunner.Run("DuplicateDimension");

            Assert.Contains("DuplicateDimensionArgumentSetsAttribute", run.Output, StringComparison.Ordinal);
            Assert.Contains("AssetDataStore", run.Output, StringComparison.Ordinal);
        }

        /// <summary>
        /// The leg's own filter shape. A failure the leg cannot see is no better than the silent
        /// expansion the refusal replaced, so a leg excluding the other data store has to be left
        /// with a failure of its own. The stand-in for the combination naming both stores carries
        /// both traits and is excluded with them, which is why the per-value stand-ins matter.
        /// </summary>
        [Fact]
        public void GivenAMethodDeclaringTwoDimensionsOfOneType_WhenALegExcludesTheOtherDataStore_ThenTheFailureIsStillReported()
        {
            TestAssetRun run = TestAssetRunner.Run("DuplicateDimension", filterNotTrait: "AssetDataStore=Cosmos");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ScenarioClass + ".NeverRuns (fixture argument set discovery: Sql)"] = "Failed",
                    [ScenarioClass + ".SiblingStillRuns (Sql)"] = "Passed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }
    }
}
