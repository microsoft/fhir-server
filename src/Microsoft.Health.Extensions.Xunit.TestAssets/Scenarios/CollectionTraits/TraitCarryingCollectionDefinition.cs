// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CollectionTraits
{
    /// <summary>
    /// Defines a collection that carries a trait of its own.
    /// </summary>
    /// <remarks>
    /// Whether a collection's traits land on the tests of every class in it decides what several CI
    /// legs run. Those legs exclude a category, and a class sharing a collection with an excluded
    /// one would then be excluded too - silently, because an exclusion filter reports success for
    /// the tests it did not select. This scenario is what says which of the two xunit does.
    /// </remarks>
    [CollectionDefinition("TraitCarryingCollection")]
    [Trait("Category", "CollectionOwned")]
    public class TraitCarryingCollectionDefinition
    {
    }
}
