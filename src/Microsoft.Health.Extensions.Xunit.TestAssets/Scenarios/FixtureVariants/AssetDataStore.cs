// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants
{
    /// <summary>
    /// The data stores a fixture variant can be built for. Fixture argument sets are required
    /// to be flags enums.
    /// </summary>
    [Flags]
    public enum AssetDataStore
    {
        /// <summary>
        /// SQL Server.
        /// </summary>
        Sql = 1,

        /// <summary>
        /// Cosmos DB.
        /// </summary>
        Cosmos = 2,
    }
}
