// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Microsoft.AzureHealth.DataServices.Pipelines;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SMARTCustomOperations.AzureAuth.Configuration;
using SMARTCustomOperations.AzureAuth.Filters;
using SMARTCustomOperations.AzureAuth.Services;

namespace SMARTCustomOperations.AzureAuth.UnitTests.Filters
{
    public class TokenInputFilterTests
    {
        private static IAsymmetricAuthorizationService _backendAuthService = Substitute.For<IAsymmetricAuthorizationService>();
        private static ILogger<TokenInputFilter> _logger = Substitute.For<ILogger<TokenInputFilter>>();
        private static string _testClientAssertion = "eyJ0eXAiOiJKV1QiLCJraWQiOiJhZmIyN2MyODRmMmQ5Mzk1OWMxOGZhMDMyMGUzMjA2MCIsImFsZyI6IkVTMzg0In0.eyJpc3MiOiJkZW1vX2FwcF93aGF0ZXZlciIsInN1YiI6ImRlbW9fYXBwX3doYXRldmVyIiwiYXVkIjoiaHR0cHM6Ly9zbWFydC5hcmdvLnJ1bi92L3I0L3NpbS9leUp0SWpvaU1TSXNJbXNpT2lJeElpd2lhU0k2SWpFaUxDSnFJam9pTVNJc0ltSWlPaUk0TjJFek16bGtNQzA0WTJGbExUUXhPR1V0T0Rsak55MDROalV4WlRaaFlXSXpZellpZlEvYXV0aC90b2tlbiIsImp0aSI6ImQ4MDJiZDcyY2ZlYTA2MzVhM2EyN2IwODE3YjgxZTQxZTBmNzQzMzE4MTg4OTM4YjAxMmMyMDM2NmJkZmU4YTEiLCJleHAiOjE2MzM1MzIxMzR9.eHUtXmppOLIMAfBM4mFpcgJ90bDNYWQpkm7--yRS2LY5HoXwr3FpqHMTrjhK60r5kgYGFg6v9IQaUFKFpn1N2Eyty62JWxvGXRlgEDbdX9wAAr8TeWnsAT_2orfpn6wz";
        private static string _testClientAssertionDecodedClientId = "demo_app_whatever";

        private static AzureAuthOperationsConfig config = new()
        {
            TenantId = "xxxx-xxxx-xxxx-xxxx",
            Audience = "12345678-90ab-cdef-1234-567890abcdef",
        };

        [Fact]
        public async Task GivenAClientConfidentialTokenRequest_WhenFilterExecuted_ThenRequestIsProperlyFormed()
        {
            var filter = new TokenInputFilter(_logger, config, _backendAuthService);

            OperationContext context = new();
            context.Request = new HttpRequestMessage();
            context.Request.Method = HttpMethod.Post;
            context.Request.RequestUri = new Uri("http://localhost/token");
            context.Request.Content = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("code", "12345678"),
                new KeyValuePair<string, string>("redirect_uri", "http://localhost/"),
                new KeyValuePair<string, string>("client_id", "xxxx-xxxxx-xxxxx-xxxxx"),
                new KeyValuePair<string, string>("client_secret", "super-secret"),
            });

            await filter.ExecuteAsync(context);

            // Status code of 0 means request needs to be handled by the binding
            Assert.Equal((HttpStatusCode)0, context.StatusCode);
            Assert.Equal("login.microsoftonline.com", context.Request.RequestUri.Host);
            Assert.Equal($"/{config.TenantId}/oauth2/v2.0/token", context.Request.RequestUri.AbsolutePath);
            Assert.Equal(0, context.Headers.Count(x => x.Name == "Origin"));
        }

        [Fact]
        public async Task GivenAPublicTokenRequest_WhenFilterExecuted_ThenRequestIsProperlyFormed()
        {
            var filter = new TokenInputFilter(_logger, config, _backendAuthService);

            OperationContext context = new();
            context.Request = new HttpRequestMessage();
            context.Request.Method = HttpMethod.Post;
            context.Request.RequestUri = new Uri("http://localhost/token");
            context.Request.Content = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("code", "12345678"),
                new KeyValuePair<string, string>("redirect_uri", "http://localhost/"),
                new KeyValuePair<string, string>("client_id", "xxxx-xxxxx-xxxxx-xxxxx"),
                new KeyValuePair<string, string>("code_verifier", "456"),
            });

            await filter.ExecuteAsync(context);

            // Status code of 0 means request needs to be handled by the binding
            Assert.Equal((HttpStatusCode)0, context.StatusCode);
            Assert.Equal("login.microsoftonline.com", context.Request.RequestUri.Host);
            Assert.Equal($"/{config.TenantId}/oauth2/v2.0/token", context.Request.RequestUri.AbsolutePath);
            Assert.Equal(1, context.Headers.Count(x => x.Name == "Origin"));
        }

        [Fact]
        public async Task GivenARefreshTokenRequest_WhenFilterExecuted_ThenRequestIsProperlyFormed()
        {
            var filter = new TokenInputFilter(_logger, config, _backendAuthService);

            OperationContext context = new();
            context.Request = new HttpRequestMessage();
            context.Request.Method = HttpMethod.Post;
            context.Request.RequestUri = new Uri("http://localhost/token");
            context.Request.Content = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", "12345678"),
                new KeyValuePair<string, string>("client_id", "xxxx-xxxxx-xxxxx-xxxxx"),
                new KeyValuePair<string, string>("client_secret", "my-secret"),
                new KeyValuePair<string, string>("scope", "patient/*.read"),
            });

            await filter.ExecuteAsync(context);

            // Status code of 0 means request needs to be handled by the binding
            Assert.Equal((HttpStatusCode)0, context.StatusCode);
            Assert.Equal("login.microsoftonline.com", context.Request.RequestUri.Host);
            Assert.Equal($"/{config.TenantId}/oauth2/v2.0/token", context.Request.RequestUri.AbsolutePath);
            Assert.Equal(0, context.Headers.Count(x => x.Name == "Origin"));
        }

        [Fact]
        public async Task GivenABackendServiceTokenRequest_WhenFilterExecuted_ThenRequestIsProperlyFormed()
        {
            var filter = new TokenInputFilter(_logger, config, _backendAuthService);

            var requestContent = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("scope", "system/*.read"),
                new KeyValuePair<string, string>("client_assertion_type", "urn:ietf:params:oauth:client-assertion-type:jwt-bearer"),
                new KeyValuePair<string, string>("client_assertion", _testClientAssertion),
            };

            OperationContext context = new();
            context.Request = new HttpRequestMessage();
            context.Request.Method = HttpMethod.Post;
            context.Request.RequestUri = new Uri("http://localhost/token");
            context.Request.Content = new FormUrlEncodedContent(requestContent);

            var backendClientSubstitute = Substitute.For<BackendClientConfiguration>();

            var testRtn = new BackendClientConfiguration("demo_app_whatever", "my-secret", "http://me.com/jwks.json");

            _backendAuthService.AuthenticateBackendAsyncClient(_testClientAssertionDecodedClientId, _testClientAssertion)
                .Returns(Task.FromResult(testRtn));

            await filter.ExecuteAsync(context);

            // Status code of 0 means request needs to be handled by the binding
            Assert.Equal((HttpStatusCode)0, context.StatusCode);
            Assert.Equal("login.microsoftonline.com", context.Request.RequestUri.Host);
            Assert.Equal($"/{config.TenantId}/oauth2/v2.0/token", context.Request.RequestUri.AbsolutePath);
            Assert.Equal(0, context.Headers.Count(x => x.Name == "Origin"));
        }
    }
}
