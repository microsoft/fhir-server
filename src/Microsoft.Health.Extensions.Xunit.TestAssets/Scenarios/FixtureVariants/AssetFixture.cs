// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants
{
    /// <summary>
    /// A class fixture whose constructor argument is supplied by the fixture argument set,
    /// which is what the custom executor injects.
    /// </summary>
    public class AssetFixture
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AssetFixture"/> class.
        /// </summary>
        /// <param name="dataStore">The data store this variant was constructed for.</param>
        public AssetFixture(AssetDataStore dataStore)
        {
            DataStore = dataStore;
        }

        /// <summary>
        /// Gets the data store this variant was constructed for.
        /// </summary>
        public AssetDataStore DataStore { get; }
    }
}
