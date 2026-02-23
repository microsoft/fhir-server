// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    /// <summary>
    /// Discovers OAuth2 authorization and token endpoints via OIDC discovery,
    /// with an Entra ID fallback when discovery is unavailable.
    /// Registered as a singleton so that cached results are shared across all consumers.
    /// </summary>
    public class OidcDiscoveryService : IOidcDiscoveryService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<OidcDiscoveryService> _logger;
        private readonly ConcurrentDictionary<string, (Uri AuthorizationEndpoint, Uri TokenEndpoint)> _cache = new(StringComparer.Ordinal);

        public OidcDiscoveryService(IHttpClientFactory httpClientFactory, ILogger<OidcDiscoveryService> logger)
        {
            EnsureArg.IsNotNull(httpClientFactory, nameof(httpClientFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<(Uri AuthorizationEndpoint, Uri TokenEndpoint)> ResolveEndpointsAsync(string authority, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNullOrWhiteSpace(authority, nameof(authority));

            string normalizedAuthority = authority.TrimEnd('/');

            if (_cache.TryGetValue(normalizedAuthority, out var cached))
            {
                return cached;
            }

            var endpoints = await DiscoverEndpointsAsync(normalizedAuthority, cancellationToken);
            _cache.TryAdd(normalizedAuthority, endpoints);
            return endpoints;
        }

        private async Task<(Uri AuthorizationEndpoint, Uri TokenEndpoint)> DiscoverEndpointsAsync(string authority, CancellationToken cancellationToken)
        {
            try
            {
                string openIdConfigUrl = authority + "/.well-known/openid-configuration";

                using HttpClient httpClient = _httpClientFactory.CreateClient();
                using HttpResponseMessage response = await httpClient.GetAsync(new Uri(openIdConfigUrl), cancellationToken);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync(cancellationToken);
                JObject openIdConfig = JObject.Parse(json);

                string authEndpoint = openIdConfig["authorization_endpoint"]?.Value<string>();
                string tokenEndpoint = openIdConfig["token_endpoint"]?.Value<string>();

                if (!string.IsNullOrEmpty(authEndpoint) && !string.IsNullOrEmpty(tokenEndpoint))
                {
                    return (new Uri(authEndpoint), new Uri(tokenEndpoint));
                }

                _logger.LogWarning("OIDC discovery document at {Url} did not contain authorization_endpoint or token_endpoint. Falling back to Entra ID URL pattern.", openIdConfigUrl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch OIDC discovery document for authority {Authority}. Falling back to Entra ID URL pattern.", authority);
            }

            // Fallback: assume Entra ID URL pattern
            return (
                new Uri(authority + "/oauth2/v2.0/authorize"),
                new Uri(authority + "/oauth2/v2.0/token"));
        }
    }
}
