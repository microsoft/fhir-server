// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Xunit;

namespace Microsoft.Health.Fhir.Smart.Tests.E2E
{
    /// <summary>
    /// E2E tests for RFC 7662 Token Introspection endpoint using real OpenIddict tokens.
    /// </summary>
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class TokenIntrospectionTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly HttpClient _httpClient;
        private readonly TestFhirClient _testFhirClient;
        private readonly HttpIntegrationTestFixture _fixture;
        private readonly Uri _tokenUri;
        private readonly Uri _introspectionUri;

        public TokenIntrospectionTests(HttpIntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _httpClient = fixture.TestFhirClient.HttpClient;
            _testFhirClient = fixture.TestFhirClient;
            _tokenUri = fixture.TestFhirServer.TokenUri;
            _introspectionUri = new Uri(fixture.TestFhirServer.BaseAddress, "/connect/introspect");
        }

        [Fact]
        public async Task GivenValidToken_WhenIntrospecting_ThenReturnsActiveWithStandardClaims()
        {
            // Arrange - Get a real access token from OpenIddict
            var accessToken = await GetAccessTokenAsync(TestApplications.GlobalAdminServicePrincipal);

            // Act - Introspect the token
            var introspectionResponse = await IntrospectTokenAsync(accessToken, accessToken);

            // Assert - Verify RFC 7662 compliance
            Assert.Equal(HttpStatusCode.OK, introspectionResponse.StatusCode);

            var responseJson = await introspectionResponse.Content.ReadAsStringAsync();
            var response = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseJson);

            // Verify active status
            Assert.True(response.ContainsKey("active"));
            Assert.True(response["active"].GetBoolean());

            // Verify token type
            Assert.True(response.ContainsKey("token_type"));
            Assert.Equal("Bearer", response["token_type"].GetString());

            // Verify standard claims exist
            Assert.True(response.ContainsKey("sub"));
            Assert.True(response.ContainsKey("iss"));
            Assert.True(response.ContainsKey("aud"));
            Assert.True(response.ContainsKey("exp"));
            Assert.True(response.ContainsKey("client_id"));

