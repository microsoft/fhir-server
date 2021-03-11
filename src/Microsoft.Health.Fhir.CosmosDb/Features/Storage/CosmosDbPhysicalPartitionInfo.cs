// -------------------------------------------------------------------------------------------------
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
using Microsoft.Health.Fhir.CosmosDb.Configs;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    internal class CosmosDbPhysicalPartitionInfo : IRequireInitializationOnFirstRequest, IAsyncDisposable, ICosmosDbPhysicalPartitionInfo
    {
        private readonly CosmosDataStoreConfiguration _dataStoreConfiguration;
        private readonly CosmosCollectionConfiguration _collectionConfiguration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<CosmosDbPhysicalPartitionInfo> _logger;
        private readonly CancellationTokenSource _backgroundLoopCancellationTokenSource = new();
        private Task _backgroundLoopTask;

        public CosmosDbPhysicalPartitionInfo(
            CosmosDataStoreConfiguration dataStoreConfiguration,
            IOptionsMonitor<CosmosCollectionConfiguration> collectionConfiguration,
            IHttpClientFactory httpClientFactory,
            ILogger<CosmosDbPhysicalPartitionInfo> logger)
        {
            EnsureArg.IsNotNull(dataStoreConfiguration, nameof(dataStoreConfiguration));
            EnsureArg.IsNotNull(collectionConfiguration, nameof(collectionConfiguration));
            EnsureArg.IsNotNull(httpClientFactory, nameof(httpClientFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _dataStoreConfiguration = dataStoreConfiguration;
            _collectionConfiguration = collectionConfiguration.Get(Constants.CollectionConfigurationName);
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public int PhysicalPartitionCount { get; private set; }

        private static string GenerateAuthToken(string verb, string resourceType, string resourceId, string date, string key)
        {
#pragma warning disable CA1308 // Normalize strings to uppercase
            string payLoad = $"{verb.ToLowerInvariant()}\n{resourceType.ToLowerInvariant()}\n{resourceId}\n{date.ToLowerInvariant()}\n\n";
#pragma warning restore CA1308 // Normalize strings to uppercase

            using var hmacSha256 = new HMACSHA256 { Key = Convert.FromBase64String(key) };
            byte[] hashPayLoad = hmacSha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payLoad));
            string signature = Convert.ToBase64String(hashPayLoad);

            return $"type=master&ver=1.0&sig={signature}";
        }

        public async Task EnsureInitialized()
        {
            PhysicalPartitionCount = await GetPhysicalPartitionCount(_backgroundLoopCancellationTokenSource.Token);
            _backgroundLoopTask = BackgroundLoop(_backgroundLoopCancellationTokenSource.Token);
        }

        public async Task BackgroundLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);
                    PhysicalPartitionCount = await GetPhysicalPartitionCount(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    _logger.LogError("Unable to get physical partition count.", e);
                }
            }
        }

        private async Task<int> GetPhysicalPartitionCount(CancellationToken cancellationToken)
        {
            using HttpClient client = _httpClientFactory.CreateClient();

            string host = _dataStoreConfiguration.Host;
            string key = _dataStoreConfiguration.Key;

            if (string.IsNullOrWhiteSpace(host) && string.IsNullOrWhiteSpace(key))
            {
                host = CosmosDbLocalEmulator.Host;
                key = CosmosDbLocalEmulator.Key;
            }

            string date = DateTime.UtcNow.ToString("R");

            bool isResourceToken = IsResourceToken(key);

            string authToken = HttpUtility.UrlEncode(
                isResourceToken
                    ? key
                    : GenerateAuthToken(
                        "get",
                        "pkranges",
                        $"dbs/{_dataStoreConfiguration.DatabaseId}/colls/{_collectionConfiguration.CollectionId}",
                        date,
                        key));

            var httpRequestMessage = new HttpRequestMessage(
                HttpMethod.Get,
                $"{host}/dbs/{_dataStoreConfiguration.DatabaseId}/colls/{_collectionConfiguration.CollectionId}/pkranges")
            {
                Headers =
                {
                    { "authorization", authToken },
                    { "x-ms-version", "2018-12-31" },
                    { "x-ms-date", date },
                },
            };

            HttpResponseMessage httpResponseMessage = await client.SendAsync(httpRequestMessage, cancellationToken);
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

        public static bool IsResourceToken(string key) => key.StartsWith("type=resource&", StringComparison.InvariantCulture);

        public async ValueTask DisposeAsync()
        {
            _backgroundLoopCancellationTokenSource.Cancel();
            await _backgroundLoopTask;
            _backgroundLoopCancellationTokenSource.Dispose();
            _backgroundLoopTask.Dispose();
        }

        private record PartitionKeyRange;

        private record PartitionKeyRangesResponse(PartitionKeyRange[] PartitionKeyRanges);
    }
}
