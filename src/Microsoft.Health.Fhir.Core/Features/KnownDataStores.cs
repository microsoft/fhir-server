// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features
{
    public static class KnownDataStores
    {
        public const string CosmosDb = nameof(CosmosDb);

        public const string SqlServer = nameof(SqlServer);

        public static bool IsCosmosDbDataStore(string dataStoreName)
        {
            EnsureArg.IsNotNullOrWhiteSpace(dataStoreName, nameof(dataStoreName));

            return string.Equals(CosmosDb, dataStoreName, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSqlServerDataStore(string dataStoreName)
        {
            EnsureArg.IsNotNullOrWhiteSpace(dataStoreName, nameof(dataStoreName));

            return string.Equals(SqlServer, dataStoreName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
