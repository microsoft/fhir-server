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
        private readonly HttpIntegrationTestFixture _fixture;
        private readonly Uri _tokenUri;
        private readonly Uri _introspectionUri;

        public TokenIntrospectionTests(HttpIntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _httpClient = fixture.TestFhirClient.HttpClient;
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
            var responseJson = await introspectionResponse.Content.ReadAsStringAsync();
            Assert.True(introspectionResponse.StatusCode == HttpStatusCode.OK, $"Expected OK status but got {introspectionResponse.StatusCode}. Response: {responseJson}");

            var response = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseJson);

            // Verify active status
            Assert.True(response.TryGetValue("active", out JsonElement activeElement), $"Response missing 'active' claim. Response: {responseJson}");
            Assert.True(activeElement.GetBoolean(), $"Expected active=true but got active=false. Response: {responseJson}");

            // Verify token type
            Assert.True(response.TryGetValue("token_type", out JsonElement tokenTypeElement), $"Response missing 'token_type' claim. Response: {responseJson}");
            Assert.Equal("Bearer", tokenTypeElement.GetString());

            // Verify standard claims exist
            Assert.True(response.TryGetValue("sub", out _), $"Response missing 'sub' claim. Response: {responseJson}");
            Assert.True(response.TryGetValue("iss", out _), $"Response missing 'iss' claim. Response: {responseJson}");
            Assert.True(response.TryGetValue("aud", out _), $"Response missing 'aud' claim. Response: {responseJson}");
            Assert.True(response.TryGetValue("exp", out JsonElement expirationElement), $"Response missing 'exp' claim. Response: {responseJson}");
            Assert.True(response.TryGetValue("client_id", out _), $"Response missing 'client_id' claim. Response: {responseJson}");

            // Verify timestamps are Unix timestamps (positive numbers)
            Assert.True(expirationElement.GetInt64() > 0);
            if (response.TryGetValue("iat", out JsonElement issuedAtElement))
            {
                Assert.True(issuedAtElement.GetInt64() > 0);
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

            Assert.True(
                response.TryGetValue("active", out JsonElement activeScopeElement) && activeScopeElement.GetBoolean(),
                $"Expected active=true but token is inactive or missing 'active' claim. Response: {responseJson}");

            // Scope should be a space-separated string, not an array
            if (response.TryGetValue("scope", out JsonElement scopeElement))
            {
                Assert.Equal(JsonValueKind.String, scopeElement.ValueKind);
                var scope = scopeElement.GetString();
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

            Assert.True(
                response.TryGetValue("active", out JsonElement activeFhirUserElement) && activeFhirUserElement.GetBoolean(),
                $"Expected active=true for SmartUserClient but token is inactive or missing 'active' claim. Response: {responseJson}");

            // SMART clients should have fhirUser claim
            if (response.TryGetValue("fhirUser", out JsonElement fhirUserElement))
            {
                var fhirUser = fhirUserElement.GetString();
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
            Assert.True(response.TryGetValue("active", out JsonElement inactiveElement));
            Assert.False(inactiveElement.GetBoolean());

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

            Assert.True(response.TryGetValue("active", out JsonElement inactiveElement));
            Assert.False(inactiveElement.GetBoolean());
            Assert.Single(response); // Only 'active' field
        }

        [Fact]
        public async Task GivenMissingTokenParameter_WhenIntrospecting_ThenReturnsBadRequest()
        {
            // Arrange
            var accessToken = await GetAccessTokenAsync(TestApplications.GlobalAdminServicePrincipal);

            // Act - Send request without token parameter
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>());
            using var request = new HttpRequestMessage(HttpMethod.Post, _introspectionUri)
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
            // Arrange - Get a token to introspect
            var someToken = await GetAccessTokenAsync(TestApplications.GlobalAdminServicePrincipal);

            // Act - Send request with NO authentication header (completely unauthenticated)
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "token", someToken },
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, _introspectionUri)
            {
                Content = content,
            };

            // Create an unauthenticated client using the test infrastructure's message handler
            // This ensures requests are properly routed to the in-process test server without auth
            using var unauthenticatedHandler = new TestAuthenticationHttpMessageHandler(null)
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
            var adminResponseJson = await adminIntrospection.Content.ReadAsStringAsync();
            var readerResponseJson = await readerIntrospection.Content.ReadAsStringAsync();
            var adminResponse = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(adminResponseJson);
            var readerResponse = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(readerResponseJson);

            Assert.True(
                adminResponse.TryGetValue("active", out JsonElement adminActiveElement) && adminActiveElement.GetBoolean(),
                $"Expected admin token to be active. Response: {adminResponseJson}");
            Assert.True(
                readerResponse.TryGetValue("active", out JsonElement readerActiveElement) && readerActiveElement.GetBoolean(),
                $"Expected reader token to be active. Response: {readerResponseJson}");

            // Verify different client_ids
            Assert.True(adminResponse.TryGetValue("client_id", out JsonElement adminClientIdElement), $"Admin response missing 'client_id'. Response: {adminResponseJson}");
            Assert.True(readerResponse.TryGetValue("client_id", out JsonElement readerClientIdElement), $"Reader response missing 'client_id'. Response: {readerResponseJson}");
            var adminClientId = adminClientIdElement.GetString();
            var readerClientId = readerClientIdElement.GetString();
            Assert.NotEqual(adminClientId, readerClientId);
        }

        /// <summary>
        /// Helper method to get an access token from OpenIddict token endpoint.
        /// </summary>
        private async Task<string> GetAccessTokenAsync(TestApplication testApplication)
        {
            var tokenRequest = BuildTokenRequest(testApplication);

            using var content = new FormUrlEncodedContent(tokenRequest);

            var response = await _httpClient.PostAsync(_tokenUri, content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseJson);

            return tokenResponse["access_token"].GetString();
        }

        private static IDictionary<string, string> BuildTokenRequest(TestApplication testApplication)
        {
            var (scope, resource) = ResolveAudienceParameters(testApplication);

            var request = new Dictionary<string, string>
            {
                { "grant_type", testApplication.GrantType },
                { "client_id", testApplication.ClientId },
                { "client_secret", testApplication.ClientSecret },
            };

            if (!string.IsNullOrWhiteSpace(scope))
            {
                request["scope"] = scope;
            }

            if (!string.IsNullOrWhiteSpace(resource))
            {
                request["resource"] = resource;
            }

            return request;
        }

        private static (string Scope, string Resource) ResolveAudienceParameters(TestApplication testApplication)
        {
            bool isWrongAudienceClient = testApplication.Equals(TestApplications.WrongAudienceClient);

            string scope = isWrongAudienceClient ? testApplication.ClientId : AuthenticationSettings.Scope;
            string resource = isWrongAudienceClient ? testApplication.ClientId : AuthenticationSettings.Resource;

            return (scope, resource);
        }

        /// <summary>
        /// Helper method to introspect a token using the introspection endpoint.
        /// </summary>
        private async Task<HttpResponseMessage> IntrospectTokenAsync(string authToken, string tokenToIntrospect)
        {
            using (var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "token", tokenToIntrospect },
            }))
            using (var request = new HttpRequestMessage(HttpMethod.Post, _introspectionUri)
            {
                Content = content,
            })
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
                return await _httpClient.SendAsync(request);
            }
        }
    }
}
