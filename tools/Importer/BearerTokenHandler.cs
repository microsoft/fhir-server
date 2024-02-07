// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Importer;

public class BearerTokenHandler : DelegatingHandler
{
    private readonly Dictionary<string, AccessTokenCache> _accessTokenCaches = [];

    public BearerTokenHandler(TokenCredential tokenCredential, Uri[] baseAddresses, string[] scopes)
        : this(tokenCredential, baseAddresses, scopes, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30))
    {
    }

    internal BearerTokenHandler(
        TokenCredential tokenCredential,
        Uri[] baseAddresses,
        string[] scopes,
        TimeSpan tokenRefreshOffset,
        TimeSpan tokenRefreshRetryDelay)
    {
        if (scopes.Length == 0)
        {
            scopes = baseAddresses.Select(ba => $"{ba.GetLeftPart(UriPartial.Authority)}/.default").ToArray();
        }

        if (scopes.Length != baseAddresses.Length)
        {
            throw new ArgumentException("The number of scopes must match the number of base addresses.", nameof(scopes));
        }

        foreach ((Uri baseAddress, string scope) in baseAddresses.Zip(scopes, (ba, s) => (ba, s)))
        {
            _accessTokenCaches.Add(baseAddress.GetLeftPart(UriPartial.Authority), new AccessTokenCache(tokenCredential, scope, tokenRefreshOffset, tokenRefreshRetryDelay));
        }

        InnerHandler = new HttpClientHandler();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Only add header for requests that don't already have one.
        if (request is null || request.Headers is null || request.Headers.Authorization is not null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        if (request.RequestUri.Scheme != Uri.UriSchemeHttps && request.RequestUri.Host != "localhost")
        {
            throw new InvalidOperationException("Bearer token authentication is not permitted for non TLS protected (https) endpoints.");
        }

        if (_accessTokenCaches.TryGetValue(request.RequestUri.GetLeftPart(UriPartial.Authority), out AccessTokenCache tc))
        {
            AccessToken cachedToken = await tc.GetTokenAsync(cancellationToken).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cachedToken.Token);
        }

        // Send the request.
        return await base.SendAsync(request, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach ((string _, AccessTokenCache ac) in _accessTokenCaches)
            {
                ac.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private sealed class AccessTokenCache(
                   TokenCredential tokenCredential,
                   string scope,
                   TimeSpan tokenRefreshOffset,
                   TimeSpan tokenRefreshRetryDelay) : IDisposable
    {
        private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
        private bool _disposed;
        private readonly TokenCredential _tokenCredential = tokenCredential;
        private readonly string _scope = scope;
        private readonly TimeSpan _tokenRefreshOffset = tokenRefreshOffset;
        private readonly TimeSpan _tokenRefreshRetryDelay = tokenRefreshRetryDelay;
        private AccessToken? _accessToken;
        private DateTimeOffset _accessTokenExpiration;

        public async Task<AccessToken> GetTokenAsync(CancellationToken cancellationToken)
        {
            await _semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_accessToken is null || _accessTokenExpiration <= DateTimeOffset.UtcNow + _tokenRefreshOffset)
                {
                    try
                    {
                        Console.WriteLine($"Getting new token for scope {_scope}...");
                        _accessToken = await _tokenCredential.GetTokenAsync(new TokenRequestContext([_scope]), cancellationToken).ConfigureAwait(false);
                        _accessTokenExpiration = _accessToken.Value.ExpiresOn;
                        PrintClaimsFromJwt(_accessToken.Value.Token);
                    }
                    catch (AuthenticationFailedException)
                    {
                        // If the token acquisition fails, retry after the delay.
                        await Task.Delay(_tokenRefreshRetryDelay, cancellationToken).ConfigureAwait(false);
                        _accessToken = await _tokenCredential.GetTokenAsync(new TokenRequestContext([_scope]), cancellationToken).ConfigureAwait(false);
                        _accessTokenExpiration = _accessToken.Value.ExpiresOn;
                        PrintClaimsFromJwt(_accessToken.Value.Token);
                    }
                }

                return _accessToken.Value;
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _semaphoreSlim?.Dispose();
            }

            _disposed = true;
        }

        private static void PrintClaimsFromJwt(string jwtToken)
        {
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(jwtToken) as JwtSecurityToken;
            var payload = JObject.Parse(jsonToken.Payload.SerializeToJson());

            // List of claims to check and print
            string[] claimTypes = ["name", "oid", "tid", "aud", "iss", "scp"];

            foreach (var claimType in claimTypes)
            {
                if (payload.TryGetValue(claimType, out JToken claimValue))
                {
                    Console.WriteLine($"{claimType}: {claimValue}");
                }
            }
        }
    }
}
