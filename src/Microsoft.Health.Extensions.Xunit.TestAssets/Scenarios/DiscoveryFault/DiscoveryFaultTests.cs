// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.DiscoveryFault
{
    /// <summary>
    /// A class whose fixture argument sets cannot be expanded, so discovering it throws.
    /// </summary>
    /// <remarks>
    /// The class declares one dimension and the method below declares two, the second of which
    /// names no flag. A dimension that names no flag means "use the class-level dimension in this
    /// position", and the class has no second position, so the expansion has nothing to inherit and
    /// refuses. The misconfiguration itself is not the point: it is simply the cheapest way to make
    /// discovery of a real class throw, which is what this scenario is for.
    /// </remarks>
    [AssetArgumentSets(AssetDataStore.Sql | AssetDataStore.Cosmos)]
    public class DiscoveryFaultTests : IClassFixture<AssetFixture>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DiscoveryFaultTests"/> class.
        /// </summary>
        /// <param name="fixture">The variant fixture injected by the custom executor.</param>
        public DiscoveryFaultTests(AssetFixture fixture)
        {
            Fixture = fixture;
        }

        /// <summary>
        /// Gets the variant fixture this class was constructed with.
        /// </summary>
        protected AssetFixture Fixture { get; }

        /// <summary>
        /// Never runs: discovery of this class throws before any test case is produced.
        /// </summary>
        [Fact]
        [TwoDimensionArgumentSets(AssetDataStore.Sql | AssetDataStore.Cosmos, AssetOtherDimension.None)]
        public void NeverRuns()
        {
            Assert.True(true);
        }
    }
}
