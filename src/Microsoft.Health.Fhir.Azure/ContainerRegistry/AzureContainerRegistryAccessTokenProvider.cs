// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData.Models;
using Newtonsoft.Json;
using Polly;

namespace Microsoft.Health.Fhir.Azure.ContainerRegistry
{
    /// <summary>
    /// Retrieve ACR access token with AAD token provider.
    /// We need to exchange ACR refresh token with AAD token, and get ACR access token from refresh token.
    /// References:
    /// https://github.com/Azure/acr/blob/main/docs/AAD-OAuth.md#calling-post-oauth2exchange-to-get-an-acr-refresh-token
    /// https://github.com/Azure/acr/blob/main/docs/AAD-OAuth.md#calling-post-oauth2token-to-get-an-acr-access-token
    /// </summary>
    public class AzureContainerRegistryAccessTokenProvider : IContainerRegistryTokenProvider
    {
        private const string ExchangeAcrRefreshTokenUrl = "oauth2/exchange";
        private const string GetAcrAccessTokenUrl = "oauth2/token";

        private readonly IAccessTokenProvider _aadTokenProvider;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ConvertDataConfiguration _convertDataConfiguration;
        private readonly ILogger<AzureContainerRegistryAccessTokenProvider> _logger;

        public AzureContainerRegistryAccessTokenProvider(
            IAccessTokenProvider aadTokenProvider,
            IHttpClientFactory httpClientFactory,
            IOptions<ConvertDataConfiguration> convertDataConfiguration,
            ILogger<AzureContainerRegistryAccessTokenProvider> logger)
        {
            EnsureArg.IsNotNull(aadTokenProvider, nameof(aadTokenProvider));
            EnsureArg.IsNotNull(httpClientFactory, nameof(httpClientFactory));
            EnsureArg.IsNotNull(convertDataConfiguration, nameof(convertDataConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _aadTokenProvider = aadTokenProvider;
            _httpClientFactory = httpClientFactory;
            _convertDataConfiguration = convertDataConfiguration.Value;
            _logger = logger;
        }

        public async Task<string> GetTokenAsync(string registryServer, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrEmpty(registryServer, nameof(registryServer));

            var aadResourceUri = new Uri(_convertDataConfiguration.ArmResourceManagerId);
            string aadToken;
            try
            {
                aadToken = await _aadTokenProvider.GetAccessTokenForResourceAsync(aadResourceUri, cancellationToken);
            }
            catch (AccessTokenProviderException ex)
            {
                _logger.LogWarning(ex, "Failed to get AAD access token from managed identity.");
                throw new AzureContainerRegistryTokenException(Resources.CannotGetAcrAccessToken, HttpStatusCode.Unauthorized, ex);
            }

            try
            {
                return await Policy
                  .Handle<HttpRequestException>()
                  .RetryAsync(3, onRetry: (exception, retryCount) =>
                  {
                      _logger.LogWarning(exception, "Get ACR token failed. Retry {RetryCount}.", retryCount);
                  })
                  .ExecuteAsync(() => GetAcrAccessTokenWithAadToken(registryServer, aadToken, cancellationToken));
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to get ACR access token with AAD access token.");
                throw new AzureContainerRegistryTokenException(Resources.CannotGetAcrAccessToken, HttpStatusCode.BadRequest, ex);
            }
        }

        private async Task<string> GetAcrAccessTokenWithAadToken(string registryServer, string aadToken, CancellationToken cancellationToken)
        {
            string acrRefreshToken = await ExchangeAcrRefreshToken(registryServer, aadToken, cancellationToken);
            return await GetAcrAccessToken(registryServer, acrRefreshToken, cancellationToken);
        }

        private async Task<string> ExchangeAcrRefreshToken(string registryServer, string aadToken, CancellationToken cancellationToken)
        {
            var registryUri = new Uri($"https://{registryServer}");
            var exchangeUri = new Uri(registryUri, ExchangeAcrRefreshTokenUrl);

            var parameters = new List<KeyValuePair<string, string>>();
            parameters.Add(new KeyValuePair<string, string>("grant_type", "access_token"));
            parameters.Add(new KeyValuePair<string, string>("service", registryUri.Host));
            parameters.Add(new KeyValuePair<string, string>("access_token", aadToken));
            using var request = new HttpRequestMessage(HttpMethod.Post, exchangeUri)
            {
                Content = new FormUrlEncodedContent(parameters),
            };

            using HttpResponseMessage refreshTokenResponse = await SendRequestAsync(request, cancellationToken);
            if (refreshTokenResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogInformation("Failed to exchange ACR refresh token: ACR server is unauthorized.");
                throw new ContainerRegistryNotAuthorizedException(string.Format(Resources.ContainerRegistryNotAuthorized, registryServer));
            }
            else if (refreshTokenResponse.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Failed to exchange ACR refresh token: ACR server is not found.");
                throw new ContainerRegistryNotFoundException(string.Format(Resources.ContainerRegistryNotFound, registryServer));
            }
            else if (!refreshTokenResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to exchange ACR refresh token with AAD access token. Status code: {StatusCode}.", refreshTokenResponse.StatusCode);
                throw new AzureContainerRegistryTokenException(Resources.CannotGetAcrAccessToken, refreshTokenResponse.StatusCode);
            }

            var refreshTokenText = await refreshTokenResponse.Content.ReadAsStringAsync(cancellationToken);
            dynamic refreshTokenJson = JsonConvert.DeserializeObject(refreshTokenText);
            string refreshToken = (string)refreshTokenJson.refresh_token;
            if (string.IsNullOrEmpty(refreshToken))
            {
                _logger.LogWarning("ACR refresh token is empty.");
                throw new AzureContainerRegistryTokenException(Resources.CannotGetAcrAccessToken, refreshTokenResponse.StatusCode);
            }

            _logger.LogInformation("Successfully exchanged ACR refresh token.");
            return refreshToken;
        }

        private async Task<string> GetAcrAccessToken(string registryServer, string refreshToken, CancellationToken cancellationToken)
        {
            var registryUri = new Uri($"https://{registryServer}");
            var accessTokenUri = new Uri(registryUri, GetAcrAccessTokenUrl);

            var parameters = new List<KeyValuePair<string, string>>();
            parameters.Add(new KeyValuePair<string, string>("grant_type", "refresh_token"));
            parameters.Add(new KeyValuePair<string, string>("service", registryUri.Host));
            parameters.Add(new KeyValuePair<string, string>("refresh_token", refreshToken));

            // Add scope for AcrPull role (granted at registry level).
            parameters.Add(new KeyValuePair<string, string>("scope", "repository:*:pull"));
            using var request = new HttpRequestMessage(HttpMethod.Post, accessTokenUri)
            {
                Content = new FormUrlEncodedContent(parameters),
            };

            using HttpResponseMessage accessTokenResponse = await SendRequestAsync(request, cancellationToken);
            if (accessTokenResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogInformation("Failed to get ACR access token: ACR server is unauthorized.");
                throw new ContainerRegistryNotAuthorizedException(string.Format(Resources.ContainerRegistryNotAuthorized, registryServer));
            }
            else if (accessTokenResponse.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Failed to get ACR access token: ACR server is not found.");
                throw new ContainerRegistryNotFoundException(string.Format(Resources.ContainerRegistryNotFound, registryServer));
            }
            else if (!accessTokenResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get ACR access token with ACR refresh token. Status code: {StatusCode}.", accessTokenResponse.StatusCode);
                throw new AzureContainerRegistryTokenException(Resources.CannotGetAcrAccessToken, accessTokenResponse.StatusCode);
            }

            var accessTokenText = await accessTokenResponse.Content.ReadAsStringAsync(cancellationToken);
            dynamic accessTokenJson = JsonConvert.DeserializeObject(accessTokenText);
            string accessToken = accessTokenJson.access_token;
            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogWarning("ACR access token is empty.");
                throw new AzureContainerRegistryTokenException(Resources.CannotGetAcrAccessToken, accessTokenResponse.StatusCode);
            }

            _logger.LogInformation("Successfully retrieved ACR access token.");
            return $"Bearer {accessToken}";
        }

        private async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
#pragma warning disable CA2000 //https://docs.microsoft.com/en-us/aspnet/core/fundamentals/http-requests?view=aspnetcore-5.0#httpclient-and-lifetime-management
            var client = _httpClientFactory.CreateClient();
#pragma warning restore CA2000
            return await client.SendAsync(request, cancellationToken);
        }
    }
}
