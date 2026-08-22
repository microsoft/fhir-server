// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.DiscoveryFault;
using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;
using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ThrowingTrait;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ThrowingAttributeConstructor
{
    /// <summary>
    /// A class whose discovery faults, and which carries an attribute whose constructor throws
    /// alongside the trait a CI leg would select on.
    /// Expected: 1 result, failed, still carrying the class trait.
    /// </summary>
    /// <remarks>
    /// Two things have to go wrong at once to reach the read this covers. The method carries a trait
    /// attribute that throws, which is what makes xUnit's own trait read fail and sends the failure
    /// down the per-attribute fallback. The class then carries an attribute whose constructor
    /// throws, and because a declaration's attributes are constructed together, that read fails for
    /// the class as a whole. If the fallback gives up on the declaration at that point, the class
    /// trait goes with it, and a leg selecting positively on <c>Category</c> cannot match the
    /// failure standing in for these tests - reporting success with the class missing.
    /// <para>
    /// The attribute that throws is not a trait attribute and knows nothing about traits. It just
    /// happens to sit on the same class, which is what makes this easy to arrive at without meaning
    /// to.
    /// </para>
    /// </remarks>
    [Trait("Category", "ThrowingConstructorProbe")]
    [ThrowingConstructor]
    public class ThrowingAttributeConstructorTests
    {
        /// <summary>
        /// Never runs: it declares two fixture argument set attributes, so which one applies cannot
        /// be determined. Its throwing trait attribute is what forces the fallback trait read.
        /// </summary>
        [Fact]
        [ThrowingTrait]
        [AssetArgumentSets(AssetDataStore.Sql)]
        [TwoDimensionArgumentSets(AssetDataStore.Sql, AssetOtherDimension.Some)]
        public void NeverRuns()
        {
            Assert.True(true);
        }
    }
}
