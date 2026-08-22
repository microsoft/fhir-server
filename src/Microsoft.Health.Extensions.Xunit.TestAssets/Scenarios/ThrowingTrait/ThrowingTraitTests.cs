// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.DiscoveryFault;
using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ThrowingTrait
{
    /// <summary>
    /// A class whose discovery faults on a method that also carries a trait attribute that throws.
    /// Expected: 1 result, failed, still carrying the ordinary traits its declaration named.
    /// </summary>
    /// <remarks>
    /// The failure standing in for the lost method has to carry the traits a CI leg selects on, and
    /// those are read from the method as a whole. One trait attribute refusing to produce its value
    /// must therefore not cost the method the traits its other attributes declared: a leg selecting
    /// positively on <c>Category</c> would not match the failure, and would report success with the
    /// method missing.
    /// </remarks>
    [Trait("Category", "ThrowingTraitProbe")]
    public class ThrowingTraitTests
    {
        /// <summary>
        /// Never runs: it declares two fixture argument set attributes, so which one applies cannot
        /// be determined. It also carries a trait attribute that throws, alongside ordinary ones.
        /// </summary>
        [Fact]
        [Trait("Owner", "ThrowingTraitOwner")]
        [ThrowingTrait]
        [AssetArgumentSets(AssetDataStore.Sql)]
        [TwoDimensionArgumentSets(AssetDataStore.Sql, AssetOtherDimension.Some)]
        public void NeverRuns()
        {
            Assert.True(true);
        }
    }
}
