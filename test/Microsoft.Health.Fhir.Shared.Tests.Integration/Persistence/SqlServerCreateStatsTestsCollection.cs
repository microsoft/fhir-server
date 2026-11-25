// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Runs <see cref="SqlServerCreateStatsTests"/> in a non-parallel collection. That class toggles the
    /// <c>Search.ReferenceResourceTypeFilteredStats.IsEnabled</c> flag and asserts on the process-global, static
    /// statistics cache; serializing the collection prevents other parallel test classes from adding to that cache
    /// while the flag is temporarily toggled here.
    /// </summary>
    [CollectionDefinition(SqlServerCreateStatsTests.SerialCollectionName, DisableParallelization = true)]
    public sealed class SqlServerCreateStatsTestsCollection
    {
    }
}
