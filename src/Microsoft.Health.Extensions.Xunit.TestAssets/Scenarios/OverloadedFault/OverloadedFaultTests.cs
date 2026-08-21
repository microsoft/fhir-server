// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.DiscoveryFault;
using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.OverloadedFault
{
    /// <summary>
    /// A class where two overloads of the same test method both fail discovery, so two failures have
    /// to be reported against methods that share a name.
    /// </summary>
    /// <remarks>
    /// Each overload carries two different fixture argument set attributes, so asking either for the
    /// single one it declares throws and each is reported on its own. Overloads are what make this
    /// scenario worth having: anything identifying the reported failure by method name alone gives
    /// both failures the same identity, and one of them is then dropped - the method it stood for
    /// silently absent from a run that still reports the other.
    /// <para>
    /// xunit's own analyzer rejects overloaded test method names, which is why no such class exists
    /// in this repository and why this is defence rather than a live bug. The analyzer is suppressed
    /// here rather than the scenario dropped, because the rule is a warning a consuming project can
    /// turn off, and a fault case that loses a method is not something the reader of a green run can
    /// see.
    /// </para>
    /// </remarks>
#pragma warning disable xUnit1024
    public class OverloadedFaultTests
    {
        /// <summary>
        /// Never runs: it declares two fixture argument set attributes.
        /// </summary>
        /// <param name="value">Unused; present only to give this overload a distinct signature.</param>
        [Theory]
        [InlineData(1)]
        [AssetArgumentSets(AssetDataStore.Sql)]
        [TwoDimensionArgumentSets(AssetDataStore.Sql, AssetOtherDimension.Some)]
        public void NeverRuns(int value)
        {
            Assert.True(value > 0);
        }

        /// <summary>
        /// Never runs either, and shares its name with the overload above.
        /// </summary>
        /// <param name="value">Unused; present only to give this overload a distinct signature.</param>
        [Theory]
        [InlineData("x")]
        [AssetArgumentSets(AssetDataStore.Sql)]
        [TwoDimensionArgumentSets(AssetDataStore.Sql, AssetOtherDimension.Some)]
        public void NeverRuns(string value)
        {
            Assert.NotNull(value);
        }
    }
#pragma warning restore xUnit1024
}
