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
    /// Covers a class whose argument set attribute cannot be constructed.
    /// </summary>
    /// <remarks>
    /// Reading an attribute runs its constructor, so this is a class the discoverer cannot expand and
    /// cannot interrogate the ordinary way either. The failure standing in for its lost tests still
    /// has to carry the data store trait those tests would have had: the export and E2E legs select
    /// positively on one, and a filter cannot match a trait that is not there, so those legs would
    /// run nothing and report success with a whole class missing.
    /// </remarks>
    public class ThrowingArgumentSetsTests
    {
        private const string SqlErrorCaseName = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ThrowingArgumentSets.ThrowingArgumentSetsTests.NeverRuns (fixture argument set discovery: Sql)";
        private const string CosmosErrorCaseName = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ThrowingArgumentSets.ThrowingArgumentSetsTests.NeverRuns (fixture argument set discovery: Cosmos)";

        /// <summary>
        /// Unfiltered, one failure stands in for the lost tests of each declared data store.
        /// </summary>
        [Fact]
        public void GivenAnAttributeThatCannotBeConstructed_WhenItIsDiscovered_ThenTheFaultIsReportedForEachDataStore()
        {
            TestAssetRun run = TestAssetRunner.Run("ThrowingArgumentSets");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [SqlErrorCaseName] = "Failed",
                    [CosmosErrorCaseName] = "Failed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }

        /// <summary>
        /// The shape that matters. A leg naming one data store has to see the failure for that store,
        /// which it can only do if the values were taken from metadata rather than from the attribute
        /// the discoverer was unable to construct.
        /// </summary>
        [Fact]
        public void GivenAnAttributeThatCannotBeConstructed_WhenALegSelectsOneDataStore_ThenThatLegStillSeesTheFailure()
        {
            TestAssetRun run = TestAssetRunner.Run("ThrowingArgumentSets", filterTrait: "AssetDataStore=Sql");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [SqlErrorCaseName] = "Failed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }

        /// <summary>
        /// The cause has to survive into the reported failure, or whoever reads the results is told a
        /// test failed without being told that the class never expanded.
        /// </summary>
        [Fact]
        public void GivenAnAttributeThatCannotBeConstructed_WhenItIsDiscovered_ThenTheCauseIsWrittenToTheOutput()
        {
            TestAssetRun run = TestAssetRunner.Run("ThrowingArgumentSets");

            Assert.Contains("This argument set attribute cannot be constructed.", run.Output, StringComparison.Ordinal);
        }
    }
}
