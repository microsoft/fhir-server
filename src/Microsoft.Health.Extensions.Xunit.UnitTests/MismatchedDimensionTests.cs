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
    /// Covers a method whose argument set attribute names a different enum than its class's does in
    /// the same dimension.
    /// </summary>
    /// <remarks>
    /// The merge pairs dimensions by position while the executor binds fixture arguments by enum type,
    /// and nothing but convention keeps the two agreeing. Where they disagree the method's variants
    /// carry no value for the dimension the class declared, and so none of the traits the SQL and
    /// Cosmos legs select by: both legs would run none of this method's tests and both would still
    /// report success. The discoverer therefore refuses the declaration and reports a failure, which
    /// is loud, in place of an expansion that would have been silent.
    /// </remarks>
    public class MismatchedDimensionTests
    {
        private const string ScenarioClass = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.MismatchedDimension.MismatchedDimensionTests";

        /// <summary>
        /// The refusal is reported as a failure standing in for the offending method, under each of
        /// the class's combinations, while the method that declared nothing unusual runs normally.
        /// </summary>
        [Fact]
        public void GivenAMethodDeclaringADifferentDimensionThanItsClass_WhenItIsDiscovered_ThenItIsReportedAndItsSiblingStillRuns()
        {
            TestAssetRun run = TestAssetRunner.Run("MismatchedDimension");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ScenarioClass + ".NeverRuns (fixture argument set discovery: Sql)"] = "Failed",
                    [ScenarioClass + ".NeverRuns (fixture argument set discovery: Cosmos)"] = "Failed",
                    [ScenarioClass + ".NeverRuns (fixture argument set discovery: Some)"] = "Failed",
                    [ScenarioClass + ".SiblingStillRuns (Sql)"] = "Passed",
                    [ScenarioClass + ".SiblingStillRuns (Cosmos)"] = "Passed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }

        /// <summary>
        /// The failure names both enums and the position they disagree in, because the declaration
        /// that caused it is legal C# and reads as though it should work.
        /// </summary>
        [Fact]
        public void GivenAMethodDeclaringADifferentDimensionThanItsClass_WhenItIsDiscovered_ThenTheFailureNamesBothDimensions()
        {
            TestAssetRun run = TestAssetRunner.Run("MismatchedDimension");

            Assert.Contains("AssetOtherDimension", run.Output, StringComparison.Ordinal);
            Assert.Contains("AssetDataStore", run.Output, StringComparison.Ordinal);
            Assert.Contains("argument set dimension 0", run.Output, StringComparison.Ordinal);
        }

        /// <summary>
        /// The SQL leg's own filter shape. The refused method must still be reported to the leg that
        /// would have run it - a failure the leg cannot see is no better than the silent expansion the
        /// refusal replaced.
        /// </summary>
        [Fact]
        public void GivenAMethodDeclaringADifferentDimensionThanItsClass_WhenALegExcludesTheOtherDataStore_ThenTheFailureIsStillReported()
        {
            TestAssetRun run = TestAssetRunner.Run("MismatchedDimension", filterNotTrait: "AssetDataStore=Cosmos");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ScenarioClass + ".NeverRuns (fixture argument set discovery: Sql)"] = "Failed",
                    [ScenarioClass + ".NeverRuns (fixture argument set discovery: Some)"] = "Failed",
                    [ScenarioClass + ".SiblingStillRuns (Sql)"] = "Passed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }
    }
}
