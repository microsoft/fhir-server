// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.SerializedVariants
{
    /// <summary>
    /// The second class in the same name-only collection as <see cref="FirstSerializedVariantTests"/>,
    /// so that the scenario covers variants of different classes and not only variants of one class.
    /// </summary>
    [AssetArgumentSets(AssetDataStore.Sql | AssetDataStore.Cosmos)]
    [Collection("SerializedVariants")]
    public class SecondSerializedVariantTests : IClassFixture<AssetFixture>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SecondSerializedVariantTests"/> class.
        /// </summary>
        /// <param name="fixture">The variant fixture injected by the custom executor.</param>
        public SecondSerializedVariantTests(AssetFixture fixture)
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

        /// <summary>
        /// Ties the variant's reported name to the fixture it was actually given, so that sharing a
        /// collection between variants cannot quietly hand them each other's fixture.
        /// </summary>
        [Fact]
        public void ReceivesTheFixtureItsNameClaims()
        {
            Assert.Contains(
                $"({Fixture.DataStore})",
                TestContext.Current.Test.TestDisplayName,
                System.StringComparison.Ordinal);
        }
    }
}
