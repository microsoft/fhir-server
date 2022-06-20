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
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Metric;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Xunit;
using FhirGroup = Hl7.Fhir.Model.Group;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.Category, Categories.AnonymizedExport)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class AnonymizedExportTests : IClassFixture<ExportTestFixture>
    {
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

        public AnonymizedExportTests(ExportTestFixture fixture)
        {
            _isUsingInProcTestServer = fixture.IsUsingInProcTestServer;
            _testFhirClient = fixture.TestFhirClient;
            _metricHandler = fixture.MetricHandler;
        }

        [Theory]
        [InlineData("")]
        [InlineData("Patient/")]
        public async Task GivenAValidConfigurationWithETag_WhenExportingAnonymizedData_ResourceShouldBeAnonymized(string path)
        {
            _metricHandler?.ResetCount();
            var dateTime = DateTimeOffset.UtcNow;
            var resourceToCreate = Samples.GetDefaultPatient().ToPoco<Patient>();
            resourceToCreate.Id = Guid.NewGuid().ToString();
            await _testFhirClient.UpdateAsync(resourceToCreate);

            (string fileName, string etag) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);
            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, dateTime, containerName, etag, path);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            IList<Uri> blobUris = await CheckExportStatus(response);

            IEnumerable<string> dataFromExport = await DownloadBlobAndParse(blobUris);
            FhirJsonParser parser = new FhirJsonParser();

            foreach (string content in dataFromExport)
            {
                Resource result = parser.Parse<Resource>(content);

                Assert.Contains(result.Meta.Security, c => "REDACTED".Equals(c.Code));
            }

            // Only check metric for local tests
            if (_isUsingInProcTestServer)
            {
                Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
            }
        }

        [Fact]
        public async Task GivenAValidConfigurationWithETag_WhenExportingGroupAnonymizedData_ResourceShouldBeAnonymized()
        {
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

            (string fileName, string etag) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);
            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, dateTime, containerName, etag, $"Group/{groupId}/");
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

            // Only check metric for local tests
            if (_isUsingInProcTestServer)
            {
                Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
            }
        }

        [SkippableFact]
        public async Task GivenAValidConfigurationWithETagNoQuotes_WhenExportingAnonymizedData_ResourceShouldBeAnonymized()
        {
            _metricHandler?.ResetCount();
            var dateTime = DateTimeOffset.UtcNow;
            var resourceToCreate = Samples.GetDefaultPatient().ToPoco<Patient>();
            resourceToCreate.Id = Guid.NewGuid().ToString();
            await _testFhirClient.UpdateAsync(resourceToCreate);

            (string fileName, string etag) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);
            etag = etag.Substring(1, 17);
            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, dateTime, containerName, etag);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            IList<Uri> blobUris = await CheckExportStatus(response);

            IEnumerable<string> dataFromExport = await DownloadBlobAndParse(blobUris);
            FhirJsonParser parser = new FhirJsonParser();

            foreach (string content in dataFromExport)
            {
                Resource result = parser.Parse<Resource>(content);

                Assert.Contains(result.Meta.Security, c => "REDACTED".Equals(c.Code));
            }

            // Only check metric for local tests
            if (_isUsingInProcTestServer)
            {
                Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
            }
        }

        [SkippableFact]
        public async Task GivenAValidConfigurationWithoutETag_WhenExportingAnonymizedData_ResourceShouldBeAnonymized()
        {
            _metricHandler?.ResetCount();

            var resourceToCreate = Samples.GetDefaultPatient().ToPoco<Patient>();
            resourceToCreate.Id = Guid.NewGuid().ToString();
            var dateTime = DateTimeOffset.UtcNow;
            await _testFhirClient.UpdateAsync(resourceToCreate);

            (string fileName, string _) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);

            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, dateTime, containerName);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            IList<Uri> blobUris = await CheckExportStatus(response);

            IEnumerable<string> dataFromExport = await DownloadBlobAndParse(blobUris);
            FhirJsonParser parser = new FhirJsonParser();

            foreach (string content in dataFromExport)
            {
                Resource result = parser.Parse<Resource>(content);

                Assert.Contains(result.Meta.Security, c => "REDACTED".Equals(c.Code));
            }

            // Only check metric for local tests
            if (_isUsingInProcTestServer)
            {
                Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
            }
        }

        [SkippableFact]
        public async Task GivenInvalidConfiguration_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            _metricHandler?.ResetCount();

            (string fileName, string etag) = await UploadConfigurationAsync("Invalid Json.");
            var dateTime = DateTimeOffset.UtcNow;
            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, dateTime, containerName, etag);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Failed to parse configuration file", responseContent);

            // Only check metric for local tests
            if (_isUsingInProcTestServer)
            {
                Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
            }
        }

        [SkippableFact]
        public async Task GivenAGroupIdNotExisted_WhenExportingGroupAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            _metricHandler?.ResetCount();
            var dateTime = DateTimeOffset.UtcNow;
            (string fileName, string etag) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);

            string groupId = "not-exist-id";
            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, dateTime, containerName, etag, $"Group/{groupId}/");
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains($"Group {groupId} was not found", responseContent);

            // Only check metric for local tests
            if (_isUsingInProcTestServer)
            {
                Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
            }
        }

        [SkippableFact]
        public async Task GivenInvalidEtagProvided_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            _metricHandler?.ResetCount();
            var dateTime = DateTimeOffset.UtcNow;
            (string fileName, string _) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);

            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, dateTime, containerName, "\"0x000000000000000\"");
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("The condition specified using HTTP conditional header(s) is not met.", responseContent);

            // Only check metric for local tests
            if (_isUsingInProcTestServer)
            {
                Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
            }
        }

        [SkippableFact]
        public async Task GivenEtagInWrongFormatProvided_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            _metricHandler?.ResetCount();
            var dateTime = DateTimeOffset.UtcNow;
            (string fileName, string _) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);

            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, dateTime, containerName, "\"invalid-etag");
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("The condition specified using HTTP conditional header(s) is not met.", responseContent);

            // Only check metric for local tests
            if (_isUsingInProcTestServer)
            {
                Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
            }
        }

        [SkippableFact]
        public async Task GivenAContainerNotExisted_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            _metricHandler?.ResetCount();
            var dateTime = DateTimeOffset.UtcNow;
            await InitializeAnonymizationContainer();

            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync("not-exist.json", dateTime, containerName);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Anonymization configuration 'not-exist.json' not found.", responseContent);

            // Only check metric for local tests
            if (_isUsingInProcTestServer)
            {
                Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
            }
        }

        [SkippableFact]
        public async Task GivenALargeConfigurationProvided_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            _metricHandler?.ResetCount();
            var dateTime = DateTimeOffset.UtcNow;
            string largeConfig = new string('*', (1024 * 1024) + 1); // Large config > 1MB
            (string fileName, string etag) = await UploadConfigurationAsync(largeConfig);

            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, dateTime, containerName, etag);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Anonymization configuration is too large", responseContent);

            // Only check metric for local tests
            if (_isUsingInProcTestServer)
            {
                Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
            }
        }

        [SkippableFact]
        public async Task GivenAnAnonymizedExportRequestWithoutContainerName_WhenExportingAnonymizedData_ThenFhirExceptionShouldBeThrewFromFhirClient()
        {
            var dateTime = DateTimeOffset.UtcNow;
            (string fileName, string _) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);

            string containerName = string.Empty;
            await Assert.ThrowsAsync<FhirException>(() => _testFhirClient.AnonymizedExportAsync(fileName, dateTime, containerName));
        }

        private async Task<(string name, string eTag)> UploadConfigurationAsync(string configurationContent, string blobName = null)
        {
            blobName = blobName ?? $"{Guid.NewGuid()}.json";
            CloudBlobContainer container = await InitializeAnonymizationContainer();

            CloudBlockBlob blob = container.GetBlockBlobReference(blobName);
            await blob.DeleteIfExistsAsync();

            await blob.UploadTextAsync(configurationContent);

            return (blobName, blob.Properties.ETag);
        }

        private async Task<CloudBlobContainer> InitializeAnonymizationContainer()
        {
            CloudStorageAccount cloudAccount = GetCloudStorageAccountHelper();
            CloudBlobClient blobClient = cloudAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference("anonymization");
            await container.CreateIfNotExistsAsync();
            return container;
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
    }
}
