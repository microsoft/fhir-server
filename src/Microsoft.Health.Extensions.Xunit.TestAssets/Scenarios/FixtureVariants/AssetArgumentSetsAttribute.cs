// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants
{
    /// <summary>
    /// Declares the fixture argument sets a test class is expanded over.
    /// </summary>
    public sealed class AssetArgumentSetsAttribute : FixtureArgumentSetsAttribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AssetArgumentSetsAttribute"/> class.
        /// </summary>
        /// <param name="dataStore">The data stores to expand the test class over.</param>
        public AssetArgumentSetsAttribute(AssetDataStore dataStore)
            : base(dataStore)
        {
        }
    }
}
