// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CollectionTraits
{
    /// <summary>
    /// Joins a trait-carrying collection without declaring that trait itself.
    /// </summary>
    [Collection("TraitCarryingCollection")]
    public class JoinsTheCollectionTests
    {
        /// <summary>
        /// Present or absent under a filter depending on whether the collection's trait reached it.
        /// </summary>
        [Fact]
        public void InheritsWhateverTheCollectionCarries()
        {
            Assert.True(true);
        }
    }
}
