// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Covers a class that is both expanded per data store and uses the retrying test attributes,
    /// which is the shape most of this repository's integration tests take.
    /// </summary>
    /// <remarks>
    /// Expansion and retrying were only ever asserted apart. They meet in the test case the retry
    /// discoverers build, which is a different type from the one expansion writes to and copies the
    /// traits across by hand. A copy that lost or renamed a trait would leave a variant that no leg
    /// selecting positively on a data store could see, and such a leg reports success having run
    /// nothing.
    /// </remarks>
    public class RetryVariantTests
    {
        private const string ClassName = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.RetryVariants.RetryVariantTests.";

        /// <summary>
        /// Every retrying test is expanded per data store, theory rows included, and the flaky one
        /// still recovers on a later attempt within each variant.
        /// </summary>
        [Fact]
        public void GivenRetryingTestsInAnExpandedClass_WhenTheRunCompletes_ThenEveryVariantIsReported()
        {
            TestAssetRun run = TestAssetRunner.Run("RetryVariants");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ClassName + "PassingRetryFact (Sql)"] = "Passed",
                    [ClassName + "PassingRetryFact (Cosmos)"] = "Passed",
                    [ClassName + "FlakyRetryFact (Sql)"] = "Passed",
                    [ClassName + "FlakyRetryFact (Cosmos)"] = "Passed",
                    [ClassName + "RetryTheoryRow(value: 1) (Sql)"] = "Passed",
                    [ClassName + "RetryTheoryRow(value: 2) (Sql)"] = "Passed",
                    [ClassName + "RetryTheoryRow(value: 1) (Cosmos)"] = "Passed",
                    [ClassName + "RetryTheoryRow(value: 2) (Cosmos)"] = "Passed",
                    [ClassName + "MalformedRetryTheory (Sql)"] = "Failed",
                    [ClassName + "EmptyDataRetryTheory (Sql)"] = "Failed",
                    [ClassName + "MalformedRetryTheory (Cosmos)"] = "Failed",
                    [ClassName + "EmptyDataRetryTheory (Cosmos)"] = "Failed",
                });
        }

        /// <summary>
        /// The shape the export and E2E legs use. A retrying test has to be selectable by the same
        /// compound positive filter as any other, on both the data store the expansion gave it and
        /// the category its class declares.
        /// </summary>
        [Fact]
        public void GivenRetryingTestsInAnExpandedClass_WhenALegSelectsOnBothTraits_ThenOnlyThatDataStoreRuns()
        {
            TestAssetRun run = TestAssetRunner.Run(
                "RetryVariants",
                filterQueryTraits: "(AssetDataStore=Sql)&(Category=RetryVariant)");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ClassName + "PassingRetryFact (Sql)"] = "Passed",
                    [ClassName + "FlakyRetryFact (Sql)"] = "Passed",
                    [ClassName + "RetryTheoryRow(value: 1) (Sql)"] = "Passed",
                    [ClassName + "RetryTheoryRow(value: 2) (Sql)"] = "Passed",
                    [ClassName + "MalformedRetryTheory (Sql)"] = "Failed",
                    [ClassName + "EmptyDataRetryTheory (Sql)"] = "Failed",
                });
        }

        /// <summary>
        /// Trait filtering compares names without regard to case, and a retrying test that is also
        /// expanded goes through two separate pieces of trait handling to reach the runner. This
        /// pins that it still answers to the same filter either way.
        /// </summary>
        [Fact]
        public void GivenRetryingTestsInAnExpandedClass_WhenALegNamesTheTraitInAnotherCase_ThenTheSameTestsRun()
        {
            TestAssetRun run = TestAssetRunner.Run("RetryVariants", filterTrait: "assetdatastore=sql");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ClassName + "PassingRetryFact (Sql)"] = "Passed",
                    [ClassName + "FlakyRetryFact (Sql)"] = "Passed",
                    [ClassName + "RetryTheoryRow(value: 1) (Sql)"] = "Passed",
                    [ClassName + "RetryTheoryRow(value: 2) (Sql)"] = "Passed",
                    [ClassName + "MalformedRetryTheory (Sql)"] = "Failed",
                    [ClassName + "EmptyDataRetryTheory (Sql)"] = "Failed",
                });
        }

        /// <summary>
        /// A theory that declares no data is not a test that failed, it is a test that could not be
        /// built, and the reason has to reach the results rather than being replaced by whatever
        /// happens when the retrying case calls the method with no arguments.
        /// </summary>
        [Fact]
        public void GivenAMalformedRetryingTheory_WhenTheRunCompletes_ThenTheReportedFailureNamesTheMissingData()
        {
            TestAssetRun run = TestAssetRunner.Run("RetryVariants");

            Assert.Contains("No data found for", run.Output, System.StringComparison.Ordinal);
        }
    }
}
