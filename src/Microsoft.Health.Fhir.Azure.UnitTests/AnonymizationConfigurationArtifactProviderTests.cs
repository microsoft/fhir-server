// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.ContainerRegistry;
using Microsoft.Azure.ContainerRegistry.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Azure.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.TemplateManagement.Utilities;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Microsoft.Rest;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests
{
    [Trait("Traits.OwningTeam", OwningTeam.FhirImport)]
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
            var registry = GetTestContainerRegistryInfo();

            // Use basic token here, which can work when there is no Managed Identity for container registry token.
            AcrBasicToken acrTokenProvider = new AcrBasicToken(registry);
            var exportJobConfiguration = new ExportJobConfiguration();
            IOptions<ExportJobConfiguration> optionsExportConfig = Substitute.For<IOptions<ExportJobConfiguration>>();
            optionsExportConfig.Value.Returns(exportJobConfiguration);
            var logger = Substitute.For<ILogger<AzureConnectionStringClientInitializer>>();
            var azureAccessTokenClientInitializer = new AzureConnectionStringClientInitializer(optionsExportConfig, logger);
            _provider = new AnonymizationConfigurationArtifactProvider(azureAccessTokenClientInitializer, acrTokenProvider, optionsExportConfig, new NullLogger<AnonymizationConfigurationArtifactProvider>());
        }

        [SkippableFact]
        public async Task GivenAValidConfigName_WithValidAcrReference_WhenFetchAnonymizedConfig_TheConfigContentInAcrShouldBeRerturn()
        {
            var registry = GetTestContainerRegistryInfo();

            // Skip when there is no registry configuration
            Skip.If(registry == null);

            await PushConfigurationAsync(registry, TestRepositoryName, TestRepositoryTag, AnonymizationConfiguration);
            var jobRecord = new ExportJobRecord(
                new Uri("https://localhost/$export"),
                ExportJobType.All,
                "Dummy",
                resourceType: null,
                filters: null,
                "hash",
                rollingFileSizeInMB: 1,
                anonymizationConfigurationCollectionReference: $"{registry.Server}/{TestRepositoryName}:{TestRepositoryTag}",
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

        [SkippableFact]
        public async Task GivenAValidAcrReference_WithInvalidConfigName_WhenFetchAnonymizedConfig_ExceptionShouldBeThrown()
        {
            var registry = GetTestContainerRegistryInfo();

            // Skip when there is no registry configuration
            Skip.If(registry == null);

            await PushConfigurationAsync(registry, TestRepositoryName, TestRepositoryTag, AnonymizationConfiguration);
            var jobRecord = new ExportJobRecord(
                new Uri("https://localhost/$export"),
                ExportJobType.All,
                "Dummy",
                resourceType: null,
                filters: null,
                "hash",
                rollingFileSizeInMB: 1,
                anonymizationConfigurationCollectionReference: $"{registry.Server}/{TestRepositoryName}:{TestRepositoryTag}",
                anonymizationConfigurationLocation: "InvalidConfigName");
            using (Stream stream = new MemoryStream())
            {
                await Assert.ThrowsAsync<FileNotFoundException>(() => _provider.FetchAsync(jobRecord, stream, CancellationToken.None));
            }
        }

        private async Task PushConfigurationAsync(ContainerRegistryInfo registry, string repository, string tag, string configContent)
        {
            AzureContainerRegistryClient acrClient = new AzureContainerRegistryClient(registry.Server, new AcrBasicToken(registry));

            int schemaV2 = 2;
            string mediatypeV2Manifest = "application/vnd.docker.distribution.manifest.v2+json";
            string mediatypeV1Manifest = "application/vnd.oci.image.config.v1+json";
            string emptyConfigStr = "{}";

            // Upload config blob
            byte[] originalConfigBytes = Encoding.UTF8.GetBytes(emptyConfigStr);
            using var originalConfigStream = new MemoryStream(originalConfigBytes);
            string originalConfigDigest = ComputeDigest(originalConfigStream);
            await UploadBlob(acrClient, originalConfigStream, repository, originalConfigDigest);

            // Upload memory blob
            byte[] configContentBytes = Encoding.UTF8.GetBytes(configContent);

            configContentBytes = StreamUtility.CompressToTarGz(new Dictionary<string, byte[]>() { { TestConfigName, configContentBytes } }, false);
            using Stream byteStream = new MemoryStream(configContentBytes);
            var blobLength = byteStream.Length;
            string blobDigest = ComputeDigest(byteStream);
            await UploadBlob(acrClient, byteStream, repository, blobDigest);

            // Push manifest
            List<Descriptor> layers = new List<Descriptor>
            {
                new Descriptor("application/vnd.oci.image.layer.v1.tar", blobLength, blobDigest),
            };
            var v2Manifest = new V2Manifest(schemaV2, mediatypeV2Manifest, new Descriptor(mediatypeV1Manifest, originalConfigBytes.Length, originalConfigDigest), layers);
            await acrClient.Manifests.CreateAsync(repository, tag, v2Manifest);
            acrClient.Dispose();
        }

        private static string ComputeDigest(Stream s)
        {
            s.Position = 0;
            StringBuilder sb = new StringBuilder();

            using (var hash = SHA256.Create())
            {
                byte[] result = hash.ComputeHash(s);
                foreach (byte b in result)
                {
                    sb.Append(b.ToString("x2"));
                }
            }

            return "sha256:" + sb.ToString();
        }

        private async Task UploadBlob(AzureContainerRegistryClient acrClient, Stream stream, string repository, string digest)
        {
            stream.Position = 0;
            var uploadInfo = await acrClient.Blob.StartUploadAsync(repository);
            var uploadedLayer = await acrClient.Blob.UploadAsync(stream, uploadInfo.Location);
            await acrClient.Blob.EndUploadAsync(digest, uploadedLayer.Location);
        }

        private ContainerRegistryInfo GetTestContainerRegistryInfo()
        {
            var containerRegistry = new ContainerRegistryInfo
            {
                Server = Environment.GetEnvironmentVariable("TestContainerRegistryServer"),
                Username = Environment.GetEnvironmentVariable("TestContainerRegistryServer")?.Split('.')[0],
                Password = Environment.GetEnvironmentVariable("TestContainerRegistryPassword"),
            };

            if (string.IsNullOrEmpty(containerRegistry.Server) || string.IsNullOrEmpty(containerRegistry.Password))
            {
                return null;
            }

            return containerRegistry;
        }

        internal class AcrBasicToken : ServiceClientCredentials, IContainerRegistryTokenProvider
        {
            private ContainerRegistryInfo _registry;

            public AcrBasicToken(ContainerRegistryInfo registry)
            {
                _registry = registry;
            }

            public Task<string> GetTokenAsync(string registryServer, CancellationToken cancellationToken)
            {
                return Task.FromResult("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_registry.Username}:{_registry.Password}")));
            }

            public override void InitializeServiceClient<T>(ServiceClient<T> client)
            {
                base.InitializeServiceClient(client);
            }

            public override Task ProcessHttpRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var basicToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_registry.Username}:{_registry.Password}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
                return base.ProcessHttpRequestAsync(request, cancellationToken);
            }
        }

        internal class ContainerRegistryInfo
        {
            public string Server { get; set; }

            public string Username { get; set; }

            public string Password { get; set; }
        }
    }
}
