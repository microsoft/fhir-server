﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// Maintains the count of physical partitions in a Cosmos DB collection. We have to resort to the REST API to get this information.
    /// The count can evolve over time, so we periodically query the collection to update the value.
    /// </summary>
    internal class CosmosDbCollectionPhysicalPartitionInfo : IRequireInitializationOnFirstRequest, IAsyncDisposable, ICosmosDbCollectionPhysicalPartitionInfo
    {
        private readonly CosmosDataStoreConfiguration _dataStoreConfiguration;
        private readonly CosmosCollectionConfiguration _collectionConfiguration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IAccessTokenProvider _accessTokenProvider;
        private readonly ILogger<CosmosDbCollectionPhysicalPartitionInfo> _logger;
        private readonly CancellationTokenSource _backgroundLoopCancellationTokenSource = new();
        private Task _backgroundLoopTask;

        public CosmosDbCollectionPhysicalPartitionInfo(
            CosmosDataStoreConfiguration dataStoreConfiguration,
            IOptionsMonitor<CosmosCollectionConfiguration> collectionConfiguration,
            IHttpClientFactory httpClientFactory,
            CosmosAccessTokenProviderFactory accessTokenProviderFactory,
            ILogger<CosmosDbCollectionPhysicalPartitionInfo> logger)
        {
            EnsureArg.IsNotNull(dataStoreConfiguration, nameof(dataStoreConfiguration));
            EnsureArg.IsNotNull(collectionConfiguration, nameof(collectionConfiguration));
            EnsureArg.IsNotNull(httpClientFactory, nameof(httpClientFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _dataStoreConfiguration = dataStoreConfiguration;
            _collectionConfiguration = collectionConfiguration.Get(Constants.CollectionConfigurationName);
            _httpClientFactory = httpClientFactory;
            _accessTokenProvider = accessTokenProviderFactory.Invoke();
            _logger = logger;
        }

        public int PhysicalPartitionCount { get; private set; }

        public async Task EnsureInitialized()
        {
            PhysicalPartitionCount = await GetPhysicalPartitionCount(_backgroundLoopCancellationTokenSource.Token);
            _backgroundLoopTask = BackgroundLoop(_backgroundLoopCancellationTokenSource.Token);
        }

        private async Task BackgroundLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);
                    int newPartitionCount = await GetPhysicalPartitionCount(cancellationToken);

                    if (newPartitionCount != PhysicalPartitionCount)
                    {
                        _logger.LogInformation("Physical partition count changed from {OldPhysicalPartitionCount} to {NewPhysicalPartitionCount}", PhysicalPartitionCount, newPartitionCount);
                    }

                    PhysicalPartitionCount = newPartitionCount;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Unable to get physical partition count");
                }
            }
        }

        private async Task<int> GetPhysicalPartitionCount(CancellationToken cancellationToken)
        {
            using HttpClient client = _httpClientFactory.CreateClient();

            string host = _dataStoreConfiguration.Host;
            string key = _dataStoreConfiguration.Key;

            if (_dataStoreConfiguration.UseManagedIdentity && string.IsNullOrWhiteSpace(host))
            {
                Ensure.That(host, $"{nameof(CosmosDataStoreConfiguration)}.{nameof(CosmosDataStoreConfiguration.Host)}").IsNotNullOrEmpty();
            }
            else if (string.IsNullOrWhiteSpace(host))
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    host = CosmosDbLocalEmulator.Host;
                    key = CosmosDbLocalEmulator.Key;
                }
                else
                {
                    Ensure.That(host, $"{nameof(CosmosDataStoreConfiguration)}.{nameof(CosmosDataStoreConfiguration.Host)}").IsNotNullOrEmpty();
                }
            }
            else if (!_dataStoreConfiguration.UseManagedIdentity && string.IsNullOrWhiteSpace(key))
            {
                Ensure.That(key, $"{nameof(CosmosDataStoreConfiguration)}.{nameof(CosmosDataStoreConfiguration.Key)}").IsNotNullOrEmpty();
            }

            string date = DateTime.UtcNow.ToString("R");

            bool isResourceToken = IsResourceToken(key);
            var resourceKey = key;

            if (!isResourceToken)
            {
                if (_dataStoreConfiguration.UseManagedIdentity)
                {
                    resourceKey = await GenerateAuthTokenAad(
                        host,
                        cancellationToken);
                }
                else
                {
                    resourceKey = await GenerateAuthToken(
                        "get",
                        "pkranges",
                        $"dbs/{_dataStoreConfiguration.DatabaseId}/colls/{_collectionConfiguration.CollectionId}",
                        date,
                        key);
                }
            }

            using var httpRequestMessage = new HttpRequestMessage(
                HttpMethod.Get,
                $"{host}/dbs/{_dataStoreConfiguration.DatabaseId}/colls/{_collectionConfiguration.CollectionId}/pkranges")
            {
                Headers =
                {
                    { "authorization", HttpUtility.UrlEncode(resourceKey) },
                    { "x-ms-version", "2018-12-31" },
                    { "x-ms-date", date },
                },
            };

            using HttpResponseMessage httpResponseMessage = await client.SendAsync(httpRequestMessage, cancellationToken);
            if (httpResponseMessage.IsSuccessStatusCode)
            {
                var partitionKeyRangesResponse = await httpResponseMessage.Content.ReadFromJsonAsync<PartitionKeyRangesResponse>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
                return partitionKeyRangesResponse.PartitionKeyRanges.Length;
            }

            if (httpResponseMessage.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new RequestRateExceededException(null);
            }

            _logger.LogError("Unable to get physical partition count. {ResponseBody}", await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken));

            httpResponseMessage.EnsureSuccessStatusCode();
            return 0; // will not reach this
        }

        private static bool IsResourceToken(string key) => key.StartsWith("type=resource&", StringComparison.InvariantCulture);

        public async ValueTask DisposeAsync()
        {
            if (_backgroundLoopTask != null)
            {
                try
                {
                    await _backgroundLoopCancellationTokenSource.CancelAsync();
                    await _backgroundLoopTask;
                    _backgroundLoopCancellationTokenSource.Dispose();
                    _backgroundLoopTask.Dispose();
                }
                finally
                {
                    _backgroundLoopTask = null;
                }
            }
        }

        private static Task<string> GenerateAuthToken(string verb, string resourceType, string resourceId, string date, string key)
        {
            string payLoad = $"{verb.ToLowerInvariant()}\n{resourceType.ToLowerInvariant()}\n{resourceId}\n{date.ToLowerInvariant()}\n\n";

            using var hmacSha256 = new HMACSHA256 { Key = Convert.FromBase64String(key) };
            byte[] hashPayLoad = hmacSha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payLoad));
            string signature = Convert.ToBase64String(hashPayLoad);

            return Task.FromResult($"type=master&ver=1.0&sig={signature}");
        }

        private async Task<string> GenerateAuthTokenAad(string host, CancellationToken cancellationToken)
        {
            var resourceUri = new Uri(host);
            var accessToken = await _accessTokenProvider.GetAccessTokenForResourceAsync(resourceUri, cancellationToken);

            return $"type=aad&ver=1.0&sig={accessToken}";
        }

        private record PartitionKeyRange;

        private record PartitionKeyRangesResponse(PartitionKeyRange[] PartitionKeyRanges);
    }
}
