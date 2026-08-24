// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Covers a declaration that names a value in one argument set dimension and none in the other.
    /// </summary>
    /// <remarks>
    /// Naming nothing in every dimension is already rescued: the failure standing in for the method
    /// is reported once per value each dimension's type declares, so a leg selecting positively on
    /// one of them still sees it. Naming nothing in only some of them is the same hole with a
    /// narrower mouth - the product is still empty, the method still produces no tests, but the
    /// stand-in says nothing about the dimension that named nothing and a leg selecting on that
    /// dimension still sees an empty, green run.
    /// </remarks>
    public class PartiallyEmptyArgumentSetTests
    {
        private const string SqlCase = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.PartiallyEmptyArgumentSet.PartiallyEmptyArgumentSetTests.NeverRuns (fixture argument set discovery: Sql, Some)";
        private const string CosmosCase = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.PartiallyEmptyArgumentSet.PartiallyEmptyArgumentSetTests.NeverRuns (fixture argument set discovery: Cosmos, Some)";

        private static Dictionary<string, string> EveryDataStore() =>
            new Dictionary<string, string>
            {
                [SqlCase] = "Failed",
                [CosmosCase] = "Failed",
            };

        /// <summary>
        /// The dimension that named nothing is widened to every value its type declares, so the
        /// failure is reported once per data store rather than once with no data store at all.
        /// </summary>
        [Fact]
        public void GivenADeclarationNamingNothingInOneDimension_WhenItExpandsToNoVariants_ThenTheFailureIsReportedForEveryValueOfThatDimension()
        {
            TestAssetRun run = TestAssetRunner.Run("PartiallyEmptyArgumentSet");

            TestAssetRunAssertions.PublishedExactly(run, EveryDataStore());
        }

        /// <summary>
        /// This is the shape of the E2E and export legs' filter. Without the widening the stand-in
        /// carries no data store trait, this selects nothing, and the leg passes with the method's
        /// tests missing.
        /// </summary>
        [Fact]
        public void GivenADeclarationNamingNothingInOneDimension_WhenALegSelectsPositivelyOnThatDimension_ThenTheFailureIsSelected()
        {
            TestAssetRun run = TestAssetRunner.Run("PartiallyEmptyArgumentSet", filterQueryTraits: "(AssetDataStore=Sql)");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string> { [SqlCase] = "Failed" });
        }

        /// <summary>
        /// The dimension that did name a value keeps it. Widening the one that named nothing must
        /// not spill into the one that did, or the failure would be reported under values the
        /// declaration explicitly did not ask for.
        /// </summary>
        [Fact]
        public void GivenADeclarationNamingNothingInOneDimension_WhenALegSelectsOnTheDeclaredDimension_ThenTheFailureIsSelected()
        {
            TestAssetRun run = TestAssetRunner.Run("PartiallyEmptyArgumentSet", filterQueryTraits: "(AssetOtherDimension=Some)");

            TestAssetRunAssertions.PublishedExactly(run, EveryDataStore());
        }
    }
}
