// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.CosmosDb.Configs
{
    public class CosmosDataStoreParallelQueryOptions
    {
        /// <summary>
        /// Gets the maximum degree of parallelism for the SDK to use when querying physical partitions in parallel.
        /// </summary>
        public int MaxQueryConcurrency { get; set; } = Environment.ProcessorCount * 10;

        /// <summary>
        /// Enables the parallelism if sequential query execution takes more time
        /// </summary>
        public bool EnableConcurrencyIfQueryExceedsTimeLimit { get; set; }
    }
}
