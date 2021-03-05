// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Tests.E2E.Rest.Metric;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Xunit.Abstractions;
using FhirGroup = Hl7.Fhir.Model.Group;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.Rest
{
    [Trait(Traits.Category, Categories.Export)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class AnonymizedExportTests : IClassFixture<ExportTestFixture>
    {
        private bool _isUsingInProcTestServer = false;
        private readonly TestFhirClient _testFhirClient;
        private readonly ITestOutputHelper _outputHelper;
        private readonly FhirJsonParser _fhirJsonParser;
        private readonly ExportJobConfiguration _exportConfiguration;
        private readonly MetricHandler _metricHandler;
        private const string RedactResourceIdAnonymizationConfiguration = @"
{
    ""fhirPathRules"": [
        {""path"": ""Resource.nodesByName('id')"", ""method"": ""redact""},
        {""path"": ""nodesByType('Human').name"", ""method"": ""redact""}
    ]
}";

        public AnonymizedExportTests(ExportTestFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _isUsingInProcTestServer = fixture.IsUsingInProcTestServer;
            _testFhirClient = fixture.TestFhirClient;
            _outputHelper = testOutputHelper;
            _fhirJsonParser = new FhirJsonParser();
            _metricHandler = fixture.MetricHandler;
            _exportConfiguration = ((IOptions<ExportJobConfiguration>)(fixture.TestFhirServer as InProcTestFhirServer)?.Server?.Services?.GetService(typeof(IOptions<ExportJobConfiguration>)))?.Value;
        }

        [SkippableTheory]
        [InlineData("")]
        [InlineData("Patient/")]
        public async Task GivenAValidConfigurationWithETag_WhenExportingAnonymizedData_ResourceShouldBeAnonymized(string path)
        {
            Skip.If(!_isUsingInProcTestServer, "Not using in-process fhir server.");

            _metricHandler.ResetCount();

            var resourceToCreate = Samples.GetDefaultPatient().ToPoco<Patient>();
            resourceToCreate.Id = Guid.NewGuid().ToString();
            await _testFhirClient.UpdateAsync(resourceToCreate);

            (string fileName, string etag) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);
            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, containerName, etag, path);
            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation);

            Dictionary<(string resourceType, string resourceId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            foreach (var kvp in dataFromExport)
            {
                Assert.Contains(kvp.Value.Meta.Security, c => "REDACTED".Equals(c.Code));
            }

            Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
        }

        [SkippableFact]
        public async Task GivenAValidConfigurationWithETag_WhenExportingGroupAnonymizedData_ResourceShouldBeAnonymized()
        {
            Skip.IfNot(_isUsingInProcTestServer, "Not using in-process fhir server.");

            _metricHandler.ResetCount();

            var patientToCreate = Samples.GetDefaultPatient().ToPoco<Patient>();
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
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, containerName, etag, $"Group/{groupId}/");
            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation);

            Dictionary<(string resourceType, string resourceId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            foreach (var kvp in dataFromExport)
            {
                Assert.Contains(kvp.Value.Meta.Security, c => "REDACTED".Equals(c.Code));
            }

            Assert.Equal(2, dataFromExport.Count);
            Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
        }

        [SkippableFact]
        public async Task GivenAValidConfigurationWithETagNoQuotes_WhenExportingAnonymizedData_ResourceShouldBeAnonymized()
        {
            Skip.If(!_isUsingInProcTestServer, "Not using in-process fhir server.");

            _metricHandler.ResetCount();

            var resourceToCreate = Samples.GetDefaultPatient().ToPoco<Patient>();
            resourceToCreate.Id = Guid.NewGuid().ToString();
            await _testFhirClient.UpdateAsync(resourceToCreate);

            (string fileName, string etag) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);
            etag = etag.Substring(1, 17);
            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, containerName, etag);
            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation);

            Dictionary<(string resourceType, string resourceId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            foreach (var kvp in dataFromExport)
            {
                Assert.Contains(kvp.Value.Meta.Security, c => "REDACTED".Equals(c.Code));
            }

            Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
        }

        [SkippableFact]
        public async Task GivenAValidConfigurationWithoutETag_WhenExportingAnonymizedData_ResourceShouldBeAnonymized()
        {
            Skip.If(!_isUsingInProcTestServer, "Not using in-process fhir server.");

            _metricHandler.ResetCount();

            var resourceToCreate = Samples.GetDefaultPatient().ToPoco<Patient>();
            resourceToCreate.Id = Guid.NewGuid().ToString();
            await _testFhirClient.UpdateAsync(resourceToCreate);

            (string fileName, string _) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);

            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, containerName);
            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation);

            Dictionary<(string resourceType, string resourceId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            foreach (var kvp in dataFromExport)
            {
                Assert.Contains(kvp.Value.Meta.Security, c => "REDACTED".Equals(c.Code));
            }

            Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
        }

        [SkippableFact]
        public async Task GivenInvalidConfiguration_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            Skip.If(!_isUsingInProcTestServer, "Not using in-process fhir server.");

            _metricHandler.ResetCount();

            (string fileName, string etag) = await UploadConfigurationAsync("Invalid Json.");

            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, containerName, etag);

            var ex = await Assert.ThrowsAsync<Exception>(async () => await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation));
            Assert.Contains(HttpStatusCode.BadRequest.ToString(), ex.Message);

            Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
        }

        [SkippableFact]
        public async Task GivenAGroupIdNotExisted_WhenExportingGroupAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            Skip.If(!_isUsingInProcTestServer, "Not using in-process fhir server.");

            _metricHandler.ResetCount();

            (string fileName, string etag) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);

            string groupId = "not-exist-id";
            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, containerName, etag, $"Group/{groupId}/");

            var ex = await Assert.ThrowsAsync<Exception>(async () => await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation));
            Assert.Contains(HttpStatusCode.BadRequest.ToString(), ex.Message);

            Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
        }

        [SkippableFact]
        public async Task GivenInvalidEtagProvided_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            Skip.If(!_isUsingInProcTestServer, "Not using in-process fhir server.");

            _metricHandler.ResetCount();

            (string fileName, string _) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);

            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, containerName, "\"0x000000000000000\"");

            var ex = await Assert.ThrowsAsync<Exception>(async () => await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation));
            Assert.Contains(HttpStatusCode.BadRequest.ToString(), ex.Message);

            Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
        }

        [SkippableFact]
        public async Task GivenEtagInWrongFormatProvided_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            Skip.If(!_isUsingInProcTestServer, "Not using in-process fhir server.");

            _metricHandler.ResetCount();

            (string fileName, string _) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);

            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, containerName, "\"invalid-etag");

            var ex = await Assert.ThrowsAsync<Exception>(async () => await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation));
            Assert.Contains(HttpStatusCode.BadRequest.ToString(), ex.Message);

            Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
        }

        [SkippableFact]
        public async Task GivenAContainerNotExisted_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            Skip.If(!_isUsingInProcTestServer, "Not using in-process fhir server.");

            _metricHandler.ResetCount();

            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync("not-exist.json", containerName);

            var ex = await Assert.ThrowsAsync<Exception>(async () => await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation));
            Assert.Contains(HttpStatusCode.BadRequest.ToString(), ex.Message);

            Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
        }

        [SkippableFact]
        public async Task GivenALargeConfigurationProvided_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            Skip.If(!_isUsingInProcTestServer, "Not using in-process fhir server.");

            _metricHandler.ResetCount();

            string largeConfig = new string('*', (1024 * 1024) + 1); // Large config > 1MB
            (string fileName, string etag) = await UploadConfigurationAsync(largeConfig);

            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await _testFhirClient.AnonymizedExportAsync(fileName, containerName, etag);

            var ex = await Assert.ThrowsAsync<Exception>(async () => await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation));
            Assert.Contains(HttpStatusCode.BadRequest.ToString(), ex.Message);

            Assert.Single(_metricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
        }

        [SkippableFact]
        public async Task GivenAnAnonymizedExportRequestWithoutContainerName_WhenExportingAnonymizedData_ThenFhirExceptionShouldBeThrewFromFhirClient()
        {
            Skip.If(!_isUsingInProcTestServer, "Not using in-process fhir server.");

            (string fileName, string _) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);

            string containerName = string.Empty;
            await Assert.ThrowsAsync<FhirException>(() => _testFhirClient.AnonymizedExportAsync(fileName, containerName));
        }

        private async Task<(string name, string eTag)> UploadConfigurationAsync(string configurationContent, string blobName = null)
        {
            blobName = blobName ?? $"{Guid.NewGuid()}.json";

            BlobServiceClient blobClient = GetCloudStorageAccountHelper();
            BlobContainerClient container = blobClient.GetBlobContainerClient("anonymization");
            await container.CreateIfNotExistsAsync();

            BlobClient blob = container.GetBlobClient(blobName);
            await blob.DeleteIfExistsAsync();

            string eTag;
            using (var ms = new MemoryStream())
            using (var streamWriter = new StreamWriter(ms, Encoding.UTF8))
            {
                await streamWriter.WriteAsync(configurationContent);
                await streamWriter.FlushAsync();
                ms.Seek(0, SeekOrigin.Begin);

                var response = await blob.UploadAsync(ms);
                eTag = response.Value.ETag.ToString();
            }

            return (blobName, eTag);
        }

        private BlobServiceClient GetCloudStorageAccountHelper()
        {
            if (!string.IsNullOrEmpty(_exportConfiguration.StorageAccountConnection))
            {
                return new BlobServiceClient(_exportConfiguration.StorageAccountConnection);
            }

            if (string.IsNullOrEmpty(_exportConfiguration.StorageAccountUri))
            {
                throw new Exception("Unable to create a cloud storage account");
            }

            Uri blobUri = new Uri(_exportConfiguration.StorageAccountUri);
            var blobUriBuilder = new BlobUriBuilder(blobUri);
            return ExportTestHelper.GetCloudStorageAccountHelper(blobUriBuilder.AccountName);
        }
    }
}
