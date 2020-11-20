// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Azure.ContainerRegistry;
using Microsoft.Health.Fhir.Azure.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests.ContainerRegistry
{
    public class AzureContainerRegistryAccessTokenProviderTests
    {
        private AzureContainerRegistryAccessTokenProvider _tokenProvider;
        private DataConvertConfiguration _dataConvertConfiguration = new DataConvertConfiguration();

        public AzureContainerRegistryAccessTokenProviderTests()
        {
            IAccessTokenProvider tokenProvider = new AzureAccessTokenProvider(new NullLogger<AzureAccessTokenProvider>());
            IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
            var httpClient = new HttpClient();
            httpClientFactory.CreateClient().ReturnsForAnyArgs(httpClient);
            _tokenProvider = new AzureContainerRegistryAccessTokenProvider(tokenProvider, httpClientFactory, Options.Create(_dataConvertConfiguration), new NullLogger<AzureContainerRegistryAccessTokenProvider>());
        }

        [Fact]
        public async Task GivenARegistry_WithoutCredentials_WhenGetToken_TokenException_ShouldBeThrown()
        {
            string registryServer = "test.azurecr.io";
            _dataConvertConfiguration.ContainerRegistryServers.Add(registryServer);

            await Assert.ThrowsAsync<AzureContainerRegistryTokenException>(() => _tokenProvider.GetTokenAsync(registryServer, default));
        }
    }
}
