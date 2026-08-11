// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Text.Json;

namespace Microsoft.Health.Fhir.Mcp;

internal sealed class FhirAccessTokenProvider : IFhirAccessTokenProvider, IDisposable
{
    private static readonly TimeSpan TokenRefreshSkew = TimeSpan.FromMinutes(1);
    private readonly FhirMcpOptions _options;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _expiresAt;

    public FhirAccessTokenProvider(FhirMcpOptions options, HttpClient httpClient)
    {
        _options = options;
        _httpClient = httpClient;
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_options.BearerToken is not null)
        {
            return _options.BearerToken;
        }

        if (_options.ClientId is null)
        {
            return null;
        }

        if (_cachedToken is not null && _expiresAt - TokenRefreshSkew > DateTimeOffset.UtcNow)
        {
            return _cachedToken;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is not null && _expiresAt - TokenRefreshSkew > DateTimeOffset.UtcNow)
            {
                return _cachedToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenAddress);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = _options.ClientId,
                    ["client_secret"] = _options.ClientSecret!,
                    ["scope"] = _options.Scope,
                });

            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"FHIR token request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument tokenDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
            JsonElement root = tokenDocument.RootElement;
            _cachedToken = root.TryGetProperty("access_token", out JsonElement tokenElement)
                ? tokenElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(_cachedToken))
            {
                throw new InvalidOperationException("FHIR token response did not contain an access_token.");
            }

            int expiresIn = root.TryGetProperty("expires_in", out JsonElement expiresElement) && expiresElement.TryGetInt32(out int seconds)
                ? seconds
                : 300;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _cachedToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Dispose()
    {
        _refreshLock.Dispose();
    }
}
