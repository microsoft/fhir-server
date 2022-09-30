// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Azure.ContainerRegistry;
using Microsoft.Health.Fhir.Azure.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests.ContainerRegistry
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class AzureContainerRegistryAccessTokenProviderTests
    {
        private const string RegistryServer = "test.azurecr.io";
        private ConvertDataConfiguration _convertDataConfiguration = new ConvertDataConfiguration();

        public AzureContainerRegistryAccessTokenProviderTests()
        {
            _convertDataConfiguration.ContainerRegistryServers.Add(RegistryServer);
        }

        [Fact]
        public async Task GivenARegistry_WithoutMIEnabled_WhenGetToken_TokenException_ShouldBeThrown()
        {
            IAccessTokenProvider tokenProvider = new AzureAccessTokenProvider(new NullLogger<AzureAccessTokenProvider>());
            IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
            var httpClient = new HttpClient();
            httpClientFactory.CreateClient().ReturnsForAnyArgs(httpClient);
            AzureContainerRegistryAccessTokenProvider acrTokenProvider = new AzureContainerRegistryAccessTokenProvider(tokenProvider, httpClientFactory, Options.Create(_convertDataConfiguration), new NullLogger<AzureContainerRegistryAccessTokenProvider>());

            await Assert.ThrowsAsync<AzureContainerRegistryTokenException>(() => acrTokenProvider.GetTokenAsync(RegistryServer, default));
        }

        [Fact]
        public async Task GivenARegistry_WithoutRbacGranted_WhenGetToken_ExceptionShouldBeThrown()
        {
            var acrTokenProvider = GetMockAcrTokenProvider(HttpStatusCode.Unauthorized);

            await Assert.ThrowsAsync<ContainerRegistryNotAuthorizedException>(() => acrTokenProvider.GetTokenAsync(RegistryServer, default));
        }

        [Fact]
        public async Task GivenANotFoundRegistry_WhenGetToken_ExceptionShouldBeThrown()
        {
            var acrTokenProvider = GetMockAcrTokenProvider(HttpStatusCode.NotFound);
            await Assert.ThrowsAsync<ContainerRegistryNotFoundException>(() => acrTokenProvider.GetTokenAsync(RegistryServer, default));
        }

        [Theory]
        [InlineData("{\"refresh_token\":\"refresh_token_test\"}")]
        [InlineData("{\"access_token\":\"access_token_test\"}")]
        [InlineData("{\"refresh_token\":\"\", \"access_token\":\"access_token_test\"}")]
        [InlineData("{\"refresh_token\":\"refresh_token_test\", \"access_token\":\"\"}")]
        public async Task GivenAValidRegistry_WhenRefreshTokenIsEmpty_ExceptionShouldBeThrown(string content)
        {
            var acrTokenProvider = GetMockAcrTokenProvider(HttpStatusCode.OK, content);
            await Assert.ThrowsAsync<AzureContainerRegistryTokenException>(() => acrTokenProvider.GetTokenAsync(RegistryServer, default));
        }

        [Fact]
        public async Task GivenAValidRegistry_WhenGetToken_CorrectResultShouldBeReturned()
        {
            var acrTokenProvider = GetMockAcrTokenProvider(HttpStatusCode.OK, "{\"refresh_token\":\"refresh_token_test\", \"access_token\":\"access_token_test\"}");
            string accessToken = await acrTokenProvider.GetTokenAsync(RegistryServer, default);
            Assert.Equal("Bearer access_token_test", accessToken);
        }

        private AzureContainerRegistryAccessTokenProvider GetMockAcrTokenProvider(HttpStatusCode statusCode, string content = "")
        {
            IAccessTokenProvider tokenProvider = Substitute.For<IAccessTokenProvider>();
            tokenProvider.GetAccessTokenForResourceAsync(default, default).ReturnsForAnyArgs("Bearer test");
            IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
            var httpClient = new HttpClient(new MockHttpMessageHandler(content, statusCode));
            httpClientFactory.CreateClient().ReturnsForAnyArgs(httpClient);
            return new AzureContainerRegistryAccessTokenProvider(tokenProvider, httpClientFactory, Options.Create(_convertDataConfiguration), new NullLogger<AzureContainerRegistryAccessTokenProvider>());
        }

        internal class MockHttpMessageHandler : HttpMessageHandler
        {
            private readonly string _response;
            private readonly HttpStatusCode _statusCode;

            public MockHttpMessageHandler(string response, HttpStatusCode statusCode)
            {
                _response = response;
                _statusCode = statusCode;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = _statusCode,
                    Content = new StringContent(_response),
                });
            }
        }
    }
}
