// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Covers a discovery failure whose traits have to be gathered one attribute at a time, and whose
    /// only trait comes from the collection its class joined.
    /// </summary>
    /// <remarks>
    /// Xunit v3 gives every member of a collection the traits its definition declares, so a class can
    /// be selected on a trait that appears nowhere in its own source - the propagation this PR exists
    /// because of. When one trait attribute throws, the failure standing in for the lost method has
    /// its traits gathered attribute by attribute instead, and that gathering has to reach as far as
    /// the ordinary read it replaces. Reading only the class and the method drops the collection's
    /// trait, and a leg selecting positively on it would then match nothing and report success with
    /// the method missing.
    /// </remarks>
    public class CollectionTraitFallbackTests
    {
        private const string OneDimensionErrorCaseName = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CollectionTraitFallback.CollectionTraitFallbackTests.NeverRuns (fixture argument set discovery: Sql)";
        private const string TwoDimensionErrorCaseName = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CollectionTraitFallback.CollectionTraitFallbackTests.NeverRuns (fixture argument set discovery: Sql, Some)";

        /// <summary>
        /// Unfiltered, the failure is reported and the run fails.
        /// </summary>
        [Fact]
        public void GivenAFaultWhoseTraitsMustBeGatheredSeparately_WhenItIsDiscovered_ThenTheFaultIsReported()
        {
            TestAssetRun run = TestAssetRunner.Run("CollectionTraitFallback");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [OneDimensionErrorCaseName] = "Failed",
                    [TwoDimensionErrorCaseName] = "Failed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }

        /// <summary>
        /// The shape that matters. A leg naming the collection's trait still has to see the failure,
        /// which it can only do if the fallback read the collection definition as well as the class.
        /// </summary>
        [Fact]
        public void GivenAFaultWhoseTraitsMustBeGatheredSeparately_WhenALegSelectsTheCollectionTrait_ThenThatLegStillSeesTheFailure()
        {
            TestAssetRun run = TestAssetRunner.Run("CollectionTraitFallback", filterTrait: "Category=CollectionTraitFallbackProbe");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [OneDimensionErrorCaseName] = "Failed",
                    [TwoDimensionErrorCaseName] = "Failed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }
    }
}
