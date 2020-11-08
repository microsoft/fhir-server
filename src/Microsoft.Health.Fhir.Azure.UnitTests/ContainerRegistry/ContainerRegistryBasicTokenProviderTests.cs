// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Azure.ContainerRegistry;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests.ContainerRegistry
{
    public class ContainerRegistryBasicTokenProviderTests
    {
        private ContainerRegistryBasicTokenProvider _tokenProvider;
        private DataConvertConfiguration _dataConvertConfiguration = new DataConvertConfiguration();

        public ContainerRegistryBasicTokenProviderTests()
        {
            IOptions<DataConvertConfiguration> dataConvertConfiguration = Substitute.For<IOptions<DataConvertConfiguration>>();
            dataConvertConfiguration.Value.Returns(_dataConvertConfiguration);

            _tokenProvider = new ContainerRegistryBasicTokenProvider(dataConvertConfiguration);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("   ")]
        [InlineData("123")]
        public async Task GivenAnUnregisteredRegistry_WhenGetToken_NotRegisteredException_ShouldBeThrown(string registryServer)
        {
            _dataConvertConfiguration.ContainerRegistries.Add(
                new ContainerRegistryInfo
                {
                    ContainerRegistryServer = "test.azurecr.io",
                    ContainerRegistryUsername = "test",
                    ContainerRegistryPassword = "test",
                });

            await Assert.ThrowsAsync<ContainerRegistryNotRegisteredException>(() => _tokenProvider.GetTokenAsync(registryServer, default));
        }

        [Fact]
        public async Task GivenARegisteredRegistry_WithoutCredentials_WhenGetToken_NotAuthorizedException_ShouldBeThrown()
        {
            string registryServer = "test.azurecr.io";

            _dataConvertConfiguration.ContainerRegistries.Add(
                new ContainerRegistryInfo
                {
                    ContainerRegistryServer = registryServer,
                    ContainerRegistryUsername = "test",
                });

            await Assert.ThrowsAsync<ContainerRegistryNotAuthorizedException>(() => _tokenProvider.GetTokenAsync(registryServer, default));
        }

        [Fact]
        public async Task GivenARegisteredRegistry_WithCredentials_WhenGetToken_BasicToken_ShouldReturn()
        {
            string registryServer = "test.azurecr.io";

            _dataConvertConfiguration.ContainerRegistries.Add(
                new ContainerRegistryInfo
                {
                    ContainerRegistryServer = registryServer,
                    ContainerRegistryUsername = "test",
                    ContainerRegistryPassword = "123",
                });

            var token = await _tokenProvider.GetTokenAsync(registryServer, default);
            Assert.Equal("Basic dGVzdDoxMjM=", token);
        }
    }
}
