// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CollectionTraits
{
    /// <summary>
    /// Joins the trait-carrying collection and is also expanded into one variant per data store, the
    /// shape the shipping suite uses wherever a class both selects a data store and joins a
    /// collection to be serialised.
    /// </summary>
    /// <remarks>
    /// Each variant has to end up carrying both the trait the collection owns and the data store
    /// trait the expansion injects. Losing either one hides the variant from a leg naming it, and
    /// the legs that name both are the ones that select positively, so nothing would report the loss.
    /// </remarks>
    [Collection("TraitCarryingCollection")]
    [AssetArgumentSets(AssetDataStore.Sql | AssetDataStore.Cosmos)]
    public class VariantsInTheCollectionTests : IClassFixture<AssetFixture>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VariantsInTheCollectionTests"/> class.
        /// </summary>
        /// <param name="fixture">The variant fixture injected by the custom executor.</param>
        public VariantsInTheCollectionTests(AssetFixture fixture)
        {
            Fixture = fixture;
        }

        /// <summary>
        /// Gets the variant fixture this class was constructed with.
        /// </summary>
        protected AssetFixture Fixture { get; }

        /// <summary>
        /// Selected only by a leg naming both the collection's trait and this variant's data store.
        /// </summary>
        [Fact]
        public void CarriesBothTheCollectionTraitAndItsDataStore()
        {
            Assert.True(true);
        }
    }
}
