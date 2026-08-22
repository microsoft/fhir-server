// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ThrowingArgumentSets
{
    /// <summary>
    /// A class whose argument set attribute cannot be constructed, so none of its tests can be built.
    /// </summary>
    /// <remarks>
    /// The failure standing in for the lost tests still has to carry the data store trait the tests
    /// would have carried, which can only come from metadata here. A leg selecting positively on a
    /// data store, as the export and E2E legs do, would otherwise see nothing at all and pass.
    /// </remarks>
    [ThrowingArgumentSets(AssetDataStore.Sql | AssetDataStore.Cosmos)]
    public class ThrowingArgumentSetsTests : IClassFixture<AssetFixture>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ThrowingArgumentSetsTests"/> class.
        /// </summary>
        /// <param name="fixture">The variant fixture injected by the custom executor.</param>
        public ThrowingArgumentSetsTests(AssetFixture fixture)
        {
            Fixture = fixture;
        }

        /// <summary>
        /// Gets the variant fixture this class was constructed with.
        /// </summary>
        protected AssetFixture Fixture { get; }

        /// <summary>
        /// Never runs: the class carries an argument set attribute that cannot be constructed.
        /// </summary>
        [Fact]
        public void NeverRuns()
        {
            Assert.True(true);
        }
    }
}
