// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Microsoft.Health.Internal.Fhir.IncludePerf.Benchmark
{
    /// <summary>
    /// Fetches bearer tokens from the in-process development identity provider exposed at
    /// <c>/connect/token</c>. The development provider uses the client id as the client secret, and derives
    /// the <c>fhirUser</c> claim from the client id, so a client application registered with the same id as a
    /// Patient resource yields a SMART token bound to that patient's compartment.
    /// </summary>
    internal sealed class TokenProvider
    {
        private readonly HttpClient _httpClient;
        private readonly Uri _endpoint;
        private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);

        internal TokenProvider(HttpClient httpClient, Uri endpoint)
        {
            _httpClient = httpClient;
            _endpoint = endpoint;
        }

        internal async Task<string> GetTokenAsync(string clientId, string scope, CancellationToken cancellationToken)
        {
            string key = clientId + "\n" + scope;
            if (_cache.TryGetValue(key, out string cached))
            {
                return cached;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_endpoint, "connect/token"))
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = clientId,
                    ["client_secret"] = clientId,
                    ["scope"] = scope,
                }),
            };

            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Token request failed for client '{0}' scope '{1}': {2} {3}",
                        clientId,
                        scope,
                        (int)response.StatusCode,
                        body));
            }

            using JsonDocument document = JsonDocument.Parse(body);
            string token = document.RootElement.GetProperty("access_token").GetString();

            _cache[key] = token;
            return token;
        }

        internal static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);
    }
}
