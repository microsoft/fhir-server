// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Security
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Security)]
    public class OidcDiscoveryServiceTests
    {
        [Fact]
        public async Task GivenValidAuthority_WhenDiscoverySucceeds_ThenCorrectEndpointsReturned()
        {
            string authority = "https://login.microsoftonline.com/tenant-id";
            string expectedAuth = authority + "/oauth2/v2.0/authorize";
            string expectedToken = authority + "/oauth2/v2.0/token";

            var service = CreateService(new OidcDiscoveryMessageHandler(expectedAuth, expectedToken));

            var (authEndpoint, tokenEndpoint) = await service.ResolveEndpointsAsync(authority);

            Assert.Equal(expectedAuth, authEndpoint.ToString());
            Assert.Equal(expectedToken, tokenEndpoint.ToString());
        }

        [Fact]
        public async Task GivenValidAuthority_WhenDiscoveryFails_ThenEntraIdFallbackUsed()
        {
            string authority = "https://login.microsoftonline.com/tenant-id";

            var service = CreateService(new FailingMessageHandler());

            var (authEndpoint, tokenEndpoint) = await service.ResolveEndpointsAsync(authority);

            Assert.Equal(authority + "/oauth2/v2.0/authorize", authEndpoint.ToString());
            Assert.Equal(authority + "/oauth2/v2.0/token", tokenEndpoint.ToString());
        }

        [Fact]
        public async Task GivenValidAuthority_WhenDiscoveryReturnsBadContent_ThenEntraIdFallbackUsed()
        {
            string authority = "https://login.microsoftonline.com/tenant-id";

            var service = CreateService(new EmptyDiscoveryMessageHandler());

            var (authEndpoint, tokenEndpoint) = await service.ResolveEndpointsAsync(authority);

            Assert.Equal(authority + "/oauth2/v2.0/authorize", authEndpoint.ToString());
            Assert.Equal(authority + "/oauth2/v2.0/token", tokenEndpoint.ToString());
        }

        [Fact]
        public async Task GivenSameAuthority_WhenCalledTwice_ThenDiscoveryOnlyCalledOnce()
        {
            string authority = "https://login.microsoftonline.com/tenant-id";
            var messageHandler = new OidcDiscoveryMessageHandler(
                authority + "/oauth2/v2.0/authorize",
                authority + "/oauth2/v2.0/token");

            var service = CreateService(messageHandler);

            await service.ResolveEndpointsAsync(authority);
            await service.ResolveEndpointsAsync(authority);

            Assert.Equal(1, messageHandler.CallCount);
        }

        [Fact]
        public async Task GivenDifferentAuthorities_WhenCalledForEach_ThenDiscoveryCalledForEach()
        {
            string authority1 = "https://login.microsoftonline.com/tenant-1";
            string authority2 = "https://login.microsoftonline.com/tenant-2";
            var messageHandler = new OidcDiscoveryMessageHandler(
                "https://example.com/authorize",
                "https://example.com/token");

            var service = CreateService(messageHandler);

            await service.ResolveEndpointsAsync(authority1);
            await service.ResolveEndpointsAsync(authority2);

            Assert.Equal(2, messageHandler.CallCount);
        }

        [Fact]
        public async Task GivenAuthorityWithTrailingSlash_WhenResolved_ThenNormalizedCorrectly()
        {
            string authority = "https://login.microsoftonline.com/tenant-id/";
            string expectedAuth = "https://login.microsoftonline.com/tenant-id/oauth2/v2.0/authorize";
            string expectedToken = "https://login.microsoftonline.com/tenant-id/oauth2/v2.0/token";

            var service = CreateService(new OidcDiscoveryMessageHandler(expectedAuth, expectedToken));

            var (authEndpoint, tokenEndpoint) = await service.ResolveEndpointsAsync(authority);

            Assert.Equal(expectedAuth, authEndpoint.ToString());
            Assert.Equal(expectedToken, tokenEndpoint.ToString());
        }

        private static OidcDiscoveryService CreateService(HttpMessageHandler messageHandler)
        {
            var httpClient = new HttpClient(messageHandler);
            var httpClientFactory = Substitute.For<IHttpClientFactory>();
            httpClientFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);
            return new OidcDiscoveryService(httpClientFactory, NullLogger<OidcDiscoveryService>.Instance);
        }

        private class OidcDiscoveryMessageHandler : HttpMessageHandler
        {
            private readonly string _authorizationEndpoint;
            private readonly string _tokenEndpoint;

            public int CallCount { get; private set; }

            public OidcDiscoveryMessageHandler(string authorizationEndpoint, string tokenEndpoint)
            {
                _authorizationEndpoint = authorizationEndpoint;
                _tokenEndpoint = tokenEndpoint;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                CallCount++;
                string json = $@"{{
                    ""authorization_endpoint"": ""{_authorizationEndpoint}"",
                    ""token_endpoint"": ""{_tokenEndpoint}"",
                    ""issuer"": ""https://example.com""
                }}";

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
                });
            }
        }

        private class FailingMessageHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }

        private class EmptyDiscoveryMessageHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
                });
            }
        }
    }
}
