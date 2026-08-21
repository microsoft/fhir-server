// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.DiscoveryFault;
using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.MismatchedDimension
{
    /// <summary>
    /// A class one of whose methods declares a different enum than the class does in the same
    /// fixture argument set dimension.
    /// </summary>
    /// <remarks>
    /// The merge pairs dimensions by position while the executor binds fixture arguments by type, and
    /// only convention keeps the two agreeing. Where they disagree the method's variants carry no
    /// value for the dimension the class declared, and so none of the traits a CI leg selects by: the
    /// leg would run none of this method's tests and still report success. The discoverer refuses the
    /// declaration instead, so the mistake is a red test rather than a silent absence.
    /// </remarks>
    [AssetArgumentSets(AssetDataStore.Sql | AssetDataStore.Cosmos)]
    public class MismatchedDimensionTests : IClassFixture<AssetFixture>
    {
        private readonly AssetFixture _fixture;

        /// <summary>
        /// Initializes a new instance of the <see cref="MismatchedDimensionTests"/> class.
        /// </summary>
        /// <param name="fixture">The variant fixture injected by the custom executor.</param>
        public MismatchedDimensionTests(AssetFixture fixture)
        {
            _fixture = fixture;
        }

        /// <summary>
        /// Declares <see cref="AssetOtherDimension"/> where the class declares
        /// <see cref="AssetDataStore"/>, so discovery of this method fails and a failing test case
        /// stands in for it. It never runs, which is why it can assert nothing.
        /// </summary>
        [Fact]
        [OtherDimensionFirstArgumentSets(AssetOtherDimension.Some)]
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
