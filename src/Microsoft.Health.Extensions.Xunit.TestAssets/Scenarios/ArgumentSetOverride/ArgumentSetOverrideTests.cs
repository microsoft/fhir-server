// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.DiscoveryFault;
using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ArgumentSetOverride
{
    /// <summary>
    /// A class whose methods narrow one dimension of the class-level fixture argument set while
    /// inheriting the other. A method-level declaration replaces the class-level values dimension by
    /// dimension rather than wholesale: a dimension naming at least one flag overrides the class's,
    /// and a dimension naming none falls back to it. Both halves of that merge decide which variants
    /// of a test exist at all, so a mistake in either silently drops tests from a run that still
    /// reports success.
    /// </summary>
    [TwoDimensionArgumentSets(AssetDataStore.Sql | AssetDataStore.Cosmos, AssetOtherDimension.Some)]
    public class ArgumentSetOverrideTests : IClassFixture<OverrideFixture>
    {
        private readonly OverrideFixture _fixture;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArgumentSetOverrideTests"/> class.
        /// </summary>
        /// <param name="fixture">The variant fixture injected by the custom executor.</param>
        public ArgumentSetOverrideTests(OverrideFixture fixture)
        {
            _fixture = fixture;
        }

        /// <summary>
        /// Declaring nothing of its own leaves this method with the class's values in both
        /// dimensions.
        /// </summary>
        [Fact]
        public void InheritsBothDimensions()
        {
            AssertFixtureMatchesDisplayName();
        }

        /// <summary>
        /// The first dimension names a flag, so it replaces the class's two values with one; the
        /// second names none, so it keeps the class's. Reading the method's declaration as a whole
        /// rather than dimension by dimension would leave this method with no second dimension and
        /// so no variants at all.
        /// </summary>
        [Fact]
        [TwoDimensionArgumentSets(AssetDataStore.Cosmos, AssetOtherDimension.None)]
        public void OverridesTheFirstDimensionOnly()
        {
            AssertFixtureMatchesDisplayName();
        }

        /// <summary>
        /// The mirror image: the first dimension names no flag and falls back to the class's two
        /// values, while the second names one of its own. The fallback has to work at any position,
        /// not only the last.
        /// </summary>
        [Fact]
        [TwoDimensionArgumentSets((AssetDataStore)0, AssetOtherDimension.Some)]
        public void OverridesTheSecondDimensionOnly()
        {
            AssertFixtureMatchesDisplayName();
        }

        /// <summary>
        /// Ties the values the fixture was built from to the name the variant reports under.
        /// Asserting only that the fixture holds some valid value would hold for every variant
        /// alike, so a merge that produced the right number of variants from the wrong values -
        /// or built every fixture from the same one - would pass unnoticed.
        /// </summary>
        private void AssertFixtureMatchesDisplayName()
        {
            string displayName = TestContext.Current.Test.TestDisplayName;

            Assert.EndsWith(
                $" ({_fixture.DataStore}, {_fixture.OtherDimension})",
                displayName,
                StringComparison.Ordinal);
        }
    }
}
