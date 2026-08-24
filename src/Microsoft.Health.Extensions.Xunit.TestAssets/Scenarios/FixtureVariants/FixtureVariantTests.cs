// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
        /// Passes for every variant, and ties the reported name to the fixture it actually got.
        /// Asserting only that the fixture holds some valid store would hold equally for both
        /// variants, so a transposed mapping - or the same value injected into both - would go
        /// unnoticed while the display names still looked right.
        /// </summary>
        [Fact]
        public void EachVariantIsReportedUnderItsOwnName()
        {
            string displayName = TestContext.Current.Test.TestDisplayName;

            Assert.Contains(
                $"({_fixture.DataStore})",
                displayName,
                StringComparison.Ordinal);
        }
    }
}
