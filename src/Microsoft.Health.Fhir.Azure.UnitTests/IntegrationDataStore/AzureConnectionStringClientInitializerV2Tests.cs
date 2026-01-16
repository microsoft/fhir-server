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
    public class AzureConnectionStringClientInitializerV2Tests
    {
        private readonly NullLogger<AzureConnectionStringClientInitializerV2> _logger = new NullLogger<AzureConnectionStringClientInitializerV2>();

        [Fact]
        public void GivenNullConfiguration_WhenCreatingInitializer_ThenThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() => new AzureConnectionStringClientInitializerV2(null, _logger));
        }

        [Fact]
        public void GivenNullLogger_WhenCreatingInitializer_ThenThrowsArgumentNullException()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration());

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new AzureConnectionStringClientInitializerV2(config, null));
        }

        [Fact]
        public async Task GivenNullBlobUri_WhenGettingAuthorizedBlobClient_ThenThrowsArgumentNullException()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration
            {
                StorageAccountConnection = "UseDevelopmentStorage=true",
            });

            var initializer = new AzureConnectionStringClientInitializerV2(config, _logger);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => initializer.GetAuthorizedBlobClientAsync(null));
        }

        [Fact]
        public async Task GivenNullBlobUri_WhenGettingAuthorizedBlockBlobClient_ThenThrowsArgumentNullException()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration
            {
                StorageAccountConnection = "UseDevelopmentStorage=true",
            });

            var initializer = new AzureConnectionStringClientInitializerV2(config, _logger);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => initializer.GetAuthorizedBlockBlobClientAsync(null));
        }

        [Fact]
        public async Task GivenEmptyConnectionString_WhenGettingAuthorizedClient_ThenThrowsException()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration
            {
                StorageAccountConnection = string.Empty,
            });

            var initializer = new AzureConnectionStringClientInitializerV2(config, _logger);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<IntegrationDataStoreClientInitializerException>(
                () => initializer.GetAuthorizedClientAsync());

            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [Fact]
        public async Task GivenNullConnectionString_WhenGettingAuthorizedClient_ThenThrowsException()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration
            {
                StorageAccountConnection = null,
            });

            var initializer = new AzureConnectionStringClientInitializerV2(config, _logger);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<IntegrationDataStoreClientInitializerException>(
                () => initializer.GetAuthorizedClientAsync());

            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }
    }
}
