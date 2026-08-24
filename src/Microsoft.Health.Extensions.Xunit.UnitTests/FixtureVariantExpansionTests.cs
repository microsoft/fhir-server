// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Verifies that a test class carrying fixture argument sets is expanded into one variant per
    /// argument set, and that each variant reports under its own display name.
    /// </summary>
    public class FixtureVariantExpansionTests
    {
        private const string TestName = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants.FixtureVariantTests.EachVariantIsReportedUnderItsOwnName";

        /// <summary>
        /// Without the argument set suffix every variant reports under the same name, so the
        /// variants collide in the results and a failure cannot be attributed to the data store
        /// that produced it.
        /// </summary>
        [Fact]
        public void GivenAClassWithFixtureArgumentSets_WhenTheRunCompletes_ThenEachVariantIsNamedAfterItsArgumentSet()
        {
            TestAssetRun run = TestAssetRunner.Run("FixtureVariants");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [TestName + " (Sql)"] = "Passed",
                    [TestName + " (Cosmos)"] = "Passed",
                });
        }
    }
}
