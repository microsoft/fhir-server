// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Core;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;
using Microsoft.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class FhirCosmosClientInitializer : ICosmosClientInitializer
    {
        private const StringComparison _hashCodeStringComparison = StringComparison.Ordinal;

        private readonly ICosmosClientTestProvider _testProvider;
        private readonly Func<IEnumerable<RequestHandler>> _requestHandlerFactory;
        private readonly RetryExceptionPolicyFactory _retryExceptionPolicyFactory;
        private readonly CosmosAccessTokenProviderFactory _cosmosAccessTokenProviderFactory;
        private readonly object _lockObject;
        private readonly ILogger<FhirCosmosClientInitializer> _logger;

        private CosmosClient _cosmosClient;
        private int _cosmosKeyHashCode;

        public FhirCosmosClientInitializer(
            ICosmosClientTestProvider testProvider,
            Func<IEnumerable<RequestHandler>> requestHandlerFactory,
            RetryExceptionPolicyFactory retryExceptionPolicyFactory,
            CosmosAccessTokenProviderFactory cosmosAccessTokenProviderFactory,
            ILogger<FhirCosmosClientInitializer> logger)
        {
            EnsureArg.IsNotNull(testProvider, nameof(testProvider));
            EnsureArg.IsNotNull(requestHandlerFactory, nameof(requestHandlerFactory));
            EnsureArg.IsNotNull(retryExceptionPolicyFactory, nameof(retryExceptionPolicyFactory));
            EnsureArg.IsNotNull(cosmosAccessTokenProviderFactory, nameof(cosmosAccessTokenProviderFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _testProvider = testProvider;
            _requestHandlerFactory = requestHandlerFactory;
            _retryExceptionPolicyFactory = retryExceptionPolicyFactory;
            _cosmosAccessTokenProviderFactory = cosmosAccessTokenProviderFactory;
            _logger = logger;
            _lockObject = new object();

            _cosmosClient = null;
        }

        /// <inheritdoc />
        public CosmosClient CreateCosmosClient(CosmosDataStoreConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            // Thread-safe logic to ensure that a single instance of CosmosClient is created.
            if (_cosmosClient == null || IsNewConnectionKey(configuration))
            {
                lock (_lockObject)
                {
                    if (_cosmosClient == null || IsNewConnectionKey(configuration))
                    {
                        _cosmosClient = CreateCosmosClientInternal(configuration);
                        _cosmosKeyHashCode = string.IsNullOrWhiteSpace(configuration.Key) ? 0 : configuration.Key.GetHashCode(_hashCodeStringComparison);
                    }
                }
            }

            return _cosmosClient;
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
                    await _testProvider.PerformTestAsync(client.GetContainer(configuration.DatabaseId, cosmosCollectionConfiguration.CollectionId)));

                _logger.LogInformation("Established CosmosClient connection to {CollectionId}", cosmosCollectionConfiguration.CollectionId);
            }
            catch (Exception e)
            {
                LogLevel logLevel = e is RequestRateExceededException ? LogLevel.Warning : LogLevel.Critical;
                _logger.Log(logLevel, e, "Failed to connect to CosmosClient collection {CollectionId}", cosmosCollectionConfiguration.CollectionId);
                throw;
            }
        }

        private CosmosClient CreateCosmosClientInternal(CosmosDataStoreConfiguration configuration)
        {
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

            CosmosClientBuilder builder;

            builder = configuration.UseManagedIdentity ?
                new CosmosClientBuilder(host, _cosmosAccessTokenProviderFactory.Invoke().TokenCredential) :
                new CosmosClientBuilder(host, key);

            if (configuration.ConnectionMode == ConnectionMode.Gateway)
            {
                builder.WithConnectionModeGateway();
            }
            else
            {
                builder.WithConnectionModeDirect(
                    idleTcpConnectionTimeout: configuration.IdleTcpConnectionTimeout,
                    openTcpConnectionTimeout: configuration.OpenTcpConnectionTimeout,
                    maxRequestsPerTcpConnection: configuration.MaxRequestsPerTcpConnection,
                    maxTcpConnectionsPerEndpoint: configuration.MaxTcpConnectionsPerEndpoint,
                    portReuseMode: configuration.PortReuseMode,
                    enableTcpConnectionEndpointRediscovery: configuration.EnableTcpConnectionEndpointRediscovery);
            }

            builder
                .WithCustomSerializer(new FhirCosmosSerializer(_logger))
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

        private bool IsNewConnectionKey(CosmosDataStoreConfiguration configuration)
        {
            // Configuration key is not empty and hashcode is empty - first process access.
            if (configuration.UseManagedIdentity)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(configuration.Key) && _cosmosKeyHashCode == 0)
            {
                return true;
            }
            else if (string.IsNullOrWhiteSpace(configuration.Key)) // Configuration key is empty  - using local emulator.
            {
                return false;
            }
            else if (configuration.Key.GetHashCode(_hashCodeStringComparison) == _cosmosKeyHashCode) // Hash code is the same, no need to recreate the client.
            {
                return false;
            }
            else
            {
                return true; // Hash code is different, need to recreate the client.
            }
        }

        private class FhirCosmosSerializer : CosmosSerializer
        {
            private const int BlobSizeThresholdWarningInBytes = 1000000; // 1MB threshold.

            private static readonly RecyclableMemoryStreamManager _manager = new RecyclableMemoryStreamManager();

            private readonly ILogger<FhirCosmosClientInitializer> _logger;
            private readonly JsonSerializer _serializer;

            public FhirCosmosSerializer(ILogger<FhirCosmosClientInitializer> logger)
            {
                _logger = logger;
                _serializer = CreateSerializer();
            }

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
                try
                {
                    using var textReader = new StreamReader(stream);
                    using var reader = new JsonTextReader(textReader);

                    if (stream.Length >= BlobSizeThresholdWarningInBytes)
                    {
                        _logger.LogInformation(
                            "{Origin} - MemoryWatch - Heavy deserialization in memory. Stream size: {StreamSize}. Current memory in use: {MemoryInUse}.",
                            nameof(FhirCosmosSerializer),
                            stream.Length,
                            GC.GetTotalMemory(forceFullCollection: false));
                    }

                    return _serializer.Deserialize<T>(reader);
                }
                finally
                {
                    // As the documentation suggests, the implementation is responsible for Disposing of the stream, including when an exception is thrown, to avoid memory leaks.
                    // Reference: https://learn.microsoft.com/en-us/dotnet/api/microsoft.azure.cosmos.cosmosserializer.fromstream
                    if (stream != null)
                    {
                        stream.Dispose();
                    }
                }
            }

            public override Stream ToStream<T>(T input)
            {
                // Stream is returned to the method caller, unable to dispose it under the current scope.
                MemoryStream stream = _manager.GetStream(tag: nameof(FhirCosmosSerializer));
                using var writer = new StreamWriter(stream, leaveOpen: true);
                using var jsonWriter = new JsonTextWriter(writer);
                _serializer.Serialize(jsonWriter, input);
                jsonWriter.Flush();
                stream.Position = 0;

                if (stream.Length >= BlobSizeThresholdWarningInBytes)
                {
                    _logger.LogInformation(
                        "{Origin} - MemoryWatch - Heavy serialization in memory. Stream size: {StreamSize}. Current memory in use: {MemoryInUse}.",
                        nameof(FhirCosmosSerializer),
                        stream.Length,
                        GC.GetTotalMemory(forceFullCollection: false));
                }

                return stream;
            }
        }
    }
}
