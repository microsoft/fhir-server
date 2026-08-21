// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.PartialDiscoveryFault
{
    /// <summary>
    /// A class where one method's fixture argument set cannot be expanded and the methods around it
    /// are declared normally.
    /// </summary>
    /// <remarks>
    /// The failing method is declared between the two healthy ones so that the scenario covers both
    /// sides of it: a method discovered before the failure and a method discovered after it. The
    /// class takes no fixture, so nothing can fail ahead of the reported case and the message it
    /// carries is the discoverer's own.
    /// </remarks>
    public class PartialDiscoveryFaultTests
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
        /// Never runs: the argument set it asks for names no value, so it expands to nothing. It
        /// carries an ordinary trait so that a scenario can check the reported failure keeps it.
        /// </summary>
        [Fact]
        [Trait("AssetCategory", "PartialFault")]
        [AssetArgumentSets((AssetDataStore)0)]
        public void NeverRuns()
        {
            Assert.True(true);
        }

        /// <summary>
        /// Runs normally, and is declared after the failing method.
        /// </summary>
        [Fact]
        public void RunsAfterTheFault()
        {
            Assert.True(true);
        }
    }
}
