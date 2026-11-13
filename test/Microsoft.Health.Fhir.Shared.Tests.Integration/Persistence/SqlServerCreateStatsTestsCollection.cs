// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Runs <see cref="SqlServerCreateStatsTests"/> in a non-parallel collection. That class toggles
    /// the process-global, static <c>Search.ReferenceResourceTypeFilteredStats.IsEnabled</c> cache;
    /// serializing the collection prevents other parallel test classes from observing that flag while
    /// it is temporarily toggled here.
    /// </summary>
    [CollectionDefinition(SqlServerCreateStatsTests.SerialCollectionName, DisableParallelization = true)]
    public sealed class SqlServerCreateStatsTestsCollection
    {
    }
}
