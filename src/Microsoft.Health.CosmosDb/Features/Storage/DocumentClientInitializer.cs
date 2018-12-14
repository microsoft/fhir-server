// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage.Versioning;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    public class DocumentClientInitializer : IDocumentClientInitializer
    {
        private readonly IDocumentClientTestProvider _testProvider;
        private readonly ILogger<DocumentClientInitializer> _logger;
        private readonly IUpgradeManager _upgradeManager;

        public DocumentClientInitializer(IDocumentClientTestProvider testProvider, ILogger<DocumentClientInitializer> logger, IUpgradeManager upgradeManager)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(upgradeManager, nameof(upgradeManager));
            EnsureArg.IsNotNull(testProvider, nameof(testProvider));

            _testProvider = testProvider;
            _logger = logger;
            _upgradeManager = upgradeManager;
        }

        /// <summary>
        /// Creates am unopened <see cref="DocumentClient"/> based on the given <see cref="CosmosDataStoreConfiguration"/>.
        /// </summary>
        /// <param name="configuration">The endpoint and collection settings</param>
        /// <returns>A <see cref="DocumentClient"/> instance</returns>
        public IDocumentClient CreateDocumentClient(CosmosDataStoreConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            _logger.LogInformation("Creating DocumentClient instance for {CollectionUri}", configuration.AbsoluteFhirCollectionUri);

            var connectionPolicy = new ConnectionPolicy
            {
                ConnectionMode = configuration.ConnectionMode,
                ConnectionProtocol = configuration.ConnectionProtocol,
                RetryOptions = new RetryOptions()
                {
                    MaxRetryAttemptsOnThrottledRequests = configuration.RetryOptions.MaxNumberOfRetries,
                    MaxRetryWaitTimeInSeconds = configuration.RetryOptions.MaxWaitTimeInSeconds,
                },
            };

            if (configuration.PreferredLocations != null && configuration.PreferredLocations.Any())
            {
                _logger.LogInformation("Setting DocumentClient PreferredLocations to {PreferredLocations}", string.Join(";", configuration.PreferredLocations));

                foreach (var preferredLocation in configuration.PreferredLocations)
                {
                    connectionPolicy.PreferredLocations.Add(preferredLocation);
                }
            }

            // Setting TypeNameHandling to any value other than 'None' will be flagged
            // as causing potential security issues
            var serializerSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DateFormatHandling = DateFormatHandling.IsoDateFormat,
                DateParseHandling = DateParseHandling.DateTimeOffset,
                DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
                TypeNameHandling = TypeNameHandling.None,
            };

            serializerSettings.Converters.Add(new StringEnumConverter());

            // By default, the Json.NET serializer uses 'F' instead of 'f' for fractions.
            // 'F' will omit the trailing digits if they are 0. You might end up getting something like '2018-02-07T20:04:49.97114+00:00'
            // where the fraction is actually 971140. Because the ordering is done as string,
            // if the values don't always have complete 7 digits, the comparison might not work properly.
            serializerSettings.Converters.Add(new IsoDateTimeConverter { DateTimeFormat = "o" });

            // TODO: Figure out how to expose a FhirDocumentClient
            return new DocumentClient(new Uri(configuration.Host), configuration.Key, serializerSettings, connectionPolicy, configuration.DefaultConsistencyLevel);
        }

        /// <summary>
        /// Perform a trivial query to establish a connection.
        /// DocumentClient.OpenAsync() is not supported when a token is used as the access key.
        /// </summary>
        /// <param name="client">The document client</param>
        /// <param name="configuration">The data store config</param>
        public async Task OpenDocumentClient(IDocumentClient client, CosmosDataStoreConfiguration configuration)
        {
            EnsureArg.IsNotNull(client, nameof(client));
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            _logger.LogInformation("Opening DocumentClient connection to {CollectionUri}", configuration.AbsoluteFhirCollectionUri);
            try
            {
                await _testProvider.PerformTest(client, configuration);

                _logger.LogInformation("Established DocumentClient connection to {CollectionUri}", configuration.AbsoluteFhirCollectionUri);
            }
            catch (Exception e)
            {
                _logger.LogCritical(e, "Failed to connect to DocumentClient collection {CollectionUri}", configuration.AbsoluteFhirCollectionUri);
                throw;
            }
        }

        /// <summary>
        /// Ensures that the necessary database and collection exist with the proper indexing policy and stored procedures
        /// </summary>
        /// <param name="documentClient">The <see cref="DocumentClient"/> instance to use for initialization.</param>
        /// <param name="cosmosDataStoreConfiguration">The data store configuration.</param>
        /// <param name="collectionInitializers">Collections to initialize</param>
        /// <returns>A task</returns>
        public async Task InitializeDataStore(IDocumentClient documentClient, CosmosDataStoreConfiguration cosmosDataStoreConfiguration, IEnumerable<ICollectionInitializer> collectionInitializers)
        {
            EnsureArg.IsNotNull(documentClient, nameof(documentClient));
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(collectionInitializers, nameof(collectionInitializers));

            try
            {
                _logger.LogInformation("Initializing Cosmos DB Database {DatabaseId} and collections", cosmosDataStoreConfiguration.DatabaseId);

                if (cosmosDataStoreConfiguration.AllowDatabaseCreation)
                {
                    _logger.LogDebug("CreateDatabaseIfNotExists {DatabaseId})", cosmosDataStoreConfiguration.DatabaseId);

                    await documentClient.CreateDatabaseIfNotExistsAsync(new Database
                    {
                        Id = cosmosDataStoreConfiguration.DatabaseId,
                    });
                }

                foreach (var collectionInitializer in collectionInitializers)
                {
                    // Create the Fhir collection
                    _logger.LogDebug("CreateDocumentCollectionIfNotExists {HostDescription}", collectionInitializer.RelativeCollectionUri);
                    await collectionInitializer.InitializeCollection(documentClient);
                }

                _logger.LogInformation("Cosmos DB Database {DatabaseId} and collections successfully initialized", cosmosDataStoreConfiguration.DatabaseId);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Cosmos DB Database {DatabaseId} and collections initialization failed", cosmosDataStoreConfiguration.DatabaseId);
                throw;
            }
        }
    }
}
