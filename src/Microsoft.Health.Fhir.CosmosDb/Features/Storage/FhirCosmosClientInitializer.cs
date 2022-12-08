// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class FhirCosmosClientInitializer : ICosmosClientInitializer
    {
        private readonly ICosmosClientTestProvider _testProvider;
        private readonly ILogger<FhirCosmosClientInitializer> _logger;
        private readonly Func<IEnumerable<RequestHandler>> _requestHandlerFactory;
        private readonly RetryExceptionPolicyFactory _retryExceptionPolicyFactory;

        public FhirCosmosClientInitializer(
            ICosmosClientTestProvider testProvider,
            Func<IEnumerable<RequestHandler>> requestHandlerFactory,
            RetryExceptionPolicyFactory retryExceptionPolicyFactory,
            ILogger<FhirCosmosClientInitializer> logger)
        {
            EnsureArg.IsNotNull(testProvider, nameof(testProvider));
            EnsureArg.IsNotNull(requestHandlerFactory, nameof(requestHandlerFactory));
            EnsureArg.IsNotNull(retryExceptionPolicyFactory, nameof(retryExceptionPolicyFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _testProvider = testProvider;
            _requestHandlerFactory = requestHandlerFactory;
            _retryExceptionPolicyFactory = retryExceptionPolicyFactory;
            _logger = logger;
        }

        /// <inheritdoc />
        public CosmosClient CreateCosmosClient(CosmosDataStoreConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            var host = configuration.Host;
            var key = configuration.Key;

            if (string.IsNullOrWhiteSpace(host) && string.IsNullOrWhiteSpace(key))
            {
                _logger.LogWarning("No connection string provided, attempting to connect to local emulator.");

                host = CosmosDbLocalEmulator.Host;
                key = CosmosDbLocalEmulator.Key;
            }

            _logger.LogInformation("Creating CosmosClient instance for {DatabaseId}, Host: {Host}", configuration.DatabaseId, host);

            IEnumerable<RequestHandler> requestHandlers = _requestHandlerFactory.Invoke();

            var builder = new CosmosClientBuilder(host, key)
                .WithConnectionModeDirect(enableTcpConnectionEndpointRediscovery: true)
                .WithCustomSerializer(new FhirCosmosSerializer())
                .WithThrottlingRetryOptions(TimeSpan.FromSeconds(configuration.RetryOptions.MaxWaitTimeInSeconds), configuration.RetryOptions.MaxNumberOfRetries)
                .AddCustomHandlers(requestHandlers.ToArray());

            if (configuration.PreferredLocations?.Any() == true)
            {
                builder.WithApplicationPreferredRegions(configuration.PreferredLocations?.ToArray());
            }

            if (configuration.DefaultConsistencyLevel != null)
            {
                builder.WithConsistencyLevel(configuration.DefaultConsistencyLevel.Value);
            }

            return builder.Build();
        }

        public Container CreateFhirContainer(CosmosClient client, string databaseId, string collectionId)
        {
            return client.GetContainer(databaseId, collectionId);
        }

        /// <inheritdoc />
        public async Task OpenCosmosClient(CosmosClient client, CosmosDataStoreConfiguration configuration, CosmosCollectionConfiguration cosmosCollectionConfiguration)
        {
            EnsureArg.IsNotNull(client, nameof(client));
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            _logger.LogInformation("Opening CosmosClient connection to {CollectionId}", cosmosCollectionConfiguration.CollectionId);
            try
            {
                await _retryExceptionPolicyFactory.RetryPolicy.ExecuteAsync(async () =>
                    await _testProvider.PerformTestAsync(client.GetContainer(configuration.DatabaseId, cosmosCollectionConfiguration.CollectionId), configuration, cosmosCollectionConfiguration));

                _logger.LogInformation("Established CosmosClient connection to {CollectionId}", cosmosCollectionConfiguration.CollectionId);
            }
            catch (Exception e)
            {
                LogLevel logLevel = e is RequestRateExceededException ? LogLevel.Warning : LogLevel.Critical;
                _logger.Log(logLevel, e, "Failed to connect to CosmosClient collection {CollectionId}", cosmosCollectionConfiguration.CollectionId);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task InitializeDataStoreAsync(CosmosClient client, CosmosDataStoreConfiguration cosmosDataStoreConfiguration, IEnumerable<ICollectionInitializer> collectionInitializers, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNull(client, nameof(client));
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(collectionInitializers, nameof(collectionInitializers));

            try
            {
                _logger.LogInformation("Initializing Cosmos DB Database {DatabaseId} and collections", cosmosDataStoreConfiguration.DatabaseId);

                if (cosmosDataStoreConfiguration.AllowDatabaseCreation)
                {
                    _logger.LogInformation("CreateDatabaseIfNotExists {DatabaseId}", cosmosDataStoreConfiguration.DatabaseId);

                    await _retryExceptionPolicyFactory.RetryPolicy.ExecuteAsync(
                        async () =>
                            await client.CreateDatabaseIfNotExistsAsync(
                                cosmosDataStoreConfiguration.DatabaseId,
                                cosmosDataStoreConfiguration.InitialDatabaseThroughput.HasValue ? ThroughputProperties.CreateManualThroughput(cosmosDataStoreConfiguration.InitialDatabaseThroughput.Value) : null,
                                cancellationToken: cancellationToken));
                }

                foreach (var collectionInitializer in collectionInitializers)
                {
                    await collectionInitializer.InitializeCollectionAsync(client, cancellationToken);
                }

                _logger.LogInformation("Cosmos DB Database {DatabaseId} and collections successfully initialized", cosmosDataStoreConfiguration.DatabaseId);
            }
            catch (Exception ex)
            {
                LogLevel logLevel = ex is RequestRateExceededException ? LogLevel.Warning : LogLevel.Critical;
                _logger.Log(logLevel, ex, "Cosmos DB Database {DatabaseId} and collections initialization failed", cosmosDataStoreConfiguration.DatabaseId);
                throw;
            }
        }

        private class FhirCosmosSerializer : CosmosSerializer
        {
            private readonly JsonSerializer _serializer = CreateSerializer();
            private static readonly RecyclableMemoryStreamManager _manager = new RecyclableMemoryStreamManager();

            private static JsonSerializer CreateSerializer()
            {
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

                return JsonSerializer.Create(serializerSettings);
            }

            public override T FromStream<T>(Stream stream)
            {
                using var textReader = new StreamReader(stream);
                using var reader = new JsonTextReader(textReader);
                return _serializer.Deserialize<T>(reader);
            }

            public override Stream ToStream<T>(T input)
            {
                MemoryStream stream = _manager.GetStream();
                using var writer = new StreamWriter(stream, leaveOpen: true);
                using var jsonWriter = new JsonTextWriter(writer);
                _serializer.Serialize(jsonWriter, input);
                jsonWriter.Flush();
                stream.Position = 0;
                return stream;
            }
        }
    }
}