            // Verify timestamps are Unix timestamps (positive numbers)
            Assert.True(response["exp"].GetInt64() > 0);
            if (response.ContainsKey("iat"))
            {
                Assert.True(response["iat"].GetInt64() > 0);
            }
        }

        [Fact]
        public async Task GivenValidToken_WhenIntrospecting_ThenReturnsScopeAsString()
        {
            // Arrange
            var accessToken = await GetAccessTokenAsync(TestApplications.GlobalAdminServicePrincipal);

            // Act
            var introspectionResponse = await IntrospectTokenAsync(accessToken, accessToken);

            // Assert
            var responseJson = await introspectionResponse.Content.ReadAsStringAsync();
            var response = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseJson);

            Assert.True(response["active"].GetBoolean());

            // Scope should be a space-separated string, not an array
            if (response.ContainsKey("scope"))
            {
                Assert.Equal(JsonValueKind.String, response["scope"].ValueKind);
                var scope = response["scope"].GetString();
                Assert.NotEmpty(scope);
            }
        }

        [Fact]
        public async Task GivenTokenWithFhirUser_WhenIntrospecting_ThenReturnsSmartClaims()
        {
            // Arrange - Get token from a SMART user client
            var accessToken = await GetAccessTokenAsync(TestApplications.SmartUserClient);

            // Act
            var introspectionResponse = await IntrospectTokenAsync(accessToken, accessToken);

            // Assert
            var responseJson = await introspectionResponse.Content.ReadAsStringAsync();
            var response = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseJson);

            Assert.True(response["active"].GetBoolean());

            // SMART clients should have fhirUser claim
            if (response.ContainsKey("fhirUser"))
            {
                var fhirUser = response["fhirUser"].GetString();
                Assert.NotEmpty(fhirUser);

                // fhirUser should be a full URL to a Practitioner, Patient, Person, or RelatedPerson
                Assert.Contains("http", fhirUser, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public async Task GivenInvalidToken_WhenIntrospecting_ThenReturnsInactive()
        {
            // Arrange - Use an invalid token
            var accessToken = await GetAccessTokenAsync(TestApplications.GlobalAdminServicePrincipal);
            var invalidToken = "invalid.jwt.token";

            // Act
            var introspectionResponse = await IntrospectTokenAsync(accessToken, invalidToken);

            // Assert - Should return 200 OK with active=false per RFC 7662
            Assert.Equal(HttpStatusCode.OK, introspectionResponse.StatusCode);

            var responseJson = await introspectionResponse.Content.ReadAsStringAsync();
            var response = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseJson);

            // Verify inactive status
            Assert.True(response.ContainsKey("active"));
            Assert.False(response["active"].GetBoolean());

            // Per RFC 7662 section 2.2: "If the introspection call is properly authorized
            // but the token is not active, the authorization server MUST return ... {"active": false}"
            // No other fields should be present
            Assert.Single(response);
        }

        [Fact]
        public async Task GivenMalformedToken_WhenIntrospecting_ThenReturnsInactive()
        {
            // Arrange
            var accessToken = await GetAccessTokenAsync(TestApplications.GlobalAdminServicePrincipal);
            var malformedToken = "not-even-three-parts";

            // Act
            var introspectionResponse = await IntrospectTokenAsync(accessToken, malformedToken);

            // Assert
            Assert.Equal(HttpStatusCode.OK, introspectionResponse.StatusCode);

            var responseJson = await introspectionResponse.Content.ReadAsStringAsync();
            var response = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseJson);

            Assert.False(response["active"].GetBoolean());
            Assert.Single(response); // Only 'active' field
        }

        [Fact]
        public async Task GivenMissingTokenParameter_WhenIntrospecting_ThenReturnsBadRequest()
        {
            // Arrange
            var accessToken = await GetAccessTokenAsync(TestApplications.GlobalAdminServicePrincipal);

            // Act - Send request without token parameter
            var content = new FormUrlEncodedContent(new Dictionary<string, string>());
            var request = new HttpRequestMessage(HttpMethod.Post, _introspectionUri)
            {
                Content = content,
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var introspectionResponse = await _httpClient.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, introspectionResponse.StatusCode);

            var responseJson = await introspectionResponse.Content.ReadAsStringAsync();
            Assert.Contains("token parameter is required", responseJson, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GivenNoAuthentication_WhenIntrospecting_ThenReturnsUnauthorized()
        {
            // This test is not working with in proc server
            Skip.If(_fixture.IsUsingInProcTestServer);

            // Arrange - Get a token to introspect
            var someToken = await GetAccessTokenAsync(TestApplications.GlobalAdminServicePrincipal);

            // Act - Send request with NO authentication header (completely unauthenticated)
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "token", someToken },
            });

            var request = new HttpRequestMessage(HttpMethod.Post, _introspectionUri)
            {
                Content = content,
            };

            // Use the existing httpClient but send the request without any Authorization header

            // Create an unauthenticated client using the test infrastructure's message handler
            // This ensures requests are properly routed to the in-process test server without auth
            var unauthenticatedHandler = new TestAuthenticationHttpMessageHandler(null)
            {
                InnerHandler = _fixture.TestFhirServer.CreateMessageHandler(),
            };
            using var unauthenticatedClient = new HttpClient(unauthenticatedHandler) { BaseAddress = _fixture.TestFhirServer.BaseAddress };
            var introspectionResponse = await unauthenticatedClient.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, introspectionResponse.StatusCode);
        }

        [Fact]
        public async Task GivenMultipleValidTokens_WhenIntrospecting_ThenEachReturnsCorrectClaims()
        {
            // Arrange - Get tokens from different applications
            var adminToken = await GetAccessTokenAsync(TestApplications.GlobalAdminServicePrincipal);
            var readerToken = await GetAccessTokenAsync(TestApplications.ReadOnlyUser);

            // Act - Introspect both tokens
            var adminIntrospection = await IntrospectTokenAsync(adminToken, adminToken);
            var readerIntrospection = await IntrospectTokenAsync(readerToken, readerToken);

            // Assert - Both should be active
            var adminResponse = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                await adminIntrospection.Content.ReadAsStringAsync());
            var readerResponse = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                await readerIntrospection.Content.ReadAsStringAsync());

            Assert.True(adminResponse["active"].GetBoolean());
            Assert.True(readerResponse["active"].GetBoolean());

            // Verify different client_ids
            var adminClientId = adminResponse["client_id"].GetString();
            var readerClientId = readerResponse["client_id"].GetString();
            Assert.NotEqual(adminClientId, readerClientId);
        }

        /// <summary>
        /// Helper method to get an access token from OpenIddict token endpoint.
        /// </summary>
        private async Task<string> GetAccessTokenAsync(TestApplication testApplication)
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", "client_credentials" },
                { "client_id", testApplication.ClientId },
                { "client_secret", testApplication.ClientSecret },
                { "scope", "fhir-api" },
            });

            var response = await _httpClient.PostAsync(_tokenUri, content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseJson);

            return tokenResponse["access_token"].GetString();
        }

        /// <summary>
        /// Helper method to introspect a token using the introspection endpoint.
        /// </summary>
        private async Task<HttpResponseMessage> IntrospectTokenAsync(string authToken, string tokenToIntrospect)
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "token", tokenToIntrospect },
            });

            var request = new HttpRequestMessage(HttpMethod.Post, _introspectionUri)
            {
                Content = content,
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authToken);

            return await _httpClient.SendAsync(request);
        }
    }
}
