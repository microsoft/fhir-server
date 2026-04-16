// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Azure.ContainerRegistry;
using Microsoft.Health.Fhir.Azure.ExportDestinationClient;
using Microsoft.Health.Fhir.Azure.IntegrationDataStore;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class FhirServerBuilderAzureRegistrationExtensionsTests
    {
        private readonly IServiceCollection _services;
        private readonly IFhirServerBuilder _fhirServerBuilder;

        public FhirServerBuilderAzureRegistrationExtensionsTests()
        {
            _services = new ServiceCollection();
            _fhirServerBuilder = new TestFhirServerBuilder(_services);
        }

        [Fact]
        public void GivenNullBuilder_WhenAddingAzureExportDestinationClient_ThenThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() => FhirServerBuilderAzureRegistrationExtensions.AddAzureExportDestinationClient(null));
        }

        [Fact]
        public void GivenValidBuilder_WhenAddingAzureExportDestinationClient_ThenServicesAreRegistered()
        {
            // Arrange & Act
            var result = _fhirServerBuilder.AddAzureExportDestinationClient();

            // Assert
            Assert.NotNull(result);
            Assert.Same(_fhirServerBuilder, result);
            Assert.Contains(_services, s => s.ServiceType == typeof(IExportDestinationClient) && s.ImplementationType == typeof(AzureExportDestinationClient));
            Assert.Contains(_services, s => s.ImplementationType == typeof(AnonymizationConfigurationArtifactProvider));
        }

        [Fact]
        public void GivenNullBuilder_WhenAddingAzureExportClientInitializer_ThenThrowsArgumentNullException()
        {
            // Arrange
            var configuration = new ConfigurationBuilder().Build();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => FhirServerBuilderAzureRegistrationExtensions.AddAzureExportClientInitializer(null, configuration));
        }

        [Fact]
        public void GivenNullConfiguration_WhenAddingAzureExportClientInitializer_ThenThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() => _fhirServerBuilder.AddAzureExportClientInitializer(null));
        }

        [Fact]
        public void GivenConfigurationWithStorageAccountUri_WhenAddingAzureExportClientInitializer_ThenAccessTokenInitializerIsRegistered()
        {
            // Arrange
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, string>("FhirServer:Operations:Export:StorageAccountUri", "https://test.blob.core.windows.net"),
                })
                .Build();

            // Act
            var result = _fhirServerBuilder.AddAzureExportClientInitializer(configuration);

            // Assert
            Assert.NotNull(result);
            Assert.Same(_fhirServerBuilder, result);
            Assert.Contains(_services, s => s.ServiceType == typeof(IExportClientInitializer<BlobServiceClient>) && s.ImplementationType == typeof(AzureAccessTokenClientInitializer));
        }

        [Fact]
        public void GivenConfigurationWithoutStorageAccountUri_WhenAddingAzureExportClientInitializer_ThenConnectionStringInitializerIsRegistered()
        {
            // Arrange
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, string>("FhirServer:Operations:Export:StorageAccountConnection", "UseDevelopmentStorage=true"),
                })
                .Build();

            // Act
            var result = _fhirServerBuilder.AddAzureExportClientInitializer(configuration);

            // Assert
            Assert.NotNull(result);
            Assert.Same(_fhirServerBuilder, result);
            Assert.Contains(_services, s => s.ServiceType == typeof(IExportClientInitializer<BlobServiceClient>) && s.ImplementationType == typeof(AzureConnectionStringClientInitializer));
        }

        [Fact]
        public void GivenNullBuilder_WhenAddingContainerRegistryTokenProvider_ThenThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() => FhirServerBuilderAzureRegistrationExtensions.AddContainerRegistryTokenProvider(null));
        }

        [Fact]
        public void GivenValidBuilder_WhenAddingContainerRegistryTokenProvider_ThenServicesAreRegistered()
        {
            // Arrange & Act
            var result = _fhirServerBuilder.AddContainerRegistryTokenProvider();

            // Assert
            Assert.NotNull(result);
            Assert.Same(_fhirServerBuilder, result);
            Assert.Contains(_services, s => s.ServiceType == typeof(IAccessTokenProvider));
            Assert.Contains(_services, s => s.ServiceType == typeof(IContainerRegistryTokenProvider));
        }

        [Fact]
        public void GivenNullBuilder_WhenAddingContainerRegistryAccessValidator_ThenThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() => FhirServerBuilderAzureRegistrationExtensions.AddContainerRegistryAccessValidator(null));
        }

        [Fact]
        public void GivenValidBuilder_WhenAddingContainerRegistryAccessValidator_ThenServiceIsRegistered()
        {
            // Arrange & Act
            var result = _fhirServerBuilder.AddContainerRegistryAccessValidator();

            // Assert
            Assert.NotNull(result);
            Assert.Same(_fhirServerBuilder, result);
            Assert.Contains(_services, s => s.ServiceType == typeof(IContainerRegistryAccessValidator));
        }

        [Fact]
        public void GivenNullBuilder_WhenAddingAzureIntegrationDataStoreClient_ThenThrowsArgumentNullException()
        {
            // Arrange
            var configuration = new ConfigurationBuilder().Build();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => FhirServerBuilderAzureRegistrationExtensions.AddAzureIntegrationDataStoreClient(null, configuration));
        }

        [Fact]
        public void GivenConfigurationWithStorageAccountUri_WhenAddingAzureIntegrationDataStoreClient_ThenAccessTokenInitializerIsRegistered()
        {
            // Arrange
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, string>("FhirServer:Operations:IntegrationDataStore:StorageAccountUri", "https://test.blob.core.windows.net"),
                })
                .Build();

            // Act
            var result = _fhirServerBuilder.AddAzureIntegrationDataStoreClient(configuration);

            // Assert
            Assert.NotNull(result);
            Assert.Same(_fhirServerBuilder, result);
            Assert.Contains(_services, s => s.ServiceType == typeof(IIntegrationDataStoreClientInitializer) && s.ImplementationType == typeof(AzureAccessTokenClientInitializerV2));
            Assert.Contains(_services, s => s.ServiceType == typeof(IAccessTokenProvider));
            Assert.Contains(_services, s => s.ImplementationType == typeof(AzureBlobIntegrationDataStoreClient));
        }

        [Fact]
        public void GivenConfigurationWithoutStorageAccountUri_WhenAddingAzureIntegrationDataStoreClient_ThenConnectionStringInitializerIsRegistered()
        {
            // Arrange
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, string>("FhirServer:Operations:IntegrationDataStore:StorageAccountConnection", "UseDevelopmentStorage=true"),
                })
                .Build();

            // Act
            var result = _fhirServerBuilder.AddAzureIntegrationDataStoreClient(configuration);

            // Assert
            Assert.NotNull(result);
            Assert.Same(_fhirServerBuilder, result);
            Assert.Contains(_services, s => s.ServiceType == typeof(IIntegrationDataStoreClientInitializer) && s.ImplementationType == typeof(AzureConnectionStringClientInitializerV2));
            Assert.Contains(_services, s => s.ImplementationType == typeof(AzureBlobIntegrationDataStoreClient));
        }

        [Fact]
        public void GivenEmptyConfiguration_WhenAddingAzureIntegrationDataStoreClient_ThenDefaultsToConnectionStringInitializer()
        {
            // Arrange
            var configuration = new ConfigurationBuilder().Build();

            // Act
            var result = _fhirServerBuilder.AddAzureIntegrationDataStoreClient(configuration);

            // Assert
            Assert.NotNull(result);
            Assert.Same(_fhirServerBuilder, result);
            Assert.Contains(_services, s => s.ServiceType == typeof(IIntegrationDataStoreClientInitializer) && s.ImplementationType == typeof(AzureConnectionStringClientInitializerV2));
        }

        private class TestFhirServerBuilder : IFhirServerBuilder
        {
            public TestFhirServerBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }
    }
}
