// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CollectionTraitFallback
{
    /// <summary>
    /// A collection definition that declares a trait its members inherit, and declares it nowhere else.
    /// </summary>
    /// <remarks>
    /// Xunit v3 gives every member of a collection the traits its definition carries, which is how a
    /// class can end up filtered on a trait that appears nowhere in its own source. That propagation
    /// is the whole reason this scenario exists: it makes the collection the only place the trait can
    /// be read from.
    /// </remarks>
    [CollectionDefinition("CollectionTraitFallbackProbe")]
    [Trait("Category", "CollectionTraitFallbackProbe")]
    public class CollectionTraitFallbackCollection
    {
    }
}
