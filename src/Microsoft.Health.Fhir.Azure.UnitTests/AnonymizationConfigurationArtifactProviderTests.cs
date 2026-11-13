// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Containers.ContainerRegistry;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Azure.ContainerRegistry;
using Microsoft.Health.Fhir.Azure.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.TemplateManagement.Exceptions;
using Microsoft.Health.Fhir.TemplateManagement.Utilities;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.AnonymizedExport)]
    public class AnonymizationConfigurationArtifactProviderTests
    {
        private AnonymizationConfigurationArtifactProvider _provider;

        private const string TestRepositoryName = "testanonymizationconfigs";
        private const string TestConfigName = "testconfigname.json";
        private const string TestRepositoryTag = "unittest";

        private const string AnonymizationConfiguration = @"
{
    ""fhirPathRules"": [
        {""path"": ""Resource.nodesByName('id')"", ""method"": ""redact""},
        {""path"": ""nodesByType('Human').name"", ""method"": ""redact""}
    ]
}";

        public AnonymizationConfigurationArtifactProviderTests()
        {
            var exportJobConfiguration = new ExportJobConfiguration();
            IOptions<ExportJobConfiguration> optionsExportConfig = Substitute.For<IOptions<ExportJobConfiguration>>();
            optionsExportConfig.Value.Returns(exportJobConfiguration);
            var logger = Substitute.For<ILogger<AzureConnectionStringClientInitializer>>();
            var azureAccessTokenClientInitializer = new AzureConnectionStringClientInitializer(optionsExportConfig, logger);

            // Use federated managed identity (Azure Pipelines workload identity) to access ACR when configured;
            // otherwise fall back to a mock for the argument-validation unit tests that never reach ACR.
            IContainerRegistryTokenProvider acrTokenProvider = CreateAcrTokenProvider() ?? Substitute.For<IContainerRegistryTokenProvider>();
            _provider = new AnonymizationConfigurationArtifactProvider(azureAccessTokenClientInitializer, acrTokenProvider, optionsExportConfig, new NullLogger<AnonymizationConfigurationArtifactProvider>());
        }

        [Fact]
        public void GivenNullExportClientInitializer_WhenCreatingProvider_ThenThrowsArgumentNullException()
        {
            // Arrange
            var tokenProvider = Substitute.For<IContainerRegistryTokenProvider>();
            var config = Options.Create(new ExportJobConfiguration());
            var logger = new NullLogger<AnonymizationConfigurationArtifactProvider>();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new AnonymizationConfigurationArtifactProvider(null, tokenProvider, config, logger));
        }

        [Fact]
        public void GivenNullTokenProvider_WhenCreatingProvider_ThenThrowsArgumentNullException()
        {
            // Arrange
            var clientInitializer = Substitute.For<IExportClientInitializer<BlobServiceClient>>();
            var config = Options.Create(new ExportJobConfiguration());
            var logger = new NullLogger<AnonymizationConfigurationArtifactProvider>();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new AnonymizationConfigurationArtifactProvider(clientInitializer, null, config, logger));
        }

        [Fact]
        public void GivenNullConfiguration_WhenCreatingProvider_ThenThrowsArgumentNullException()
        {
            // Arrange
            var clientInitializer = Substitute.For<IExportClientInitializer<BlobServiceClient>>();
            var tokenProvider = Substitute.For<IContainerRegistryTokenProvider>();
            var logger = new NullLogger<AnonymizationConfigurationArtifactProvider>();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new AnonymizationConfigurationArtifactProvider(clientInitializer, tokenProvider, null, logger));
        }

        [Fact]
        public void GivenValidParameters_WhenCreatingProvider_ThenProviderIsCreated()
        {
            // Arrange
            var clientInitializer = Substitute.For<IExportClientInitializer<BlobServiceClient>>();
            var tokenProvider = Substitute.For<IContainerRegistryTokenProvider>();
            var config = Options.Create(new ExportJobConfiguration());
            var logger = new NullLogger<AnonymizationConfigurationArtifactProvider>();

            // Act
            using var provider = new AnonymizationConfigurationArtifactProvider(clientInitializer, tokenProvider, config, logger);

            // Assert
            Assert.NotNull(provider);
        }

        [Fact]
        public async Task GivenEmptyConfigurationLocation_WhenFetchingAsync_ThenThrowsArgumentException()
        {
            // Arrange
            var jobRecord = new ExportJobRecord(
                new Uri("https://localhost/$export"),
                ExportJobType.All,
                "Dummy",
                resourceType: null,
                filters: null,
                "hash",
                rollingFileSizeInMB: 1,
                anonymizationConfigurationLocation: string.Empty);
            using var stream = new MemoryStream();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => _provider.FetchAsync(jobRecord, stream, CancellationToken.None));
        }

        [Fact]
        public async Task GivenNullJobRecord_WhenFetchingAsync_ThenThrowsNullReferenceException()
        {
            // Arrange
            using var stream = new MemoryStream();

            // Act & Assert
            await Assert.ThrowsAsync<NullReferenceException>(() => _provider.FetchAsync(null, stream, CancellationToken.None));
        }

        [Fact]
        public async Task GivenNullTargetStream_WhenFetchingAsync_ThenThrowsArgumentNullException()
        {
            // Arrange
            var jobRecord = new ExportJobRecord(
                new Uri("https://localhost/$export"),
                ExportJobType.All,
                "Dummy",
                resourceType: null,
                filters: null,
                "hash",
                rollingFileSizeInMB: 1,
                anonymizationConfigurationLocation: "config.json");

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => _provider.FetchAsync(jobRecord, null, CancellationToken.None));
        }

        [Fact]
        public void GivenProviderCreated_WhenDisposed_ThenCanBeDisposedMultipleTimes()
        {
            // Arrange
            var clientInitializer = Substitute.For<IExportClientInitializer<BlobServiceClient>>();
            var tokenProvider = Substitute.For<IContainerRegistryTokenProvider>();
            var config = Options.Create(new ExportJobConfiguration());
            var logger = new NullLogger<AnonymizationConfigurationArtifactProvider>();
            using var provider = new AnonymizationConfigurationArtifactProvider(clientInitializer, tokenProvider, config, logger);

            // Act & Assert - Should not throw when disposed multiple times
            provider.Dispose();
            provider.Dispose(); // Dispose again
        }

        [Fact]
        public async Task GivenJobRecordWithoutAcrReference_WhenFetchingAsync_ThenUsesBlobStoragePath()
        {
            // Arrange
            var clientInitializer = Substitute.For<IExportClientInitializer<BlobServiceClient>>();
            var tokenProvider = Substitute.For<IContainerRegistryTokenProvider>();
            var config = Options.Create(new ExportJobConfiguration());
            var logger = new NullLogger<AnonymizationConfigurationArtifactProvider>();

            var jobRecord = new ExportJobRecord(
                new Uri("https://localhost/$export"),
                ExportJobType.All,
                "Dummy",
                resourceType: null,
                filters: null,
                "hash",
                rollingFileSizeInMB: 1,
                anonymizationConfigurationCollectionReference: null, // No ACR reference
                anonymizationConfigurationLocation: "config.json");

            using var provider = new AnonymizationConfigurationArtifactProvider(clientInitializer, tokenProvider, config, logger);
            using var stream = new MemoryStream();

            // Act & Assert - Will attempt to use blob storage path and fail since we don't have real blob infrastructure
            // This test verifies the code path selection logic
            await Assert.ThrowsAnyAsync<Exception>(() => provider.FetchAsync(jobRecord, stream, CancellationToken.None));

            // Verify that GetAuthorizedClient was called (blob storage path)
            clientInitializer.Received(1).GetAuthorizedClient(Arg.Any<ExportJobConfiguration>());
        }

        [Fact]
        public async Task GivenJobRecordWithAcrReference_WhenFetchingAsync_ThenUsesAcrPath()
        {
            // Arrange
            var clientInitializer = Substitute.For<IExportClientInitializer<BlobServiceClient>>();
            var tokenProvider = Substitute.For<IContainerRegistryTokenProvider>();
            tokenProvider.GetTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns("Bearer test-token");

            var config = Options.Create(new ExportJobConfiguration());
            var logger = new NullLogger<AnonymizationConfigurationArtifactProvider>();

            var jobRecord = new ExportJobRecord(
                new Uri("https://localhost/$export"),
                ExportJobType.All,
                "Dummy",
                resourceType: null,
                filters: null,
                "hash",
                rollingFileSizeInMB: 1,
                anonymizationConfigurationCollectionReference: "test.azurecr.io/repo:tag", // ACR reference present
                anonymizationConfigurationLocation: "config.json");

            using var provider = new AnonymizationConfigurationArtifactProvider(clientInitializer, tokenProvider, config, logger);
            using var stream = new MemoryStream();

            // Act & Assert - Will attempt to use ACR path and fail since we don't have real ACR
            // This test verifies the code path selection logic
            await Assert.ThrowsAnyAsync<Exception>(() => provider.FetchAsync(jobRecord, stream, CancellationToken.None));

            // Verify that token provider was called (ACR path)
            await tokenProvider.Received(1).GetTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAValidConfigName_WithValidAcrReference_WhenFetchAnonymizedConfig_TheConfigContentInAcrShouldBeRerturn()
        {
            Assert.SkipWhen(!IsAcrConfigured(), Microsoft.Health.Fhir.Tests.Common.SkipReasons.Unspecified);

            await PushConfigurationAsync(TestRepositoryName, TestRepositoryTag, AnonymizationConfiguration);
            var jobRecord = new ExportJobRecord(
                new Uri("https://localhost/$export"),
                ExportJobType.All,
                "Dummy",
                resourceType: null,
                filters: null,
                "hash",
                rollingFileSizeInMB: 1,
                anonymizationConfigurationCollectionReference: $"{GetRegistryServer()}/{TestRepositoryName}:{TestRepositoryTag}",
                anonymizationConfigurationLocation: TestConfigName);
            using (Stream stream = new MemoryStream())
            {
                await _provider.FetchAsync(jobRecord, stream, CancellationToken.None);
                stream.Position = 0;
                using (StreamReader reader = new StreamReader(stream))
                {
                    string configurationContent = await reader.ReadToEndAsync();
                    Assert.Contains("Resource.nodesByName('id')", configurationContent);
                }
            }
        }

        [Fact]
        public async Task GivenAValidAcrReference_WithInvalidConfigName_WhenFetchAnonymizedConfig_ExceptionShouldBeThrown()
        {
            Assert.SkipWhen(!IsAcrConfigured(), Microsoft.Health.Fhir.Tests.Common.SkipReasons.Unspecified);

            await PushConfigurationAsync(TestRepositoryName, TestRepositoryTag, AnonymizationConfiguration);
            var jobRecord = new ExportJobRecord(
                new Uri("https://localhost/$export"),
                ExportJobType.All,
                "Dummy",
                resourceType: null,
                filters: null,
                "hash",
                rollingFileSizeInMB: 1,
                anonymizationConfigurationCollectionReference: $"{GetRegistryServer()}/{TestRepositoryName}:{TestRepositoryTag}",
                anonymizationConfigurationLocation: "InvalidConfigName");
            using (Stream stream = new MemoryStream())
            {
                await Assert.ThrowsAsync<FileNotFoundException>(() => _provider.FetchAsync(jobRecord, stream, CancellationToken.None));
            }
        }

        private static async Task PushConfigurationAsync(string repository, string tag, string configContent)
        {
            var client = new ContainerRegistryContentClient(new Uri($"https://{GetRegistryServer()}"), repository, CreateAzurePipelinesCredential());

            using var configStream = new MemoryStream("{}"u8.ToArray());
            var configResult = await client.UploadBlobAsync(configStream);

            byte[] configContentBytes = Encoding.UTF8.GetBytes(configContent);
            byte[] tarGzBytes = StreamUtility.CompressToTarGz(new Dictionary<string, byte[]>() { { TestConfigName, configContentBytes } }, false);
            using var layerStream = new MemoryStream(tarGzBytes);
            var layerResult = await client.UploadBlobAsync(layerStream);

            var manifest = new
            {
                schemaVersion = 2,
                config = new
                {
                    mediaType = "application/vnd.oci.image.config.v1+json",
                    digest = configResult.Value.Digest,
                    size = configResult.Value.SizeInBytes,
                },
                layers = new[]
                {
                    new
                    {
                        mediaType = "application/vnd.oci.image.layer.v1.tar",
                        digest = layerResult.Value.Digest,
                        size = layerResult.Value.SizeInBytes,
                    },
                },
            };

            BinaryData manifestData = BinaryData.FromString(JsonSerializer.Serialize(manifest));
            await client.SetManifestAsync(manifestData, tag, ManifestMediaType.OciImageManifest);
        }

        private static string GetRegistryServer()
        {
            return EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.TestContainerRegistryServer);
        }

        private static bool IsAcrConfigured()
        {
            return !string.IsNullOrEmpty(GetRegistryServer()) && HasFederatedIdentityEnvironment();
        }

        private static bool HasFederatedIdentityEnvironment()
        {
            return !string.IsNullOrEmpty(EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.AzureSubscriptionTenantId))
                && !string.IsNullOrEmpty(EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.AzureSubscriptionClientId))
                && !string.IsNullOrEmpty(EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.AzureSubscriptionServiceConnectionId))
                && !string.IsNullOrEmpty(EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SystemAccessToken));
        }

        private static AzurePipelinesCredential CreateAzurePipelinesCredential()
        {
            string tenantId = EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.AzureSubscriptionTenantId);
            string clientId = EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.AzureSubscriptionClientId);
            string serviceConnectionId = EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.AzureSubscriptionServiceConnectionId);
            string systemAccessToken = EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SystemAccessToken);

            return new AzurePipelinesCredential(tenantId, clientId, serviceConnectionId, systemAccessToken);
        }

        private static IContainerRegistryTokenProvider CreateAcrTokenProvider()
        {
            if (!IsAcrConfigured())
            {
                return null;
            }

            var aadTokenProvider = new AzurePipelinesAccessTokenProvider(CreateAzurePipelinesCredential());
            return new AzureContainerRegistryAccessTokenProvider(
                aadTokenProvider,
                new SingleHttpClientFactory(),
                Options.Create(new ConvertDataConfiguration()),
                NullLogger<AzureContainerRegistryAccessTokenProvider>.Instance);
        }

        private sealed class AzurePipelinesAccessTokenProvider : IAccessTokenProvider
        {
            private readonly AzurePipelinesCredential _credential;

            public AzurePipelinesAccessTokenProvider(AzurePipelinesCredential credential)
            {
                _credential = credential;
            }

            public TokenCredential TokenCredential => _credential;

            public async Task<string> GetAccessTokenForResourceAsync(Uri resourceUri, CancellationToken cancellationToken)
            {
                string scope = new Uri(resourceUri, "/.default").ToString();
                AccessToken token = await _credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), cancellationToken);
                return token.Token;
            }
        }

        private sealed class SingleHttpClientFactory : IHttpClientFactory
        {
            private static readonly HttpClient SharedClient = new HttpClient();

            public HttpClient CreateClient(string name) => SharedClient;
        }
    }
}
