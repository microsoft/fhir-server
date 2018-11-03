// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Consistency;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class DocumentClientInitializer : IDocumentClientInitializer
    {
        private readonly IDocumentClientTestProvider _testProvider;
        private readonly ILogger<DocumentClientInitializer> _logger;
        private readonly IUpgradeManager _upgradeManager;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;

        public DocumentClientInitializer(IDocumentClientTestProvider testProvider, ILogger<DocumentClientInitializer> logger, IUpgradeManager upgradeManager, IFhirRequestContextAccessor fhirRequestContextAccessor)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(upgradeManager, nameof(upgradeManager));
            EnsureArg.IsNotNull(testProvider, nameof(testProvider));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));

            _testProvider = testProvider;
            _logger = logger;
            _upgradeManager = upgradeManager;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
        }

        /// <summary>
        /// Creates am unopened <see cref="DocumentClient"/> based on the given <see cref="CosmosDataStoreConfiguration"/>.
        /// </summary>
        /// <param name="configuration">The endpoint and collection settings</param>
        /// <returns>A <see cref="DocumentClient"/> instance</returns>
        public IDocumentClient CreateDocumentClient(CosmosDataStoreConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            _logger.LogInformation("Creating DocumentClient instance for {CollectionUri}", configuration.AbsoluteCollectionUri);

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

            return new DocumentClientWithExceptionHandler(
                new DocumentClientWithConsistencyLevelFromContext(
                    new DocumentClient(new Uri(configuration.Host), configuration.Key, serializerSettings, connectionPolicy, configuration.DefaultConsistencyLevel),
                    _fhirRequestContextAccessor));
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

            _logger.LogInformation("Opening DocumentClient connection to {CollectionUri}", configuration.AbsoluteCollectionUri);
            try
            {
                await _testProvider.PerformTest(client, configuration);

                _logger.LogInformation("Established DocumentClient connection to {CollectionUri}", configuration.AbsoluteCollectionUri);
            }
            catch (Exception e)
            {
                _logger.LogCritical(e, "Failed to connect to DocumentClient collection {CollectionUri}", configuration.AbsoluteCollectionUri);
                throw;
            }
        }

        /// <summary>
        /// Ensures that the necessary database and collection exist with the proper indexing policy and stored procedures
        /// </summary>
        /// <param name="documentClient">The <see cref="DocumentClient"/> instance to use for initialization.</param>
        /// <param name="cosmosDataStoreConfiguration">The data store configuration.</param>
        /// <returns>A task</returns>
        public async Task InitializeDataStore(IDocumentClient documentClient, CosmosDataStoreConfiguration cosmosDataStoreConfiguration)
        {
            EnsureArg.IsNotNull(documentClient, nameof(documentClient));
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));

            try
            {
                _logger.LogInformation("Initializing Cosmos DB collection {CollectionUri}", cosmosDataStoreConfiguration.AbsoluteCollectionUri);

                if (cosmosDataStoreConfiguration.AllowDatabaseCreation)
                {
                    _logger.LogDebug("CreateDatabaseIfNotExists {DatabaseId})", cosmosDataStoreConfiguration.DatabaseId);

                    await documentClient.CreateDatabaseIfNotExistsAsync(new Database
                    {
                        Id = cosmosDataStoreConfiguration.DatabaseId,
                    });
                }

                _logger.LogDebug("CreateDocumentCollectionIfNotExists {HostDescription}", cosmosDataStoreConfiguration.AbsoluteCollectionUri);

                DocumentCollection existingDocumentCollection = await documentClient.TryGetDocumentCollectionAsync(cosmosDataStoreConfiguration.RelativeCollectionUri);

                if (existingDocumentCollection == null)
                {
                    var documentCollection = new DocumentCollection
                    {
                        Id = cosmosDataStoreConfiguration.CollectionId,
                        PartitionKey = new PartitionKeyDefinition
                        {
                            Paths =
                            {
                                $"/{KnownResourceWrapperProperties.PartitionKey}",
                            },
                        },
                    };

                    existingDocumentCollection = await documentClient.CreateDocumentCollectionIfNotExistsAsync(
                        cosmosDataStoreConfiguration.RelativeDatabaseUri, cosmosDataStoreConfiguration.RelativeCollectionUri, documentCollection);
                }

                await _upgradeManager.SetupCollectionAsync(documentClient, existingDocumentCollection);

                _logger.LogInformation("Cosmos DB collection {CollectionUri} successfully initialized", cosmosDataStoreConfiguration.AbsoluteCollectionUri);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Cosmos DB collection {CollectionUri} initialization failed", cosmosDataStoreConfiguration.AbsoluteCollectionUri);
                throw;
            }
        }
    }
}
