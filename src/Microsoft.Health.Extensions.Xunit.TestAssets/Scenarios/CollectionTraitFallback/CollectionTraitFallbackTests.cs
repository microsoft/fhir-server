// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.DiscoveryFault;
using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;
using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ThrowingTrait;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CollectionTraitFallback
{
    /// <summary>
    /// A class whose discovery faults, whose traits cannot be read in one go, and whose only trait
    /// comes from the collection it joined. Expected: 1 result, failed, carrying that trait.
    /// </summary>
    /// <remarks>
    /// The throwing trait attribute forces the failure's traits to be gathered one attribute at a
    /// time. Gathering them from the class and the method alone would silently drop the collection's
    /// trait, because the class does not declare it - and the export and E2E legs select positively,
    /// so a leg naming that trait would match nothing and report success with the method missing.
    /// That is the same silence reporting discovery faults exists to break, so the fallback has to be
    /// no quieter than the ordinary read it stands in for.
    /// </remarks>
    [Collection("CollectionTraitFallbackProbe")]
    public class CollectionTraitFallbackTests
    {
        /// <summary>
        /// Never runs: it declares two fixture argument set attributes, so which one applies cannot be
        /// determined. It also carries a trait attribute that throws.
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
