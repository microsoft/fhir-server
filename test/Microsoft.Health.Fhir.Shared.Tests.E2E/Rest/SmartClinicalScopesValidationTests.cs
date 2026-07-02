// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Smart.Tests.E2E
{
    /// <summary>
    /// E2E tests validating SMART clinical scope handling in <c>SmartClinicalScopesMiddleware</c>.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Authorization)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class SmartClinicalScopesValidationTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly HttpClient _httpClient;
        private readonly Uri _tokenUri;
        private readonly Uri _baseAddress;

        public SmartClinicalScopesValidationTests(HttpIntegrationTestFixture fixture)
        {
            _httpClient = fixture.TestFhirClient.HttpClient;
            _tokenUri = fixture.TestFhirServer.TokenUri;
            _baseAddress = fixture.TestFhirServer.BaseAddress;
        }

        [Fact]
        public async Task GivenMixedSmartV1AndV2Scopes_WhenRequestingFhirResource_ThenBadRequestIsReturned()
        {
            // Only meaningful when security (and the dev token endpoint) is enabled.
            Skip.If(_tokenUri == null, "Security is not enabled on this test server.");

            // A token that contains both a SMART v1 scope (.read) and a SMART v2 granular scope (.rs)
            // for the same resource type. The middleware must reject this combination with HTTP 400.
            const string mixedScope = "patient/Patient.read patient/Patient.rs";

            string accessToken = await GetAccessTokenWithScopeAsync(TestApplications.SmartUserClient, mixedScope);

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseAddress, "Patient"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using HttpResponseMessage response = await _httpClient.SendAsync(request);

            string body = await response.Content.ReadAsStringAsync();

            Assert.True(
                response.StatusCode == HttpStatusCode.BadRequest,
                $"Expected 400 BadRequest for mixed SMART scopes but got {(int)response.StatusCode} {response.StatusCode}. Response: {body}");

            Assert.Contains("OperationOutcome", body, StringComparison.Ordinal);
            Assert.Contains("\"code\":\"invalid\"", body, StringComparison.Ordinal);
        }

        private async Task<string> GetAccessTokenWithScopeAsync(TestApplication testApplication, string scope)
        {
            var tokenRequest = new Dictionary<string, string>
            {
                { "grant_type", testApplication.GrantType },
                { "client_id", testApplication.ClientId },
                { "client_secret", testApplication.ClientSecret },
                { "scope", scope },
                { "resource", AuthenticationSettings.Resource },
            };

            using var content = new FormUrlEncodedContent(tokenRequest);

            using HttpResponseMessage response = await _httpClient.PostAsync(_tokenUri, content);
            string responseJson = await response.Content.ReadAsStringAsync();

            Assert.True(
                response.IsSuccessStatusCode,
                $"Failed to acquire token for scope '{scope}'. Status {(int)response.StatusCode}. Response: {responseJson}");

            var tokenResponse = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseJson);
            return tokenResponse["access_token"].GetString();
        }
    }
}
