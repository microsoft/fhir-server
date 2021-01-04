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
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Azure;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Shared.Tests.E2E.Rest.Metric;
using Microsoft.Health.Fhir.TemplateManagement.Client;
using Microsoft.Health.Fhir.TemplateManagement.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.Rest
{
    [Trait(Traits.Category, Categories.Export)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class AnonymizedExportTests : IClassFixture<ExportTestFixture>
    {
        private bool _isUsingInProcTestServer = false;
        private readonly TestFhirClient _testFhirClient;
        private readonly ExportJobConfiguration _exportConfiguration;
        private readonly MetricHandler _metricHandler;
        private const string RedactResourceIdAnonymizationConfiguration = @"
{
    ""fhirPathRules"": [
        {""path"": ""Resource.nodesByName('id')"", ""method"": ""redact""},
        {""path"": ""nodesByType('Human').name"", ""method"": ""redact""}
    ]
}";

        public AnonymizedExportTests(ExportTestFixture fixture)
        {
            _isUsingInProcTestServer = fixture.IsUsingInProcTestServer;
            _testFhirClient = fixture.TestFhirClient;
            _metricHandler = fixture.MetricHandler;
            _exportConfiguration = ((IOptions<ExportJobConfiguration>)(fixture.TestFhirServer as InProcTestFhirServer)?.Server?.Services?.GetService(typeof(IOptions<ExportJobConfiguration>)))?.Value;
        }

        [Fact]
        public async Task GivenAValidConfigurationWithETag_WhenExportingAnonymizedData_ResourceShouldBeAnonymized()
        {
            if (!_isUsingInProcTestServer)
            {
                return;
            }

            _metricHandler.ResetCount();

            (string fileName, string etag) = await UploadConfigurationToStorageAsync(RedactResourceIdAnonymizationConfiguration);

            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, containerName, etag);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            IList<Uri> blobUris = await CheckExportStatus(response);

            IEnumerable<string> dataFromExport = await DownloadBlobAndParse(blobUris);
            FhirJsonParser parser = new FhirJsonParser();

            foreach (string content in dataFromExport)
            {
                Resource result = parser.Parse<Resource>(content);

                Assert.Contains(result.Meta.Security, c => "REDACTED".Equals(c.Code));
            }

            Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
        }

        [Fact]
        public async Task GivenAValidConfigurationWithETag_WhenExportingAnonymizedData_WithACRProvider_ResourceShouldBeAnonymized()
        {
            if (!_isUsingInProcTestServer)
            {
                return;
            }

            _metricHandler.ResetCount();

            string registryServer = Environment.GetEnvironmentVariable("TestContainerRegistryServer");
            string registryPassword = Environment.GetEnvironmentVariable("TestContainerRegistryPassword");
            (string tag, string etag) = await UploadConfigurationToAcrAsync(RedactResourceIdAnonymizationConfiguration, registryServer, registryPassword);

            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(tag, containerName, etag);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            IList<Uri> blobUris = await CheckExportStatus(response);

            IEnumerable<string> dataFromExport = await DownloadBlobAndParse(blobUris);
            FhirJsonParser parser = new FhirJsonParser();

            foreach (string content in dataFromExport)
            {
                Resource result = parser.Parse<Resource>(content);

                Assert.Contains(result.Meta.Security, c => "REDACTED".Equals(c.Code));
            }

            Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
        }

        [Fact]
        public async Task Mytest()
        {
            var acrProvider = new ExportDestinationArtifactAcrProvider();

            var cancellationToken = new CancellationToken(false);
            using (Stream stream = new MemoryStream())
            {
                await acrProvider.FetchAsync(string.Empty, stream, cancellationToken);
                stream.Position = 0;
                using (StreamReader reader = new StreamReader(stream))
                {
                    string configurationContent = await reader.ReadToEndAsync();
                    Console.WriteLine("Wanquan");
                }
            }
        }

        [Fact]
        public async Task GivenAValidConfigurationWithETagNoQuotes_WhenExportingAnonymizedData_ResourceShouldBeAnonymized()
        {
            if (!_isUsingInProcTestServer)
            {
                return;
            }

            _metricHandler.ResetCount();

            (string fileName, string etag) = await UploadConfigurationToStorageAsync(RedactResourceIdAnonymizationConfiguration);
            etag = etag.Substring(1, 17);
            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, containerName, etag);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            IList<Uri> blobUris = await CheckExportStatus(response);

            IEnumerable<string> dataFromExport = await DownloadBlobAndParse(blobUris);
            FhirJsonParser parser = new FhirJsonParser();

            foreach (string content in dataFromExport)
            {
                Resource result = parser.Parse<Resource>(content);

                Assert.Contains(result.Meta.Security, c => "REDACTED".Equals(c.Code));
            }

            Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
        }

        [Fact]
        public async Task GivenAValidConfigurationWithoutETag_WhenExportingAnonymizedData_ResourceShouldBeAnonymized()
        {
            if (!_isUsingInProcTestServer)
            {
                return;
            }

            _metricHandler.ResetCount();

            (string fileName, string _) = await UploadConfigurationToStorageAsync(RedactResourceIdAnonymizationConfiguration);

            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, containerName);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            IList<Uri> blobUris = await CheckExportStatus(response);

            IEnumerable<string> dataFromExport = await DownloadBlobAndParse(blobUris);
            FhirJsonParser parser = new FhirJsonParser();

            foreach (string content in dataFromExport)
            {
                Resource result = parser.Parse<Resource>(content);

                Assert.Contains(result.Meta.Security, c => "REDACTED".Equals(c.Code));
            }

            Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
        }

        [Fact]
        public async Task GivenInvalidConfiguration_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            if (!_isUsingInProcTestServer)
            {
                return;
            }

            _metricHandler.ResetCount();

            (string fileName, string etag) = await UploadConfigurationToStorageAsync("Invalid Json.");

            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, containerName, etag);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Failed to parse configuration file", responseContent);

            Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
        }

        [Fact]
        public async Task GivenInvalidEtagProvided_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            if (!_isUsingInProcTestServer)
            {
                return;
            }

            _metricHandler.ResetCount();

            (string fileName, string _) = await UploadConfigurationToStorageAsync(RedactResourceIdAnonymizationConfiguration);

            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, containerName, "\"0x000000000000000\"");
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("The condition specified using HTTP conditional header(s) is not met.", responseContent);

            Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
        }

        [Fact]
        public async Task GivenEtagInWrongFormatProvided_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            if (!_isUsingInProcTestServer)
            {
                return;
            }

            _metricHandler.ResetCount();

            (string fileName, string _) = await UploadConfigurationToStorageAsync(RedactResourceIdAnonymizationConfiguration);

            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, containerName, "\"invalid-etag");
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("invalid-etag' is invalid.", responseContent);

            Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
        }

        [Fact]
        public async Task GivenAContainerNotExisted_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            if (!_isUsingInProcTestServer)
            {
                return;
            }

            _metricHandler.ResetCount();

            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync("not-exist.json", containerName);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Configuration not found on the destination storage.", responseContent);

            Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
        }

        [Fact]
        public async Task GivenALargeConfigurationProvided_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            if (!_isUsingInProcTestServer)
            {
                return;
            }

            _metricHandler.ResetCount();

            string largeConfig = new string('*', (1024 * 1024) + 1); // Large config > 1MB
            (string fileName, string etag) = await UploadConfigurationToStorageAsync(largeConfig);

            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, containerName, etag);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Anonymization configuration is too large", responseContent);

            Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
        }

        [Fact]
        public async Task GivenAnAnonymizedExportRequestWithoutContainerName_WhenExportingAnonymizedData_ThenFhirExceptionShouldBeThrewFromFhirClient()
        {
            if (!_isUsingInProcTestServer)
            {
                return;
            }

            (string fileName, string _) = await UploadConfigurationToStorageAsync(RedactResourceIdAnonymizationConfiguration);

            string containerName = string.Empty;
            await Assert.ThrowsAsync<FhirException>(() => _testFhirClient.AnonymizedExportAsync(fileName, containerName));
        }

        private async Task<(string, string)> UploadConfigurationToStorageAsync(string configurationContent, string blobName = null)
        {
            blobName = blobName ?? $"{Guid.NewGuid()}.json";
            CloudStorageAccount cloudAccount = GetCloudStorageAccountHelper();
            CloudBlobClient blobClient = cloudAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference("anonymization");
            await container.CreateIfNotExistsAsync();

            CloudBlockBlob blob = container.GetBlockBlobReference(blobName);
            await blob.DeleteIfExistsAsync();

            await blob.UploadTextAsync(configurationContent);

            return (blobName, blob.Properties.ETag);
        }

        private async Task<(string, string)> UploadConfigurationToAcrAsync(
            string configurationContent,
            string registryServer,
            string registryPassword,
            string tag = null)
        {
            tag = tag ?? $"{Guid.NewGuid()}.json";
            string registryUsername = registryServer.Split('.')[0];
            string repository = "anonymization";
            string imageReference = string.Format("{0}/{1}:{2}", registryServer, "acrtest", "onelayer");
            ImageInfo imageInfo = ImageInfo.CreateFromImageReference(imageReference);
            string token = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{registryUsername}:{registryPassword}"));

            IAzureContainerRegistryClient client = new AzureContainerRegistryClient(imageInfo.Registry, new ACRClientCredentials(token));

            int schemaV2 = 2;
            string mediatypeV2Manifest = "application/vnd.docker.distribution.manifest.v2+json";
            string mediatypeV1Manifest = "application/vnd.oci.image.config.v1+json";
            string emptyConfigStr = "{}";

            // Upload config blob
            byte[] originalImageConfigBytes = Encoding.UTF8.GetBytes(emptyConfigStr);
            using var originalConfigStream = new MemoryStream(originalImageConfigBytes);
            string originalConfigDigest = ComputeDigest(originalConfigStream);
            await UploadBlob(client, originalConfigStream, repository, originalConfigDigest);

            // Upload memory blob

            List<Descriptor> layers = new List<Descriptor>();
            byte[] originalConfigurationBytes = Encoding.UTF8.GetBytes(configurationContent);
            using var byteStream = new MemoryStream(originalConfigurationBytes);
            var blobLength = byteStream.Length;
            string blobDigest = ComputeDigest(byteStream);
            await UploadBlob(client, byteStream, repository, blobDigest);
            layers.Add(new Descriptor("application/vnd.oci.image.layer.v1.tar", blobLength, blobDigest));

            // Push manifest
            var v2Manifest = new V2Manifest(schemaV2, mediatypeV2Manifest, new Descriptor(mediatypeV1Manifest, originalImageConfigBytes.Length, originalConfigDigest), layers);
            await client.Manifests.CreateAsync(repository, tag, v2Manifest);

            return (tag, string.Empty);
        }

        private async Task UploadBlob(IAzureContainerRegistryClient client, Stream stream, string repository, string digest)
        {
            stream.Position = 0;
            var uploadInfo = await client.Blob.StartUploadAsync(repository);
            var uploadedLayer = await client.Blob.UploadAsync(stream, uploadInfo.Location);
            await client.Blob.EndUploadAsync(digest, uploadedLayer.Location);
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
            CloudStorageAccount cloudAccount = null;
            string connectionString = _exportConfiguration.StorageAccountConnection;
            if (string.IsNullOrEmpty(connectionString))
            {
                Uri sampleUri = new Uri(_exportConfiguration.StorageAccountUri);
                string storageAccountName = sampleUri.Host.Split('.')[0];
                string storageSecret = Environment.GetEnvironmentVariable(storageAccountName + "_secret");
                if (!string.IsNullOrWhiteSpace(storageSecret))
                {
                    StorageCredentials storageCredentials = new StorageCredentials(storageAccountName, storageSecret);
                    cloudAccount = new CloudStorageAccount(storageCredentials, useHttps: true);
                }
            }
            else
            {
                CloudStorageAccount.TryParse(_exportConfiguration.StorageAccountConnection, out cloudAccount);
            }

            if (cloudAccount == null)
            {
                throw new Exception("Unable to create a cloud storage account");
            }

            return cloudAccount;
        }
    }
}
