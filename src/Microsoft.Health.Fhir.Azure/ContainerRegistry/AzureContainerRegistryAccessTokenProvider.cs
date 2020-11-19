// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Newtonsoft.Json;
using Polly;

namespace Microsoft.Health.Fhir.Azure.ContainerRegistry
{
    /// <summary>
    /// Retrieve ACR accessTOkens with AAD token provider.
    /// We need to exchange ACR refresh token with AAD token, and get ACR access token from refresh token.
    /// References:
    /// https://github.com/Azure/acr/blob/main/docs/AAD-OAuth.md#calling-post-oauth2exchange-to-get-an-acr-refresh-token
    /// https://github.com/Azure/acr/blob/main/docs/AAD-OAuth.md#calling-post-oauth2token-to-get-an-acr-access-token
    /// </summary>
    public class AzureContainerRegistryAccessTokenProvider : IContainerRegistryTokenProvider
    {
        private const string ARMResourceUrl = "https://management.azure.com/";
        private const string ClassicalARMResourceUrl = "https://management.core.windows.net/";
        private const string ExchangeAcrRefreshTokenUrl = "oauth2/exchange";
        private const string GetAcrAccessTokenUrl = "oauth2/token";

        private readonly IAccessTokenProvider _aadTokenProvider;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly DataConvertConfiguration _dataConvertConfiguration;
        private readonly ILogger<AzureContainerRegistryAccessTokenProvider> _logger;

        public AzureContainerRegistryAccessTokenProvider(
            IAccessTokenProvider aadTokenProvider,
            IHttpClientFactory httpClientFactory,
            IOptions<DataConvertConfiguration> dataConvertConfiguration,
            ILogger<AzureContainerRegistryAccessTokenProvider> logger)
        {
            EnsureArg.IsNotNull(aadTokenProvider, nameof(aadTokenProvider));
            EnsureArg.IsNotNull(httpClientFactory, nameof(httpClientFactory));
            EnsureArg.IsNotNull(dataConvertConfiguration, nameof(dataConvertConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _aadTokenProvider = aadTokenProvider;
            _httpClientFactory = httpClientFactory;
            _dataConvertConfiguration = dataConvertConfiguration.Value;
            _logger = logger;
        }

        public async Task<string> GetTokenAsync(string registryServer, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrEmpty(registryServer, nameof(registryServer));

            CheckIfRegistryIsConfigured(registryServer);

            var aadResourceUri = GetArmResourceUri(registryServer);

            string aadToken;
            try
            {
                aadToken = await _aadTokenProvider.GetAccessTokenForResourceAsync(aadResourceUri, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get AAD access token from managed identity.");
                throw new AzureContainerRegistryTokenException(Resources.CannotGetAcrAccessToken, ex);
            }

            var registryUri = new Uri($"https://{registryServer}");
            var exchangeRefereshTokenUrl = new Uri(registryUri, ExchangeAcrRefreshTokenUrl);
            string acrRefreshToken = await ExchangeAcrRefreshToken(exchangeRefereshTokenUrl, aadToken, cancellationToken);

            var acrAccessTokenUri = new Uri(registryUri, GetAcrAccessTokenUrl);
            return await GetAcrAccessToken(acrAccessTokenUri, acrRefreshToken, cancellationToken);
        }

        private async Task<string> ExchangeAcrRefreshToken(Uri exchangeUri, string aadToken, CancellationToken cancellationToken)
        {
            var parameters = new List<KeyValuePair<string, string>>();
            parameters.Add(new KeyValuePair<string, string>("grant_type", "access_token"));
            parameters.Add(new KeyValuePair<string, string>("service", exchangeUri.Host));
            parameters.Add(new KeyValuePair<string, string>("access_token", aadToken));
            var request = new HttpRequestMessage(HttpMethod.Post, exchangeUri)
            {
                Content = new FormUrlEncodedContent(parameters),
            };

            string refreshToken;
            try
            {
                var refreshTokenResponse = await SendRequestAsync(request, cancellationToken);
                refreshTokenResponse.EnsureSuccessStatusCode();
                var refreshTokenText = await refreshTokenResponse.Content.ReadAsStringAsync();
                dynamic refreshTokenJson = JsonConvert.DeserializeObject(refreshTokenText);
                refreshToken = (string)refreshTokenJson.refresh_token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to exchange ACR refresh token with aad access token.");
                throw new AzureContainerRegistryTokenException(Resources.CannotGetAcrAccessToken, ex);
            }

            if (string.IsNullOrEmpty(refreshToken))
            {
                _logger.LogError("ACR refresh token is empty.");
                throw new AzureContainerRegistryTokenException(Resources.CannotGetAcrAccessToken);
            }

            _logger.LogInformation("Successfully exchanged ACR refresh token.");
            return refreshToken;
        }

        private async Task<string> GetAcrAccessToken(Uri accessTokenUri, string refreshToken, CancellationToken cancellationToken)
        {
            var parameters = new List<KeyValuePair<string, string>>();
            parameters.Add(new KeyValuePair<string, string>("grant_type", "refresh_token"));
            parameters.Add(new KeyValuePair<string, string>("service", accessTokenUri.Host));
            parameters.Add(new KeyValuePair<string, string>("refresh_token", refreshToken));

            // Add scope for AcrPull role (granted at registry level).
            parameters.Add(new KeyValuePair<string, string>("scope", "repository:*:pull"));
            var request = new HttpRequestMessage(HttpMethod.Post, accessTokenUri)
            {
                Content = new FormUrlEncodedContent(parameters),
            };

            string accessToken;
            try
            {
                var accessTokenResponse = await SendRequestAsync(request, cancellationToken);
                accessTokenResponse.EnsureSuccessStatusCode();
                var accessTokenText = await accessTokenResponse.Content.ReadAsStringAsync();
                dynamic accessTokenJson = JsonConvert.DeserializeObject(accessTokenText);
                accessToken = accessTokenJson.access_token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get ACR access token with ACR refresh token.");
                throw new AzureContainerRegistryTokenException(Resources.CannotGetAcrAccessToken, ex);
            }

            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogError("ACR access token is empty.");
                throw new AzureContainerRegistryTokenException(Resources.CannotGetAcrAccessToken);
            }

            _logger.LogInformation("Successfully retrieved ACR access token.");
            return string.Format("Bearer {0}", accessToken);
        }

        private async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            return await Policy
              .Handle<HttpRequestException>()
              .RetryAsync(3, onRetry: (exception, retryCount) =>
              {
                  _logger.LogWarning(exception, $"Get ACR token failed. Retry {retryCount}");
              })
              .ExecuteAsync(() => httpClient.SendAsync(request, cancellationToken));
        }

        private Uri GetArmResourceUri(string registryServer)
        {
            // Determine which ARM resource uri to use for this registry. https://docs.microsoft.com/en-us/rest/api/azure/#request-uri
            // Note ARMResourceUri will be chosen mostly except that registry is from dogfooding environment which end with 'azurecr-test'.
            if (registryServer.EndsWith(".azurecr.io", StringComparison.OrdinalIgnoreCase))
            {
                return new Uri(ARMResourceUrl);
            }

            return new Uri(ClassicalARMResourceUrl);
        }

        private void CheckIfRegistryIsConfigured(string registryServer)
        {
            if (!_dataConvertConfiguration.ContainerRegistryServers.Any(server =>
                string.Equals(server, registryServer, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogError("The requested ACR server is not configured.");
                throw new ContainerRegistryNotConfiguredException(string.Format(Resources.ContainerRegistryNotConfigured, registryServer));
            }
        }
    }
}
