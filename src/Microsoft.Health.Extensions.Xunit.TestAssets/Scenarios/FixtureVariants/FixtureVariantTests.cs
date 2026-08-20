// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants
{
    /// <summary>
    /// Expanded into one variant per data store. Each variant must report under a distinct
    /// display name, otherwise a failure cannot be attributed to a fixture argument set.
    /// </summary>
    [AssetArgumentSets(AssetDataStore.Sql | AssetDataStore.Cosmos)]
    public class FixtureVariantTests : IClassFixture<AssetFixture>
    {
        private readonly AssetFixture _fixture;

        /// <summary>
        /// Initializes a new instance of the <see cref="FixtureVariantTests"/> class.
        /// </summary>
        /// <param name="fixture">The variant fixture injected by the custom executor.</param>
        public FixtureVariantTests(AssetFixture fixture)
        {
            _fixture = fixture;
        }

        /// <summary>
        /// Passes for every variant. The harness asserts on the reported display names, which
        /// must carry the fixture argument set as a suffix, and on the fixture actually
        /// receiving its argument.
        /// </summary>
        [Fact]
        public void EachVariantIsReportedUnderItsOwnName()
        {
            Assert.True(
                _fixture.DataStore == AssetDataStore.Sql || _fixture.DataStore == AssetDataStore.Cosmos,
                $"ASSET: fixture received an unexpected data store: {_fixture.DataStore}");
        }
    }
}
