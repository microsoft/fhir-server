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
    /// Verifies that a method whose fixture argument set attribute cannot even be read costs only
    /// that method, rather than every method of its class.
    /// </summary>
    public class MethodAttributeFaultTests
    {
        private const string ScenarioClass = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.MethodAttributeFault.MethodAttributeFaultTests";
        private const string SqlErrorCaseName = ScenarioClass + ".NeverRuns (fixture argument set discovery: Sql)";
        private const string SqlSomeErrorCaseName = ScenarioClass + ".NeverRuns (fixture argument set discovery: Sql, Some)";

        /// <summary>
        /// Reading the attribute is a separate step from expanding it, and it can fail on its own: a
        /// method carrying two different fixture argument set attributes has no single one to expand.
        /// Reading every method's attribute up front, before the walk that isolates each method, put
        /// that failure outside the isolation and cost the whole class its tests - reported as one
        /// failure claiming the class never ran, with the healthy methods gone and no sign of it.
        /// Reading each method's attribute inside the walk keeps the loss to the method at fault.
        /// </summary>
        [Fact]
        public void GivenOneMethodWhoseAttributeCannotBeRead_WhenTheClassIsDiscovered_ThenTheOtherMethodsStillRun()
        {
            TestAssetRun run = TestAssetRunner.Run("MethodAttributeFault");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ScenarioClass + ".RunsBeforeTheFault"] = "Passed",
                    [ScenarioClass + ".RunsAfterTheFault (Cosmos)"] = "Passed",
                    [SqlErrorCaseName] = "Failed",
                    [SqlSomeErrorCaseName] = "Failed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }

        /// <summary>
        /// The failure has to carry the values the failing method itself declared, so that the leg
        /// which would have run it is the leg that sees it. Asking the method for the single attribute
        /// it declares is what threw, and answering that with nothing left the failure carrying only
        /// values borrowed from a sibling - reported to the Cosmos leg, which would never have run
        /// this method, and hidden from the SQL leg, which would. Reading each of the method's
        /// attributes on its own keeps the failure where its tests were.
        /// </summary>
        [Fact]
        public void GivenOneMethodWhoseAttributeCannotBeRead_WhenTestsAreSelectedByItsOwnArgumentSetTrait_ThenTheFaultIsStillReported()
        {
            TestAssetRun run = TestAssetRunner.Run("MethodAttributeFault", filterTrait: "AssetDataStore=Sql");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [SqlErrorCaseName] = "Failed",
                    [SqlSomeErrorCaseName] = "Failed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }

        /// <summary>
        /// The other side of the same guarantee: the sibling's value must not leak onto the failure.
        /// A failure tagged with a value its method never declared is a red test on a leg that would
        /// never have run it, and - worse - is dropped by that leg's exclusion filter in the runs
        /// where the fault should have been reported elsewhere.
        /// </summary>
        [Fact]
        public void GivenOneMethodWhoseAttributeCannotBeRead_WhenTestsAreSelectedByASiblingsArgumentSetTrait_ThenOnlyTheSiblingRuns()
        {
            TestAssetRun run = TestAssetRunner.Run("MethodAttributeFault", filterTrait: "AssetDataStore=Cosmos");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ScenarioClass + ".RunsAfterTheFault (Cosmos)"] = "Passed",
                });

            Assert.Equal(0, run.ExitCode);
        }

        /// <summary>
        /// The failure has to name the method whose attribute could not be read, otherwise a class of
        /// many methods reports a fault with no indication of which declaration to go and fix.
        /// </summary>
        [Fact]
        public void GivenOneMethodWhoseAttributeCannotBeRead_WhenTheClassIsDiscovered_ThenTheFailureNamesOnlyThatMethod()
        {
            TestAssetRun run = TestAssetRunner.Run("MethodAttributeFault");

            Assert.Contains("MethodAttributeFaultTests.NeverRuns", run.Output, StringComparison.Ordinal);
            Assert.Contains("Other methods of the class were discovered normally", run.Output, StringComparison.Ordinal);
        }
    }
}
