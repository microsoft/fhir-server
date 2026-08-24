// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.DiscoveryFault;
using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ClassAttributeFault
{
    /// <summary>
    /// A class whose own fixture argument set declaration cannot be read, so the fault belongs to the
    /// class rather than to any one of its methods.
    /// </summary>
    /// <remarks>
    /// The class carries two different fixture argument set attributes, so asking it for the single
    /// one it declares throws before the walk over its methods begins. That is what makes this a
    /// class-level fault: every method is lost at once, so each one gets its own failure standing in
    /// for it. The methods declare different traits so that failures carrying only their own
    /// declarations can be told apart from failures carrying every method's pooled together.
    /// </remarks>
    [AssetArgumentSets(AssetDataStore.Sql)]
    [TwoDimensionArgumentSets(AssetDataStore.Sql, AssetOtherDimension.Some)]
    public class ClassAttributeFaultTests
    {
        /// <summary>
        /// Never runs. Being declared first, it is the method whose ordinary traits a pooled failure
        /// would most plausibly have borrowed, which is what the assertions look for.
        /// </summary>
        [Fact]
        [Trait("Category", "DeclaredFirst")]
        public void FirstNeverRuns()
        {
            Assert.True(true);
        }

        /// <summary>
        /// Never runs, and declares a trait no earlier method does, together with an argument set of
        /// its own, so a leg selecting on both at once only sees the failure if it carries both.
        /// </summary>
        [Fact]
        [Trait("Category", "DeclaredLast")]
        [AssetArgumentSets(AssetDataStore.Cosmos)]
        public void SecondNeverRuns()
        {
            Assert.True(true);
        }
    }
}
