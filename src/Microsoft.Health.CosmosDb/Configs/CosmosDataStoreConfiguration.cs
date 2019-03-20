// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;

namespace Microsoft.Health.CosmosDb.Configs
{
    public class CosmosDataStoreConfiguration
    {
        public string Host { get; set; }

        public string Key { get; set; }

        public string DatabaseId { get; set; }

        public int? InitialDatabaseThroughput { get; set; }

        public ConnectionMode ConnectionMode { get; set; } = ConnectionMode.Direct;

        public Protocol ConnectionProtocol { get; set; } = Protocol.Tcp;

        public ConsistencyLevel? DefaultConsistencyLevel { get; set; }

        public bool AllowDatabaseCreation { get; set; } = true;

        public Uri RelativeDatabaseUri => string.IsNullOrEmpty(DatabaseId) ? null : UriFactory.CreateDatabaseUri(DatabaseId);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a configuration class")]
        public IList<string> PreferredLocations { get; set; }

        public int DataMigrationBatchSize { get; set; } = 100;

        public CosmosDataStoreRetryOptions RetryOptions { get; } = new CosmosDataStoreRetryOptions();

        public int? ContinuationTokenSizeLimitInKb { get; set; }

        public Uri GetRelativeCollectionUri(string collectionId)
        {
            return string.IsNullOrEmpty(DatabaseId) || string.IsNullOrEmpty(collectionId) ? null : UriFactory.CreateDocumentCollectionUri(DatabaseId, collectionId);
        }

        public Uri GetAbsoluteCollectionUri(string collectionId)
        {
            var relativeCollectionUri = GetRelativeCollectionUri(collectionId);

            return string.IsNullOrEmpty(Host) || relativeCollectionUri == null ? null : new Uri(new Uri(Host), relativeCollectionUri);
        }
    }
}
