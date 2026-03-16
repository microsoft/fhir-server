// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    /// <summary>
    /// Discovers OAuth2 authorization and token endpoints via OIDC discovery,
    /// with an Entra ID fallback when discovery is unavailable.
    /// Registered as a singleton so that cached results are shared across all consumers.
    /// </summary>
    // TODO: Update DefaultTokenIntrospectionService to use this shared service in a follow-up PR.
    public class OidcDiscoveryService : IOidcDiscoveryService
    {
        private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromHours(24);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<OidcDiscoveryService> _logger;
        private readonly TimeSpan _cacheDuration;
        private readonly ConcurrentDictionary<string, (Uri AuthorizationEndpoint, Uri TokenEndpoint, DateTimeOffset Timestamp)> _cache = new(StringComparer.Ordinal);

        private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;

        public OidcDiscoveryService(IHttpClientFactory httpClientFactory, ILogger<OidcDiscoveryService> logger)
            : this(httpClientFactory, logger, DefaultCacheDuration)
        {
        }

        internal OidcDiscoveryService(IHttpClientFactory httpClientFactory, ILogger<OidcDiscoveryService> logger, TimeSpan cacheDuration)
        {
            EnsureArg.IsNotNull(httpClientFactory, nameof(httpClientFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _cacheDuration = cacheDuration;

            // Retry on network failures (HttpRequestException), timeouts (TaskCanceledException),
            // and transient HTTP status codes (429 Too Many Requests, 5xx server errors).
            _retryPolicy = Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .Or<TaskCanceledException>()
                .OrResult(r => IsTransientStatusCode(r.StatusCode))
                .WaitAndRetryAsync(
                    3,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                        + TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(0, 1000)),
                    onRetry: (outcome, delay, retryCount, _) =>
                    {
                        _logger.LogWarning(
                            outcome.Exception,
                            "OIDC discovery retry {RetryCount} after {Delay}ms. Status: {StatusCode}",
                            retryCount,
                            (int)delay.TotalMilliseconds,
                            outcome.Result?.StatusCode);
                    });
        }

        public async Task<(Uri AuthorizationEndpoint, Uri TokenEndpoint)> ResolveEndpointsAsync(string authority, CancellationToken cancellationToken = default)
        {
            EnsureArg.IsNotNullOrWhiteSpace(authority, nameof(authority));

            string normalizedAuthority = authority.TrimEnd('/');

            if (_cache.TryGetValue(normalizedAuthority, out var cached))
            {
                if (DateTimeOffset.UtcNow - cached.Timestamp < _cacheDuration)
                {
                    return (cached.AuthorizationEndpoint, cached.TokenEndpoint);
                }

                _cache.TryRemove(normalizedAuthority, out _);
            }

            var endpoints = await DiscoverEndpointsAsync(normalizedAuthority, cancellationToken);
            _cache.TryAdd(normalizedAuthority, (endpoints.AuthorizationEndpoint, endpoints.TokenEndpoint, DateTimeOffset.UtcNow));
            return endpoints;
        }

        private async Task<(Uri AuthorizationEndpoint, Uri TokenEndpoint)> DiscoverEndpointsAsync(string authority, CancellationToken cancellationToken)
        {
            try
            {
                string openIdConfigUrl = authority + "/.well-known/openid-configuration";

                using HttpClient httpClient = _httpClientFactory.CreateClient();
                using HttpResponseMessage response = await _retryPolicy.ExecuteAsync(
                    ct => httpClient.GetAsync(new Uri(openIdConfigUrl), ct),
                    cancellationToken);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync(cancellationToken);
                using JsonDocument openIdConfig = JsonDocument.Parse(json);

                string authEndpoint = openIdConfig.RootElement.TryGetProperty("authorization_endpoint", out var authProp) ? authProp.GetString() : null;
                string tokenEndpoint = openIdConfig.RootElement.TryGetProperty("token_endpoint", out var tokenProp) ? tokenProp.GetString() : null;

                if (!string.IsNullOrEmpty(authEndpoint) && !string.IsNullOrEmpty(tokenEndpoint))
                {
                    return (new Uri(authEndpoint), new Uri(tokenEndpoint));
                }

                _logger.LogWarning("OIDC discovery document at {Url} did not contain authorization_endpoint or token_endpoint. Falling back to Entra ID URL pattern.", openIdConfigUrl);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Failed to fetch OIDC discovery document for authority {Authority}. Falling back to Entra ID URL pattern.", authority);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "Failed to fetch OIDC discovery document for authority {Authority}. Falling back to Entra ID URL pattern.", authority);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to fetch OIDC discovery document for authority {Authority}. Falling back to Entra ID URL pattern.", authority);
            }
            catch (UriFormatException ex)
            {
                _logger.LogWarning(ex, "Failed to fetch OIDC discovery document for authority {Authority}. Falling back to Entra ID URL pattern.", authority);
            }

            // Fallback: assume Entra ID URL pattern
            return (
                new Uri(authority + "/oauth2/v2.0/authorize"),
                new Uri(authority + "/oauth2/v2.0/token"));
        }

        private static bool IsTransientStatusCode(HttpStatusCode code) =>
            code == HttpStatusCode.TooManyRequests || (int)code >= 500;
    }
}
