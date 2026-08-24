// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.DiscoveryFault;
using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ArgumentSetOverride
{
    /// <summary>
    /// A class fixture built from both fixture argument set dimensions, so a test can tell which
    /// value of each dimension it actually received rather than only which name it ran under.
    /// </summary>
    public class OverrideFixture
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OverrideFixture"/> class.
        /// </summary>
        /// <param name="dataStore">The value of the first dimension this variant was constructed for.</param>
        /// <param name="otherDimension">The value of the second dimension this variant was constructed for.</param>
        public OverrideFixture(AssetDataStore dataStore, AssetOtherDimension otherDimension)
        {
            DataStore = dataStore;
            OtherDimension = otherDimension;
        }

        /// <summary>
        /// Gets the value of the first dimension this variant was constructed for.
        /// </summary>
        public AssetDataStore DataStore { get; }

        /// <summary>
        /// Gets the value of the second dimension this variant was constructed for.
        /// </summary>
        public AssetOtherDimension OtherDimension { get; }
    }
}
