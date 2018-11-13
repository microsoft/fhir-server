// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;

namespace Microsoft.Health.Fhir.CosmosDb.Configs
{
    public class CosmosDataStoreConfiguration
    {
        public string Host { get; set; }

        public string Key { get; set; }

        public string DatabaseId { get; set; }

        public string CollectionId { get; set; }

        public ConnectionMode ConnectionMode { get; set; } = ConnectionMode.Direct;

        public Protocol ConnectionProtocol { get; set; } = Protocol.Tcp;

        public ConsistencyLevel? DefaultConsistencyLevel { get; set; }

        public bool AllowDatabaseCreation { get; set; } = true;

        public Uri RelativeDatabaseUri => string.IsNullOrEmpty(DatabaseId) ? null : UriFactory.CreateDatabaseUri(DatabaseId);

        public Uri RelativeCollectionUri => string.IsNullOrEmpty(DatabaseId) || string.IsNullOrEmpty(CollectionId) ? null : UriFactory.CreateDocumentCollectionUri(DatabaseId, CollectionId);

        public Uri AbsoluteCollectionUri => string.IsNullOrEmpty(Host) || RelativeCollectionUri == null ? null : new Uri(new Uri(Host), RelativeCollectionUri);

        public IList<string> PreferredLocations { get; set; }

        public int DataMigrationBatchSize { get; set; } = 100;

        public CosmosDataStoreRetryOptions RetryOptions { get; } = new CosmosDataStoreRetryOptions();
    }
}
