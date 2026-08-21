// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.VariantTraits
{
    /// <summary>
    /// A class whose variants must carry the argument set's trait onto every test case discovered
    /// from them, including the one xunit builds for a test it cannot run.
    /// </summary>
    /// <remarks>
    /// Every CI leg in this repository selects by trait, so a test case reaching the runner without
    /// the data store trait is one no leg selecting a data store can see. That is harmless for a
    /// passing test and silent for a broken one: the leg runs neither, and reports success either
    /// way. Both a malformed test and a healthy one carrying a trait of its own are declared here,
    /// because the two reach the runner by different routes.
    /// </remarks>
    [AssetArgumentSets(AssetDataStore.Sql | AssetDataStore.Cosmos)]
    public class VariantTraitsTests : IClassFixture<AssetFixture>
    {
        private readonly AssetFixture _fixture;

        /// <summary>
        /// Initializes a new instance of the <see cref="VariantTraitsTests"/> class.
        /// </summary>
        /// <param name="fixture">The variant fixture injected by the custom executor.</param>
        public VariantTraitsTests(AssetFixture fixture)
        {
            _fixture = fixture;
        }

        /// <summary>
        /// Declaring a parameter on a fact is a test xunit cannot run, so it reports the method as an
        /// error test case rather than discovering it normally. That case is built without traits,
        /// which is what makes it worth declaring here: the analyser rule against writing it is
        /// suppressed because the malformed declaration is the point.
        /// </summary>
        /// <param name="value">Never supplied: the declaration is deliberately malformed.</param>
#pragma warning disable xUnit1001 // Fact methods cannot have parameters
        [Fact]
        public void MalformedFactIsStillReportedToAFilteringLeg(int value)
#pragma warning restore xUnit1001
        {
            throw new InvalidOperationException("This method can never run: xunit reports it as an error instead.");
        }

        /// <summary>
        /// Carries a trait of its own alongside the injected one, in the shape the export and E2E legs
        /// select by - a data store and a category together. A variant that kept only the injected
        /// trait would drop this one and vanish from those legs.
        /// </summary>
        [Trait("Category", "ExportLongRunning")]
        [Fact]
        public void HealthyTestKeepsBothItsOwnTraitAndTheInjectedOne()
        {
            string displayName = TestContext.Current.Test.TestDisplayName;

            Assert.Contains($"({_fixture.DataStore})", displayName, StringComparison.Ordinal);
        }
    }
}
