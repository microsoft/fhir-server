// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.SerializedVariants
{
    /// <summary>
    /// One of two classes placed in the same name-only collection, each expanded into one variant
    /// per data store. A name-only <c>[Collection]</c> has no <c>[CollectionDefinition]</c> class
    /// behind it, which is the case the fixture argument set machinery has to detect by other means.
    /// </summary>
    /// <remarks>
    /// Classes are put in a collection to stop them running at the same time, usually because they
    /// share something external. Expanding a class into variants must not quietly undo that: if the
    /// variants land in collections of their own, xUnit is free to run them concurrently and the
    /// grouping the author asked for is lost.
    /// </remarks>
    [AssetArgumentSets(AssetDataStore.Sql | AssetDataStore.Cosmos)]
    [Collection("SerializedVariants")]
    public class FirstSerializedVariantTests : IClassFixture<AssetFixture>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FirstSerializedVariantTests"/> class.
        /// </summary>
        /// <param name="fixture">The variant fixture injected by the custom executor.</param>
        public FirstSerializedVariantTests(AssetFixture fixture)
        {
            Fixture = fixture;
        }

        /// <summary>
        /// Gets the variant fixture this class was constructed with.
        /// </summary>
        protected AssetFixture Fixture { get; }

        /// <summary>
        /// Fails if any other test in this collection was running at the same time.
        /// </summary>
        [Fact]
        public void RunsWithoutOverlappingTheOtherVariants()
        {
            ConcurrencyProbe.Occupy();

            Assert.False(
                ConcurrencyProbe.ObservedOverlap,
                "Two tests from the same [Collection] ran concurrently, so expanding a class into fixture argument set variants broke the collection's serialization.");
        }
    }
}
