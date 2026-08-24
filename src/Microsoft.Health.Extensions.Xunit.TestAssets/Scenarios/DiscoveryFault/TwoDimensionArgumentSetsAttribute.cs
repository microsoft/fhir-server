// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.DiscoveryFault
{
    /// <summary>
    /// Declares two fixture argument set dimensions, so a method carrying it declares more
    /// dimensions than a class that declares only one.
    /// </summary>
    public sealed class TwoDimensionArgumentSetsAttribute : FixtureArgumentSetsAttribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TwoDimensionArgumentSetsAttribute"/> class.
        /// </summary>
        /// <param name="first">The first dimension.</param>
        /// <param name="second">The second dimension.</param>
        public TwoDimensionArgumentSetsAttribute(AssetDataStore first, AssetOtherDimension second)
            : base(first, second)
        {
        }
    }
}
