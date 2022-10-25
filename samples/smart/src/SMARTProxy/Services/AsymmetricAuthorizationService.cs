// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SMARTProxy.Configuration;
using SMARTProxy.Models;

namespace SMARTProxy.Services
{
    public class AsymmetricAuthorizationService
    {
        private SMARTProxyConfig _functionConfig;
        private IHttpClientFactory _httpClientFactory;
        private ILogger _logger;
        private IClientConfigService _clientConfigService;

        public AsymmetricAuthorizationService(SMARTProxyConfig functionConfig, IHttpClientFactory httpClientFactory, ILogger<AsymmetricAuthorizationService> logger, IClientConfigService clientConfigService)
        {
            _httpClientFactory = httpClientFactory;
            _functionConfig = functionConfig;
            _logger = logger;
            _clientConfigService = clientConfigService;
        }

        public async Task<BackendClientConfiguration> AuthenticateBackendAsyncClient(ClientConfidentialAsyncTokenContext castTokenContext)
        {
            var clientConfig = await _clientConfigService.FetchBackendClientConfiguration(_functionConfig, castTokenContext.ClientId);

            var jwks = await FetchJwks(clientConfig);
            ValidateToken(clientConfig, jwks, castTokenContext.ClientAssertion);

            return clientConfig;
        }

        public async Task<JsonWebKeySet> FetchJwks(BackendClientConfiguration clientConfig)
        {
#pragma warning disable CA2000 //https://docs.microsoft.com/en-us/aspnet/core/fundamentals/http-requests?view=aspnetcore-5.0#httpclient-and-lifetime-management
            var client = _httpClientFactory.CreateClient();
#pragma warning restore CA2000

            HttpResponseMessage jwksResponse;
            try
            {
                jwksResponse = await client.GetAsync(clientConfig.JwksUri);
            }
            catch (HttpRequestException)
            {
                _logger.LogWarning("HTTP exception occurred while fetching {JwksUrl} for client {ClientId}.", clientConfig.JwksUri, clientConfig.ClientId);
                throw;
            }
            catch (Exception)
            {
                _logger.LogError("Unhandled exception occurred while fetching {JwksUrl} for client {ClientId}.", clientConfig.JwksUri, clientConfig.ClientId);
                throw;
            }

            if (!jwksResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Unsuccessful response while fetching {JwksUrl} for client {ClientId}. Is the client configured correctly?", clientConfig.JwksUri, clientConfig.ClientId);
                throw new InvalidDataException($"Unsuccessful response while fetching {clientConfig.JwksUri} for client {clientConfig.ClientId}.");
            }

            try
            {
                var responseString = await jwksResponse.Content.ReadAsStringAsync();
                return new JsonWebKeySet(responseString);
            }
            catch (ArgumentException)
            {
                _logger.LogError("Invalid JWKS document found at {JwksUrl} for client {ClientId}. Is the client configured correctly?", clientConfig.JwksUri, clientConfig.ClientId);
                throw new InvalidDataException($"Invalid JWKS document found atg {clientConfig.JwksUri} for client {clientConfig.ClientId}.");
            }
        }

        public JwtSecurityToken ValidateToken(BackendClientConfiguration clientConfig, JsonWebKeySet jwks, string token)
        {
            var signingKeys = jwks.GetSigningKeys();

            var validationParameters = new TokenValidationParameters()
            {
                ValidateAudience = true,

                // This MUST be set to the token endpoint per the SMART IG
                ValidAudience = $"{_functionConfig.SmartFhirEndpoint}/token",
                ValidateLifetime = true,

                // This MUST be the to the client id
                ValidIssuers = new List<string> { clientConfig.ClientId },
                IssuerSigningKeys = signingKeys,
                RequireSignedTokens = true,
            };

            var tokenHandler = new JwtSecurityTokenHandler();

            tokenHandler.ValidateToken(token, validationParameters, out SecurityToken validatedToken);

            var castToken = (JwtSecurityToken)validatedToken;

            return castToken;
        }
    }
}
