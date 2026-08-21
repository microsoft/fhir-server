// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Verifies that a fault belonging to a whole class is reported so that every leg which would
    /// have run any of its tests still sees a failure, and no leg sees one it should not.
    /// </summary>
    /// <remarks>
    /// A class-level fault loses every method at once, which is where a merged failure does the most
    /// damage. One failure carrying every method's traits is dropped by a leg excluding any one of
    /// them; one carrying only the first method's is invisible to a leg selecting by another's.
    /// Either way a leg passes green with tests missing, and nothing in its output says so. The
    /// failures are therefore reported one per lost method, per combination that method would have
    /// run under.
    /// </remarks>
    public class ClassAttributeFaultTests
    {
        private const string ScenarioClass = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ClassAttributeFault.ClassAttributeFaultTests";
        private const string FirstSqlCase = ScenarioClass + ".FirstNeverRuns (fixture argument set discovery: Sql)";
        private const string FirstSqlSomeCase = ScenarioClass + ".FirstNeverRuns (fixture argument set discovery: Sql, Some)";
        private const string SecondSqlCase = ScenarioClass + ".SecondNeverRuns (fixture argument set discovery: Sql)";
        private const string SecondSqlSomeCase = ScenarioClass + ".SecondNeverRuns (fixture argument set discovery: Sql, Some)";
        private const string SecondCosmosCase = ScenarioClass + ".SecondNeverRuns (fixture argument set discovery: Cosmos)";

        /// <summary>
        /// A fault in the class's own declaration loses every method, so every method gets a failure
        /// standing in for it, under each combination it would have run.
        /// </summary>
        /// <remarks>
        /// Reporting one failure for the class was the obvious thing and is wrong: a class of many
        /// methods that disagree about their traits cannot be represented by one case, because a
        /// filter selects or drops that case whole.
        /// </remarks>
        [Fact]
        public void GivenAClassWhoseOwnAttributeCannotBeRead_WhenItIsDiscovered_ThenEveryLostMethodGetsAFailure()
        {
            TestAssetRun run = TestAssetRunner.Run("ClassAttributeFault");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [FirstSqlCase] = "Failed",
                    [FirstSqlSomeCase] = "Failed",
                    [SecondSqlCase] = "Failed",
                    [SecondSqlSomeCase] = "Failed",
                    [SecondCosmosCase] = "Failed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }

        /// <summary>
        /// The class's declaration cannot be read as the single attribute the expansion would have
        /// used, but each attribute on its own can be. Answering the failure with no values at all
        /// would leave every failure carrying no argument set trait, and a leg selecting by one would
        /// pass with the whole class missing.
        /// </summary>
        [Fact]
        public void GivenAClassDeclaringTwoArgumentSetAttributes_WhenItIsDiscovered_ThenTheFailuresCarryBothDeclarations()
        {
            TestAssetRun run = TestAssetRunner.Run("ClassAttributeFault", filterTrait: "AssetDataStore=Sql");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [FirstSqlCase] = "Failed",
                    [FirstSqlSomeCase] = "Failed",
                    [SecondSqlCase] = "Failed",
                    [SecondSqlSomeCase] = "Failed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }

        /// <summary>
        /// This is the shape the repository's own E2E and export legs use: a query selecting on an
        /// argument set value and an ordinary trait at once. A failure missing either half matches
        /// nothing, so this is the only filter that pins both halves reaching the same case.
        /// </summary>
        [Fact]
        public void GivenAClassLevelFault_WhenTestsAreSelectedByACompoundQuery_ThenTheMatchingFailureIsReported()
        {
            TestAssetRun run = TestAssetRunner.Run(
                "ClassAttributeFault",
                filterQueryTraits: "(AssetDataStore=Cosmos)&(Category=DeclaredLast)");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [SecondCosmosCase] = "Failed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }

        /// <summary>
        /// The other half of the compound query: only the second method declares
        /// <c>AssetDataStore.Cosmos</c>, and only the first declares <c>Category=DeclaredFirst</c>, so
        /// no test would ever have run under both. A failure matching this query would mean the
        /// failures are carrying traits pooled across methods - which is what makes a leg excluding
        /// one method's trait drop the failures standing in for all the others.
        /// </summary>
        [Fact]
        public void GivenAClassLevelFault_WhenAQuerySelectsACombinationNoMethodDeclared_ThenNothingMatches()
        {
            TestAssetRun run = TestAssetRunner.Run(
                "ClassAttributeFault",
                filterQueryTraits: "(AssetDataStore=Cosmos)&(Category=DeclaredFirst)");

            Assert.Empty(run.Results);
        }

        /// <summary>
        /// Two overloads that both fail discovery are two lost methods, so both have to be reported.
        /// Identifying a failure by its method name alone gave them one identity between them, and
        /// xunit kept a single case - leaving one method's loss invisible.
        /// </summary>
        /// <remarks>
        /// The failures share a display name, because that is built from the method name too, so this
        /// counts results rather than naming them. Each overload is reported under both combinations
        /// its two declarations expand to, so four failures stand for the two lost methods.
        /// </remarks>
        [Fact]
        public void GivenTwoOverloadsThatBothFailDiscovery_WhenTheyAreDiscovered_ThenBothAreReported()
        {
            TestAssetRun run = TestAssetRunner.Run("OverloadedFault");

            Assert.Equal(0, run.ErrorCount);
            Assert.Equal(
                4,
                run.Results.Count(result =>
                    string.Equals(result.Outcome, "Failed", StringComparison.Ordinal) &&
                    result.Name != null &&
                    result.Name.Contains("OverloadedFaultTests.NeverRuns", StringComparison.Ordinal)));

            Assert.NotEqual(0, run.ExitCode);
        }
    }
}
