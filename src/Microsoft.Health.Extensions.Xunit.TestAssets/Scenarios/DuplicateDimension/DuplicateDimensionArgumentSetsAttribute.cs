// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.DuplicateDimension
{
    /// <summary>
    /// Declares two fixture argument set dimensions of the same enum type.
    /// </summary>
    /// <remarks>
    /// This reads as though it asks for every pairing of two data stores - a primary and a
    /// secondary, say - which is exactly the kind of thing a test author would reach for. Fixture
    /// arguments are matched to a fixture's constructor by their enum type, so the two cannot be
    /// told apart, and what would actually run is the first value twice.
    /// </remarks>
    public sealed class DuplicateDimensionArgumentSetsAttribute : FixtureArgumentSetsAttribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DuplicateDimensionArgumentSetsAttribute"/> class.
        /// </summary>
        /// <param name="first">The first dimension.</param>
        /// <param name="second">The second dimension, of the same type as the first.</param>
        public DuplicateDimensionArgumentSetsAttribute(AssetDataStore first, AssetDataStore second)
            : base(first, second)
        {
        }
    }
}
