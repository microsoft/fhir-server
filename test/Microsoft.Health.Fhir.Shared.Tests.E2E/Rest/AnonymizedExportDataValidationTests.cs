// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Auth;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
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
    public class AnonymizedExportDataValidationTests : IClassFixture<HttpIntegrationTestFixture<StartupForAnonymizedExportTestProvider>>
    {
        private readonly TestFhirClient _testFhirClient;
        private readonly ExportJobConfiguration _exportConfiguration;
        private const string RedactResourceIdAnonymizationConfiguration = @"
{
    ""fhirPathRules"": [
        {""path"": ""Resource.nodesByName('id')"", ""method"": ""redact""},
        {""path"": ""nodesByType('Human').name"", ""method"": ""redact""}
    ]
}";

        public AnonymizedExportDataValidationTests(HttpIntegrationTestFixture<StartupForAnonymizedExportTestProvider> fixture)
        {
            _testFhirClient = fixture.TestFhirClient;
            _exportConfiguration = ((IOptions<ExportJobConfiguration>)(fixture.TestFhirServer as InProcTestFhirServer).Server.Services.GetService(typeof(IOptions<ExportJobConfiguration>))).Value;
        }

        [Fact]
        public async Task GivenAValidConfigurationWithETag_WhenExportingAnonymizedData_ResourceShouldBeAnonymized()
        {
            (string fileName, string etag) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);

            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, etag);
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

        [Fact]
        public async Task GivenAValidConfigurationWithoutETag_WhenExportingAnonymizedData_ResourceShouldBeAnonymized()
        {
            (string fileName, string _) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);

            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName);
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

        [Fact]
        public async Task GivenInvalidConfiguration_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            (string fileName, string etag) = await UploadConfigurationAsync("Invalid Json.");

            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, etag);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Failed to parse configuration file", responseContent);
        }

        [Fact]
        public async Task GivenInvalidEtagProvided_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            (string fileName, string _) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);

            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, "\"0x000000000000000\"");
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("The condition specified using HTTP conditional header(s) is not met.", responseContent);
        }

        [Fact]
        public async Task GivenEtagInWrongFormatProvided_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            (string fileName, string _) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);

            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, "invalid-etag");
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("The format of value 'invalid-etag' is invalid.", responseContent);
        }

        [Fact]
        public async Task GivenAContainerNotExisted_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync("not-exist.json");
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Configuration not found on the destination storage.", responseContent);
        }

        [Fact]
        public async Task GivenALargeConfigurationProvided_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            string largeConfig = new string('*', (1024 * 1024) + 1); // Large config > 1MB
            (string fileName, string etag) = await UploadConfigurationAsync(largeConfig);

            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, etag);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Anonymization configuration is too large", responseContent);
        }

        private async Task<(string, string)> UploadConfigurationAsync(string configurationContent, string blobName = null)
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
