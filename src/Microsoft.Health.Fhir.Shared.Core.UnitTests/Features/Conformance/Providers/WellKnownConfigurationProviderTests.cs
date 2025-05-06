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
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Conformance.Providers;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance.Providers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class WellKnownConfigurationProviderTests
    {
        private readonly SecurityConfiguration _securityConfiguration;
        private readonly TestHttpMessageHandler _messageHandler;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly WellKnownConfigurationProvider _provider;

        public WellKnownConfigurationProviderTests()
        {
            _securityConfiguration = new SecurityConfiguration();
            _securityConfiguration.Authorization.Enabled = true;
            _securityConfiguration.Authentication.Authority = "https://testhost:44312/OAuth2/v2.0";
            _securityConfiguration.SmartAuthentication.Authority = "https://testhost:44312/Smart/OAuth2/v2.0";

            _messageHandler = new TestHttpMessageHandler();
            _httpClientFactory = Substitute.For<IHttpClientFactory>();
            _httpClientFactory.CreateClient().Returns(new HttpClient(_messageHandler));

            _provider = new WellKnownConfigurationProvider(
                Options.Create(_securityConfiguration),
                _httpClientFactory,
                NullLogger<WellKnownConfigurationProvider>.Instance);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void GivenSmartAuthorityIsNullOrEmpty_WhenIsSmartConfiguredCalled_ReturnsFalse(string authority)
        {
            _securityConfiguration.SmartAuthentication.Authority = authority;

            Assert.False(_provider.IsSmartConfigured());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task GivenOAuthAuthorityAndSmartAuthorityAreNullOrEmpty_WhenRetrievingOpenIdConfiguration_ReturnsNull(string authority)
        {
            _securityConfiguration.Authentication.Authority = authority;
            _securityConfiguration.SmartAuthentication.Authority = authority;

            OpenIdConfigurationResponse response = await _provider.GetOpenIdConfigurationAsync(CancellationToken.None);

            Assert.Null(response);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task GivenOAuthAuthorityAndSmartAuthorityAreNullOrEmpty_WhenRetrievingSmartConfiguration_ReturnsNull(string authority)
        {
            _securityConfiguration.Authentication.Authority = authority;
            _securityConfiguration.SmartAuthentication.Authority = authority;

            GetSmartConfigurationResponse response = await _provider.GetSmartConfigurationAsync(CancellationToken.None);

            Assert.Null(response);
        }

        [Theory]
        [InlineData(500)]
        [InlineData(400)]
        public async Task GivenSmartConfigurationRequestFails_WhenRetrievingSmartConfiguration_ReturnsNull(int code)
        {
            SetResponseBody(code);

            GetSmartConfigurationResponse response = await _provider.GetSmartConfigurationAsync(CancellationToken.None);

            Assert.Null(response);
        }

        [Fact]
        public async Task GivenResponseIsNotValidJson_WhenRetrievingOpenIdConfiguration_ReturnsNull()
        {
            SetResponseBody(new string[] { "Invalid" });

            OpenIdConfigurationResponse response = await _provider.GetOpenIdConfigurationAsync(CancellationToken.None);

            Assert.Null(response);
        }

        [Fact]
        public async Task GivenResponseIsNotValidJson_WhenRetrievingSmartConfiguration_ReturnsNull()
        {
            SetResponseBody(new string[] { "Invalid" });

            GetSmartConfigurationResponse response = await _provider.GetSmartConfigurationAsync(CancellationToken.None);

            Assert.Null(response);
        }

        [Theory]
        [InlineData(500)]
        [InlineData(400)]
        public async Task GivenOpenIdConfigurationRequestFails_WhenRetrievingOpenIdConfiguration_ReturnsNull(int code)
        {
            SetResponseBody(code);

            OpenIdConfigurationResponse response = await _provider.GetOpenIdConfigurationAsync(CancellationToken.None);

            Assert.Null(response);
        }

        [Fact]
        public async Task GivenSmartAuthorityIsNotNull_WhenRetrievingOpenIdConfiguration_ReturnsOpenIdConfigurationUsingSmartAuthority()
        {
            OpenIdConfigurationResponse expectedResponse = GetOpenIdConfiguration();
            SetResponseBody(expectedResponse);

            OpenIdConfigurationResponse response = await _provider.GetOpenIdConfigurationAsync(CancellationToken.None);

            Assert.Equal(expectedResponse.AuthorizationEndpoint, response.AuthorizationEndpoint);
            Assert.Equal(expectedResponse.TokenEndpoint, response.TokenEndpoint);
            Assert.Equal(new Uri($"{_securityConfiguration.SmartAuthentication.Authority}/.well-known/openid-configuration"), _messageHandler.Request.RequestUri);
        }

        [Fact]
        public async Task GivenSmartAuthorityIsNotNull_WhenRetrievingOpenIdConfiguration_ReturnsSmartConfigurationUsingSmartAuthority()
        {
            GetSmartConfigurationResponse expectedResponse = GetSmartConfiguration();
            SetResponseBody(expectedResponse);

            GetSmartConfigurationResponse response = await _provider.GetSmartConfigurationAsync(CancellationToken.None);

            Assert.Equal(expectedResponse.AuthorizationEndpoint, response.AuthorizationEndpoint);
            Assert.Equal(expectedResponse.TokenEndpoint, response.TokenEndpoint);
            Assert.Equal(new Uri($"{_securityConfiguration.SmartAuthentication.Authority}/.well-known/smart-configuration"), _messageHandler.Request.RequestUri);
        }

        [Fact]
        public async Task GivenNoSmartAuthorityIsNotNull_WhenRetrievingOpenIdConfiguration_ReturnsOpenIdConfigurationUsingOAuthAuthority()
        {
            _securityConfiguration.SmartAuthentication.Authority = null;

            OpenIdConfigurationResponse expectedResponse = GetOpenIdConfiguration();
            SetResponseBody(expectedResponse);

            OpenIdConfigurationResponse response = await _provider.GetOpenIdConfigurationAsync(CancellationToken.None);

            Assert.Equal(expectedResponse.AuthorizationEndpoint, response.AuthorizationEndpoint);
            Assert.Equal(expectedResponse.TokenEndpoint, response.TokenEndpoint);
            Assert.Equal(new Uri($"{_securityConfiguration.Authentication.Authority}/.well-known/openid-configuration"), _messageHandler.Request.RequestUri);
        }

        [Fact]
        public async Task GivenNoSmartAuthorityIsNotNull_WhenRetrievingOpenIdConfiguration_ReturnsSmartConfigurationUsingOAuthAuthority()
        {
            _securityConfiguration.SmartAuthentication.Authority = null;

            GetSmartConfigurationResponse expectedResponse = GetSmartConfiguration();
            SetResponseBody(expectedResponse);

            GetSmartConfigurationResponse response = await _provider.GetSmartConfigurationAsync(CancellationToken.None);

            Assert.Equal(expectedResponse.AuthorizationEndpoint, response.AuthorizationEndpoint);
            Assert.Equal(expectedResponse.TokenEndpoint, response.TokenEndpoint);
            Assert.Equal(new Uri($"{_securityConfiguration.Authentication.Authority}/.well-known/smart-configuration"), _messageHandler.Request.RequestUri);
        }

        private void SetResponseBody(object body, HttpStatusCode code = HttpStatusCode.OK)
        {
            var response = new HttpResponseMessage(code);

            if (body != null)
            {
                response.Content = new StringContent(JsonConvert.SerializeObject(body));
            }

            _messageHandler.Response = response;
        }

        private OpenIdConfigurationResponse GetOpenIdConfiguration()
        {
            return new OpenIdConfigurationResponse(new Uri("https://testhost:44312/openid/auth"), new Uri("https://testhost:44312/openid/token"));
        }

        private GetSmartConfigurationResponse GetSmartConfiguration()
        {
            return new GetSmartConfigurationResponse(new Uri("https://testhost:44312/smart/auth"), new Uri("https://testhost:44312/smart/token"));
        }

        internal class TestHttpMessageHandler : DelegatingHandler
        {
            public HttpResponseMessage Response { get; set; }

            public HttpRequestMessage Request { get; set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Request = request;
                return await Task.FromResult(Response);
            }
        }
    }
}
