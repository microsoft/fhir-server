// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.DiscoveryFault;
using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.MethodAttributeFault
{
    /// <summary>
    /// A class where reading one method's fixture argument set attribute is itself what fails, and the
    /// methods around it are declared normally.
    /// </summary>
    /// <remarks>
    /// The failing method carries two different fixture argument set attributes, so asking it for the
    /// single one it declares throws before any expansion is attempted. This is a different failure
    /// from a method whose argument set expands to nothing: it happens while reading the attribute
    /// rather than while using it, which is the step that used to run for every method of the class at
    /// once and so cost the whole class its tests. The class takes no fixture, so nothing can fail
    /// ahead of the reported case and the message it carries is the discoverer's own. One sibling
    /// declares an argument set of its own, and a different one, so a fault case that borrowed a
    /// sibling's values instead of using the failing method's can be told apart from one that did not.
    /// </remarks>
    public class MethodAttributeFaultTests
    {
        /// <summary>
        /// Runs normally, and is declared before the failing method.
        /// </summary>
        [Fact]
        public void RunsBeforeTheFault()
        {
            Assert.True(true);
        }

        /// <summary>
        /// Never runs: it declares two fixture argument set attributes, so which one applies cannot be
        /// determined.
        /// </summary>
        [Fact]
        [AssetArgumentSets(AssetDataStore.Sql)]
        [TwoDimensionArgumentSets(AssetDataStore.Sql, AssetOtherDimension.Some)]
        public void NeverRuns()
        {
            Assert.True(true);
        }

        /// <summary>
        /// Runs normally, and is declared after the failing method. It declares an argument set of its
        /// own so that the class has argument set values that can still be read once the failing
        /// method's cannot.
        /// </summary>
        [Fact]
        [AssetArgumentSets(AssetDataStore.Cosmos)]
        public void RunsAfterTheFault()
        {
            Assert.True(true);
        }
    }
}
