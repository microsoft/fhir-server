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
    /// Verifies that one method whose fixture argument sets cannot be expanded costs only that
    /// method, and that the failure standing in for it is selected by the same filters it was.
    /// </summary>
    public class PartialDiscoveryFaultTests
    {
        private const string ScenarioClass = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.PartialDiscoveryFault.PartialDiscoveryFaultTests";
        private const string ErrorCaseSql = ScenarioClass + ".NeverRuns (fixture argument set discovery: Sql)";
        private const string ErrorCaseCosmos = ScenarioClass + ".NeverRuns (fixture argument set discovery: Cosmos)";

        /// <summary>
        /// Test cases are published to xUnit as the discoverer walks the class's methods, so a
        /// failure let out of that walk leaves the methods already published running while the
        /// methods after it are silently dropped - and reports that as a single failure claiming
        /// the whole class never ran, which is wrong in both directions. Expanding each method
        /// separately keeps the blast radius to the method that is actually misdeclared.
        /// </summary>
        [Fact]
        public void GivenOneMethodThatCannotBeExpanded_WhenTheClassIsDiscovered_ThenTheOtherMethodsStillRun()
        {
            TestAssetRun run = TestAssetRunner.Run("PartialDiscoveryFault");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ScenarioClass + ".RunsBeforeTheFault"] = "Passed",
                    [ScenarioClass + ".RunsAfterTheFault"] = "Passed",
                    [ErrorCaseSql] = "Failed",
                    [ErrorCaseCosmos] = "Failed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }

        /// <summary>
        /// The failure has to say which method was lost, otherwise a class of many methods reports
        /// a fault with no indication of where to look for it.
        /// </summary>
        [Fact]
        public void GivenOneMethodThatCannotBeExpanded_WhenTheClassIsDiscovered_ThenTheFailureNamesOnlyThatMethod()
        {
            TestAssetRun run = TestAssetRunner.Run("PartialDiscoveryFault");

            Assert.Contains("PartialDiscoveryFaultTests.NeverRuns", run.Output, StringComparison.Ordinal);
            Assert.Contains("Other methods of the class were discovered normally", run.Output, StringComparison.Ordinal);
        }

        /// <summary>
        /// CI legs combine ordinary traits with argument set values in filters such as
        /// <c>(DataStore=CosmosDb)&amp;(Category=ExportLongRunning)</c>. The argument set half of
        /// such a filter is covered elsewhere; this covers the ordinary half, which the failing
        /// method declares itself. Without it the leg that would have run the method passes with
        /// the method silently missing, which is the failure this mechanism exists to prevent.
        /// </summary>
        [Fact]
        public void GivenOneMethodThatCannotBeExpanded_WhenTestsAreSelectedByItsOrdinaryTrait_ThenTheFaultIsStillReported()
        {
            TestAssetRun run = TestAssetRunner.Run("PartialDiscoveryFault", filterTrait: "AssetCategory=PartialFault");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ErrorCaseSql] = "Failed",
                    [ErrorCaseCosmos] = "Failed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }
    }
}
