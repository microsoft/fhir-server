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
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Core.Features.Context;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class FhirDocumentClientInitializer : IDocumentClientInitializer
    {
        private readonly IDocumentClientTestProvider _testProvider;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly ILogger<FhirDocumentClientInitializer> _logger;

        public FhirDocumentClientInitializer(IDocumentClientTestProvider testProvider, IFhirRequestContextAccessor fhirRequestContextAccessor, ILogger<FhirDocumentClientInitializer> logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(testProvider, nameof(testProvider));

            _testProvider = testProvider;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _logger = logger;
        }

        /// <inheritdoc />
        public IDocumentClient CreateDocumentClient(CosmosDataStoreConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            _logger.LogInformation("Creating DocumentClient instance for {DatabaseUrl}", configuration.RelativeDatabaseUri);

            var connectionPolicy = new ConnectionPolicy
            {
                ConnectionMode = configuration.ConnectionMode,
                ConnectionProtocol = configuration.ConnectionProtocol,
                RetryOptions = new RetryOptions
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

            return new FhirDocumentClient(
                new DocumentClient(new Uri(configuration.Host), configuration.Key, serializerSettings, connectionPolicy, configuration.DefaultConsistencyLevel),
                _fhirRequestContextAccessor,
                configuration.ContinuationTokenSizeLimitInKb);
        }

        /// <inheritdoc />
        public async Task OpenDocumentClient(IDocumentClient client, CosmosDataStoreConfiguration configuration, CosmosCollectionConfiguration cosmosCollectionConfiguration)
        {
            EnsureArg.IsNotNull(client, nameof(client));
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            Uri absoluteCollectionUri = configuration.GetAbsoluteCollectionUri(cosmosCollectionConfiguration.CollectionId);

            _logger.LogInformation("Opening DocumentClient connection to {CollectionUri}", absoluteCollectionUri);
            try
            {
                await _testProvider.PerformTest(client, configuration, cosmosCollectionConfiguration);

                _logger.LogInformation("Established DocumentClient connection to {CollectionUri}", absoluteCollectionUri);
            }
            catch (Exception e)
            {
                _logger.LogCritical(e, "Failed to connect to DocumentClient collection {CollectionUri}", absoluteCollectionUri);
                throw;
            }
        }

        /// <inheritdoc />
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

                    var options = new RequestOptions { OfferThroughput = cosmosDataStoreConfiguration.InitialDatabaseThroughput };
                    await documentClient.CreateDatabaseIfNotExistsAsync(
                        new Database
                        {
                            Id = cosmosDataStoreConfiguration.DatabaseId,
                        },
                        options);
                }

                foreach (var collectionInitializer in collectionInitializers)
                {
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
