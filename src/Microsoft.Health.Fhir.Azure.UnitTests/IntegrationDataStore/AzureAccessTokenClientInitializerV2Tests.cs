// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Azure.IntegrationDataStore;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests.IntegrationDataStore
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class AzureAccessTokenClientInitializerV2Tests
    {
        private readonly NullLogger<AzureAccessTokenClientInitializerV2> _logger = new NullLogger<AzureAccessTokenClientInitializerV2>();

        [Fact]
        public void GivenNullConfiguration_WhenCreatingInitializer_ThenThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() => new AzureAccessTokenClientInitializerV2(null, _logger));
        }

        [Fact]
        public void GivenNullLogger_WhenCreatingInitializer_ThenThrowsArgumentNullException()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration());

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new AzureAccessTokenClientInitializerV2(config, null));
        }

        [Fact]
        public async Task GivenNullBlobUri_WhenGettingAuthorizedBlobClient_ThenThrowsArgumentNullException()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration
            {
                StorageAccountUri = "https://myaccount.blob.core.windows.net",
            });

            var initializer = new AzureAccessTokenClientInitializerV2(config, _logger);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => initializer.GetAuthorizedBlobClientAsync(null));
        }

        [Fact]
        public async Task GivenNullBlobUri_WhenGettingAuthorizedBlockBlobClient_ThenThrowsArgumentNullException()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration
            {
                StorageAccountUri = "https://myaccount.blob.core.windows.net",
            });

            var initializer = new AzureAccessTokenClientInitializerV2(config, _logger);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => initializer.GetAuthorizedBlockBlobClientAsync(null));
        }

        [Fact]
        public async Task GivenEmptyStorageUri_WhenGettingAuthorizedClient_ThenThrowsException()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration
            {
                StorageAccountUri = string.Empty,
            });

            var initializer = new AzureAccessTokenClientInitializerV2(config, _logger);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<IntegrationDataStoreClientInitializerException>(
                () => initializer.GetAuthorizedClientAsync());

            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [Fact]
        public async Task GivenNullStorageUri_WhenGettingAuthorizedClient_ThenThrowsException()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration
            {
                StorageAccountUri = null,
            });

            var initializer = new AzureAccessTokenClientInitializerV2(config, _logger);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<IntegrationDataStoreClientInitializerException>(
                () => initializer.GetAuthorizedClientAsync());

            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [Fact]
        public async Task GivenInvalidStorageUri_WhenGettingAuthorizedClient_ThenThrowsException()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration
            {
                StorageAccountUri = "not-a-valid-uri",
            });

            var initializer = new AzureAccessTokenClientInitializerV2(config, _logger);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<IntegrationDataStoreClientInitializerException>(
                () => initializer.GetAuthorizedClientAsync());

            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [Fact]
        public async Task GivenEmptyStorageUri_WhenGettingAuthorizedClientWithConfig_ThenThrowsException()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration
            {
                StorageAccountUri = "https://myaccount.blob.core.windows.net",
            });

            var initializer = new AzureAccessTokenClientInitializerV2(config, _logger);
            var customConfig = new IntegrationDataStoreConfiguration
            {
                StorageAccountUri = string.Empty,
            };

            // Act & Assert
            var exception = await Assert.ThrowsAsync<IntegrationDataStoreClientInitializerException>(
                () => initializer.GetAuthorizedClientAsync(customConfig));

            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [Fact]
        public async Task GivenInvalidStorageUri_WhenGettingAuthorizedClientWithConfig_ThenThrowsException()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration
            {
                StorageAccountUri = "https://myaccount.blob.core.windows.net",
            });

            var initializer = new AzureAccessTokenClientInitializerV2(config, _logger);
            var customConfig = new IntegrationDataStoreConfiguration
            {
                StorageAccountUri = "invalid-uri-format",
            };

            // Act & Assert
            var exception = await Assert.ThrowsAsync<IntegrationDataStoreClientInitializerException>(
                () => initializer.GetAuthorizedClientAsync(customConfig));

            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }
    }
}
