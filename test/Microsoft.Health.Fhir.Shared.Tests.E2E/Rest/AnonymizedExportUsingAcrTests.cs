// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Azure.ContainerRegistry;
using Microsoft.Azure.ContainerRegistry.Models;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Auth;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.TemplateManagement.Utilities;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Metric;
using Microsoft.Health.Test.Utilities;
using Microsoft.Rest;
using Newtonsoft.Json;
using Xunit;
using FhirGroup = Hl7.Fhir.Model.Group;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.AnonymizedExport)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class AnonymizedExportUsingAcrTests : IClassFixture<ExportTestFixture>
    {
        private const string TestRepositoryName = "testanonymizationconfigs";
        private const string TestConfigName = "testconfigname.json";
        private const string TestRepositoryTag = "e2etest";

        private const string LocalIntegrationStoreConnectionString = "UseDevelopmentStorage=true";

        private bool _isUsingInProcTestServer = false;
        private readonly TestFhirClient _testFhirClient;
        private readonly MetricHandler _metricHandler;
        private const string RedactResourceIdAnonymizationConfiguration = @"
{
    ""fhirPathRules"": [
        {""path"": ""Resource.nodesByName('id')"", ""method"": ""redact""},
        {""path"": ""nodesByType('Human').name"", ""method"": ""redact""}
    ]
}";

        public AnonymizedExportUsingAcrTests(ExportTestFixture fixture)
        {
            _isUsingInProcTestServer = fixture.IsUsingInProcTestServer;
            _testFhirClient = fixture.TestFhirClient;
            _metricHandler = fixture.MetricHandler;
        }

        [SkippableTheory]
        [InlineData("")]
        [InlineData("Patient/")]
        public async Task GivenAValidConfigurationWithAcrReference_WhenExportingAnonymizedData_ResourceShouldBeAnonymized(string path)
        {
            var registry = GetTestContainerRegistryInfo();

            // Here we skip local E2E test since we need Managed Identity for container registry token.
            // We also skip the case when environmental variable is not provided (not able to upload configurations)
            Skip.If(_isUsingInProcTestServer || registry == null);
            await PushConfigurationAsync(registry, TestRepositoryName, TestRepositoryTag, RedactResourceIdAnonymizationConfiguration);

            _metricHandler?.ResetCount();
            var dateTime = DateTimeOffset.UtcNow;
            var resourceToCreate = Samples.GetDefaultPatient().ToPoco<Patient>();
            resourceToCreate.Id = Guid.NewGuid().ToString();
            await _testFhirClient.UpdateAsync(resourceToCreate);

            string containerName = Guid.NewGuid().ToString("N");
            string reference = $"{registry.Server}/{TestRepositoryName}:{TestRepositoryTag}";
            Uri contentLocation = await _testFhirClient.AnonymizedExportUsingAcrAsync(TestConfigName, reference, dateTime, containerName, path);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            IList<Uri> blobUris = await CheckExportStatus(response);

            IEnumerable<string> dataFromExport = await DownloadBlobAndParse(blobUris);
            FhirJsonParser parser = new FhirJsonParser();

            foreach (string content in dataFromExport)
            {
                Resource result = parser.Parse<Resource>(content);

                Assert.Contains(result.Meta.Security, c => "REDACTED".Equals(c.Code));
            }
        }

        [SkippableFact]
        public async Task GivenAValidConfigurationWithAcrReference_WhenExportingGroupAnonymizedData_ResourceShouldBeAnonymized()
        {
            var registry = GetTestContainerRegistryInfo();

            // Here we skip local E2E test since we need Managed Identity for container registry token.
            // We also skip the case when environmental variable is not provided (not able to upload configurations)
            Skip.If(_isUsingInProcTestServer || registry == null);
            await PushConfigurationAsync(registry, TestRepositoryName, TestRepositoryTag, RedactResourceIdAnonymizationConfiguration);

            _metricHandler?.ResetCount();
            var patientToCreate = Samples.GetDefaultPatient().ToPoco<Patient>();
            var dateTime = DateTimeOffset.UtcNow;
            patientToCreate.Id = Guid.NewGuid().ToString();
            var patientReponse = await _testFhirClient.UpdateAsync(patientToCreate);
            var patientId = patientReponse.Resource.Id;

            var group = new FhirGroup()
            {
                Type = FhirGroup.GroupType.Person,
                Actual = true,
                Id = Guid.NewGuid().ToString(),
                Member = new List<FhirGroup.MemberComponent>()
                {
                    new FhirGroup.MemberComponent()
                    {
                        Entity = new ResourceReference($"{KnownResourceTypes.Patient}/{patientId}"),
                    },
                },
            };
            var groupReponse = await _testFhirClient.UpdateAsync(group);
            var groupId = groupReponse.Resource.Id;

            string containerName = Guid.NewGuid().ToString("N");
            string reference = $"{registry.Server}/{TestRepositoryName}:{TestRepositoryTag}";
            Uri contentLocation = await _testFhirClient.AnonymizedExportUsingAcrAsync(TestConfigName, reference, dateTime, containerName, $"Group/{groupId}/");
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            IList<Uri> blobUris = await CheckExportStatus(response);

            IEnumerable<string> dataFromExport = await DownloadBlobAndParse(blobUris);
            FhirJsonParser parser = new FhirJsonParser();

            foreach (string content in dataFromExport)
            {
                Resource result = parser.Parse<Resource>(content);

                Assert.Contains(result.Meta.Security, c => "REDACTED".Equals(c.Code));
            }

            Assert.Equal(2, dataFromExport.Count());
        }

        [SkippableTheory]
        [InlineData("configimage:1234567890")]
        [InlineData("configimage@sha256:592535ef52d742f81e35f4d87b43d9b535ed56cf58c90a14fc5fd7ea0fbb8695")]
        [InlineData("wrongimage:default")]
        public async Task GivenAInvalidAcrReference_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned(string imageReference)
        {
            var registry = GetTestContainerRegistryInfo();

            // Here we skip local E2E test since we need Managed Identity for container registry token.
            // We also skip the case when environmental variable is not provided (not able to upload configurations)
            Skip.If(_isUsingInProcTestServer || registry == null);
            await PushConfigurationAsync(registry, TestRepositoryName, TestRepositoryTag, RedactResourceIdAnonymizationConfiguration);

            _metricHandler?.ResetCount();
            var dateTime = DateTimeOffset.UtcNow;
            var resourceToCreate = Samples.GetDefaultPatient().ToPoco<Patient>();
            resourceToCreate.Id = Guid.NewGuid().ToString();
            await _testFhirClient.UpdateAsync(resourceToCreate);

            string containerName = Guid.NewGuid().ToString("N");
            string reference = $"{registry.Server}/{imageReference}";
            Uri contentLocation = await _testFhirClient.AnonymizedExportUsingAcrAsync(TestConfigName, reference, dateTime, containerName);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);

            var responseContent = await response.Content.ReadAsStringAsync();
            Assert.Contains($"Image Not Found.", responseContent);
        }

        [SkippableFact]
        public async Task GivenInvalidConfigurationNotInAcr_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            var registry = GetTestContainerRegistryInfo();

            // Here we skip local E2E test since we need Managed Identity for container registry token.
            // We also skip the case when environmental variable is not provided (not able to upload configurations)
            Skip.If(_isUsingInProcTestServer || registry == null);
            await PushConfigurationAsync(registry, TestRepositoryName, TestRepositoryTag, "Invalid Json.");

            _metricHandler?.ResetCount();
            var dateTime = DateTimeOffset.UtcNow;
            string containerName = Guid.NewGuid().ToString("N");
            string reference = $"{registry.Server}/{TestRepositoryName}:{TestRepositoryTag}";
            Uri contentLocation = await _testFhirClient.AnonymizedExportUsingAcrAsync(TestConfigName, reference, dateTime, containerName);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Failed to parse configuration file", responseContent);
        }

        [SkippableFact]
        public async Task GivenAConfigurationNotExisted_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            var registry = GetTestContainerRegistryInfo();

            // Here we skip local E2E test since we need Managed Identity for container registry token.
            // We also skip the case when environmental variable is not provided (not able to upload configurations)
            Skip.If(_isUsingInProcTestServer || registry == null);
            await PushConfigurationAsync(registry, TestRepositoryName, TestRepositoryTag, RedactResourceIdAnonymizationConfiguration);

            _metricHandler?.ResetCount();
            var dateTime = DateTimeOffset.UtcNow;

            string containerName = Guid.NewGuid().ToString("N");
            string reference = $"{registry.Server}/{TestRepositoryName}:{TestRepositoryTag}";
            Uri contentLocation = await _testFhirClient.AnonymizedExportUsingAcrAsync("not-exist.json", reference, dateTime, containerName);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Anonymization configuration 'not-exist.json' not found.", responseContent);
        }

        [SkippableFact]
        public async Task GivenALargeConfiguration_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            var registry = GetTestContainerRegistryInfo();

            // Here we skip local E2E test since we need Managed Identity for container registry token.
            // We also skip the case when environmental variable is not provided (not able to upload configurations)
            Skip.If(_isUsingInProcTestServer || registry == null);
            string largeConfig = new string('*', (1024 * 1024) + 1); // Large config > 1MB
            await PushConfigurationAsync(registry, TestRepositoryName, TestRepositoryTag, largeConfig);

            _metricHandler?.ResetCount();
            var dateTime = DateTimeOffset.UtcNow;

            string containerName = Guid.NewGuid().ToString("N");
            string reference = $"{registry.Server}/{TestRepositoryName}:{TestRepositoryTag}";
            Uri contentLocation = await _testFhirClient.AnonymizedExportUsingAcrAsync(TestConfigName, reference, dateTime, containerName);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Anonymization configuration is too large", responseContent);
        }

        private async Task<HttpResponseMessage> WaitForCompleteAsync(Uri contentLocation)
        {
            HttpStatusCode resultCode = HttpStatusCode.Accepted;
            HttpResponseMessage response = null;
            while (resultCode == HttpStatusCode.Accepted)
            {
                await Task.Delay(5000);

                response = await _testFhirClient.CheckExportAsync(contentLocation);

                resultCode = response.StatusCode;
            }

            return response;
        }

        private async Task<IList<Uri>> CheckExportStatus(HttpResponseMessage response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception($"Export request failed with status code {response.StatusCode}");
            }

            // we have got the result. Deserialize into output response.
            var contentString = await response.Content.ReadAsStringAsync();

            ExportJobResult exportJobResult = JsonConvert.DeserializeObject<ExportJobResult>(contentString);
            return exportJobResult.Output.Select(x => x.FileUri).ToList();
        }

        private async Task<IEnumerable<string>> DownloadBlobAndParse(IList<Uri> blobUri)
        {
            CloudStorageAccount cloudAccount = GetCloudStorageAccountHelper();
            CloudBlobClient blobClient = cloudAccount.CreateCloudBlobClient();
            var result = new List<string>();

            foreach (Uri uri in blobUri)
            {
                var blob = new CloudBlockBlob(uri, blobClient);
                string allData = await blob.DownloadTextAsync();

                var splitData = allData.Split("\n");

                foreach (var entry in splitData)
                {
                    if (string.IsNullOrWhiteSpace(entry))
                    {
                        continue;
                    }

                    result.Add(entry);
                }
            }

            return result;
        }

        private CloudStorageAccount GetCloudStorageAccountHelper()
        {
            CloudStorageAccount storageAccount = null;

            string exportStoreFromEnvironmentVariable = Environment.GetEnvironmentVariable("TestExportStoreUri");
            string exportStoreKeyFromEnvironmentVariable = Environment.GetEnvironmentVariable("TestExportStoreKey");
            if (!string.IsNullOrEmpty(exportStoreFromEnvironmentVariable) && !string.IsNullOrEmpty(exportStoreKeyFromEnvironmentVariable))
            {
                Uri integrationStoreUri = new Uri(exportStoreFromEnvironmentVariable);
                string storageAccountName = integrationStoreUri.Host.Split('.')[0];
                StorageCredentials storageCredentials = new StorageCredentials(storageAccountName, exportStoreKeyFromEnvironmentVariable);
                storageAccount = new CloudStorageAccount(storageCredentials, useHttps: true);
            }
            else
            {
                CloudStorageAccount.TryParse(LocalIntegrationStoreConnectionString, out storageAccount);
            }

            if (storageAccount == null)
            {
                throw new Exception("Unable to create a cloud storage account");
            }

            return storageAccount;
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

        internal class ContainerRegistryInfo
        {
            public string Server { get; set; }

            public string Username { get; set; }

            public string Password { get; set; }
        }

        internal class AcrBasicToken : ServiceClientCredentials
        {
            private ContainerRegistryInfo _registry;

            public AcrBasicToken(ContainerRegistryInfo registry)
            {
                _registry = registry;
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
    }
}
