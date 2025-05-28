// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Azure.Cosmos;

namespace Microsoft.Health.Fhir.CosmosDb.Core.Configs
{
    public class CosmosDataStoreConfiguration
    {
        public string Host { get; set; }

        public string Key { get; set; }

        /// <summary>
        /// When true, the application will use the managed identity of the Azure resource to authenticate to Cosmos DB.
        /// Key configuration will be ignored if this is set to true.
        /// </summary>
        public bool UseManagedIdentity { get; set; }

        public bool AllowDatabaseCreation { get; set; } = true;

        public bool AllowCollectionSetup { get; set; } = true;

        public string DatabaseId { get; set; }

        public int? InitialDatabaseThroughput { get; set; }

        public ConnectionMode ConnectionMode { get; set; } = ConnectionMode.Direct;

        // Default value is indefinate, recommended value is 20m - 24 hours. Setting to 1 hour.
        public TimeSpan? IdleTcpConnectionTimeout { get; set; } = TimeSpan.FromHours(1);

        // Default value is 5 seconds, recommended value is 1 second.
        public TimeSpan? OpenTcpConnectionTimeout { get; set; } = TimeSpan.FromSeconds(1);

        // Default value is 30, recommended value is 30. Leaving null to use the SDK default.
        public int? MaxRequestsPerTcpConnection { get; set; }

        // Default value is 65535, recommended value is 65535. Leaving null to use the SDK default.
        public int? MaxTcpConnectionsPerEndpoint { get; set; }

        // Default value is PortReuseMode.ReuseUnicastPort, recommended value is PortReuseMode.ReuseUnicastPort. Leaving null to use the SDK default.
        public PortReuseMode? PortReuseMode { get; set; }

        // Default value is true, recommended value is true. Value is true for explicit configuration.
        public bool EnableTcpConnectionEndpointRediscovery { get; set; } = true;

        public ConsistencyLevel? DefaultConsistencyLevel { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a configuration class")]
        public IList<string> PreferredLocations { get; set; }

        public int DataMigrationBatchSize { get; set; } = 100;

        /// <summary>
        /// Retry options that are fed into the Cosmos DB sdk for individual database calls.
        /// </summary>
        public CosmosDataStoreRetryOptions RetryOptions { get; } = new CosmosDataStoreRetryOptions { MaxNumberOfRetries = 3, MaxWaitTimeInSeconds = 5 };

        /// <summary>
        /// Allows more generous retry options when processing batches to avoid actions with 429 response codes.
        /// </summary>
        public CosmosDataStoreRetryOptions IndividualBatchActionRetryOptions { get; } = new CosmosDataStoreRetryOptions { MaxNumberOfRetries = 18, MaxWaitTimeInSeconds = 90 };

        public int? ContinuationTokenSizeLimitInKb { get; set; }

        /// <summary>
        /// The maximum number of seconds to spend fetching search result pages when the first page comes up with fewer results than requested.
        /// This time includes the time to fetch the first page.
        /// </summary>
        public int SearchEnumerationTimeoutInSeconds { get; set; } = 30;

        /// <summary>
        /// Uses query statistics to determine the optimal level of partition parallelism needed to return results
        /// </summary>
        public bool UseQueryStatistics { get; set; }

        /// <summary>
        /// A list of Search Parameter URIs that will be enabled on first initialization
        /// </summary>
        public HashSet<string> InitialSortParameterUris { get; } = new();

        /// <summary>
        /// Options to determine if the parallel query execution is needed across physical partitions to speed up the selective queries
        /// </summary>
        public CosmosDataStoreParallelQueryOptions ParallelQueryOptions { get; } = new CosmosDataStoreParallelQueryOptions { MaxQueryConcurrency = 500 };

        /// <summary>
        /// Option to print Diagnostics returned from Cosmos SDK for each response
        /// </summary>
        public bool LogSdkDiagnostics { get; set; } = false;

        /// <summary>
        /// This represent the end to end elapsed time of the request. If the request is
        /// still in progress it will return the current elapsed time since the start of
        /// the request.
        /// </summary>
        public bool LogSdkClientElapsedTime { get; set; } = false;
    }
}
