// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.DiscoveryFault;
using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.PartiallyEmptyArgumentSet
{
    /// <summary>
    /// A class declaring a fixture argument set that names a value in one dimension and none in the
    /// other, so it expands to no variants.
    /// </summary>
    /// <remarks>
    /// The product of the declared dimensions is empty, exactly as it is when no dimension names
    /// anything, so the method produces no test cases and the failure standing in for it is all a
    /// leg can see. What differs is that one dimension does name a value, which is enough to keep
    /// the other dimension from being widened - so the stand-in carries the dimension that was
    /// declared and says nothing about the one that was not. A leg selecting positively on the
    /// undeclared dimension, which is how the E2E and export legs select a data store, matches
    /// nothing and reports success with the method's tests absent.
    /// <para>
    /// The class deliberately declares nothing itself. A class-level declaration would contribute
    /// its own combinations to the stand-in and hide the hole.
    /// </para>
    /// </remarks>
    public class PartiallyEmptyArgumentSetTests
    {
        /// <summary>
        /// Never runs: one of the two argument sets it asks for expands to nothing, so their product
        /// does too.
        /// </summary>
        [Fact]
        [TwoDimensionArgumentSets((AssetDataStore)0, AssetOtherDimension.Some)]
        public void NeverRuns()
        {
            Assert.True(true);
        }
    }
}
