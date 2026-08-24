// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.MixedFixtureAttributes
{
    /// <summary>
    /// A class carrying no class-level argument sets, where only some methods declare their own.
    /// A class shaped like this bypasses the discoverer's fast path yet has no class-level sets to
    /// expand, so its undecorated methods take a passthrough path that hands them to xUnit directly.
    /// That path has to preserve ordinary discovery, including expanding a theory's data rows:
    /// dropping them would silently shrink the suite rather than fail it.
    /// </summary>
    public class MixedAttributeTests
    {
        /// <summary>
        /// Declares its own argument sets, so this method is expanded into one variant per store.
        /// Its presence is what keeps the class off the wholly-undecorated fast path.
        /// </summary>
        [AssetArgumentSets(AssetDataStore.Sql | AssetDataStore.Cosmos)]
        [Fact]
        public void ExpandedMethod()
        {
            Assert.True(true);
        }

        /// <summary>
        /// Takes the passthrough path. A plain fact must still be discovered exactly once.
        /// </summary>
        [Fact]
        public void PassthroughFact()
        {
            Assert.True(true);
        }

        /// <summary>
        /// Takes the passthrough path while carrying data rows, which must all be discovered.
        /// </summary>
        /// <param name="value">The row value supplied by the data attribute.</param>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void PassthroughTheory(int value)
        {
            Assert.True(value >= 1, "ASSET: unexpected theory row value.");
        }
    }
}
