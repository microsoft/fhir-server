// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.DuplicateDimension
{
    /// <summary>
    /// A class one of whose methods declares two fixture argument set dimensions of the same enum
    /// type.
    /// </summary>
    /// <remarks>
    /// Dimensions are declared by position but bound to the fixture's constructor by type, so two of
    /// the same type cannot be told apart: the second is dropped and the first value is used for
    /// both. The combinations that were meant to differ would all be the same run, each reported
    /// under a name claiming otherwise - a suite reporting coverage it does not have. The discoverer
    /// refuses the declaration instead, so the mistake is a red test rather than a false green.
    /// </remarks>
    [AssetArgumentSets(AssetDataStore.Sql | AssetDataStore.Cosmos)]
    public class DuplicateDimensionTests : IClassFixture<AssetFixture>
    {
        private readonly AssetFixture _fixture;

        /// <summary>
        /// Initializes a new instance of the <see cref="DuplicateDimensionTests"/> class.
        /// </summary>
        /// <param name="fixture">The variant fixture injected by the custom executor.</param>
        public DuplicateDimensionTests(AssetFixture fixture)
        {
            _fixture = fixture;
        }

        /// <summary>
        /// Declares <see cref="AssetDataStore"/> in both of its dimensions, so discovery of this
        /// method fails and a failing test case stands in for it. It never runs, which is why it can
        /// assert nothing.
        /// </summary>
        [Fact]
        [DuplicateDimensionArgumentSets(AssetDataStore.Sql, AssetDataStore.Cosmos)]
        public void NeverRuns()
        {
            throw new InvalidOperationException("This method must never be executed: its discovery is expected to fail.");
        }

        /// <summary>
        /// Declares nothing of its own, so it expands normally. A fault on one method must not cost
        /// its siblings their variants.
        /// </summary>
        [Fact]
        public void SiblingStillRuns()
        {
            string displayName = TestContext.Current.Test.TestDisplayName;

            Assert.Contains($"({_fixture.DataStore})", displayName, StringComparison.Ordinal);
        }
    }
}
