// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.EmptyArgumentSet
{
    /// <summary>
    /// A class declaring a fixture argument set that names no value, so it expands to no variants.
    /// </summary>
    /// <remarks>
    /// The method asks to be expanded over an argument set of zero, which names no flag, so the
    /// product of the declared dimensions is empty and the method produces no test cases at all.
    /// The class deliberately takes no fixture, so nothing else can fail ahead of the reported case
    /// and the message it carries is the one the discoverer wrote.
    /// </remarks>
    public class EmptyArgumentSetTests
    {
        /// <summary>
        /// Never runs: the argument set it asks for expands to nothing.
        /// </summary>
        [Fact]
        [AssetArgumentSets((AssetDataStore)0)]
        public void NeverRuns()
        {
            Assert.True(true);
        }
    }
}
