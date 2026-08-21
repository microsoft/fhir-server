// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.DiscoveryFault;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.MismatchedDimension
{
    /// <summary>
    /// Declares one fixture argument set dimension, taking a different enum than
    /// <see cref="FixtureVariants.AssetArgumentSetsAttribute"/> takes in the same position.
    /// </summary>
    /// <remarks>
    /// A method carrying this under a class carrying that one is the declaration the discoverer has
    /// to refuse: legal C#, reading as though it narrows the class's dimension, while in fact
    /// replacing it with an unrelated one.
    /// </remarks>
    public sealed class OtherDimensionFirstArgumentSetsAttribute : FixtureArgumentSetsAttribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OtherDimensionFirstArgumentSetsAttribute"/> class.
        /// </summary>
        /// <param name="first">The first, and only, dimension.</param>
        public OtherDimensionFirstArgumentSetsAttribute(AssetOtherDimension first)
            : base(first)
        {
        }
    }
}
