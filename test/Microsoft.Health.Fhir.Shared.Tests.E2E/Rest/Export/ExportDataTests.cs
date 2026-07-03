// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Export
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    [Trait(Traits.Category, Categories.ExportData)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class ExportDataTests : IClassFixture<ExportDataTestFixture>
    {
        private readonly TestFhirClient _testFhirClient;
        private readonly ITestOutputHelper _outputHelper;
        private readonly FhirJsonParser _fhirJsonParser;
        private readonly ExportDataTestFixture _fixture;

        public ExportDataTests(ExportDataTestFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _testFhirClient = fixture.TestFhirClient;
            _outputHelper = testOutputHelper;
            _fhirJsonParser = new FhirJsonParser();
            _fixture = fixture;
        }

        [Fact]
        public async Task GivenFhirServer_WhenAllDataIsExported_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // NOTE: Azurite or Azure Storage Explorer is required to run these tests locally.\
            string parameters = _fixture.ExportTestFilterQueryParameters();

            // Trigger export request and check for export status
            Uri contentLocation = await _fixture.TestFhirClient.ExportAsync(parameters: parameters);

            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(_fixture.TestResources, dataFromExport, _outputHelper));
        }

        [Fact]
        public async Task GivenFhirServer_WhenPatientDataIsExported_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // NOTE: Azurite or Azure Storage Explorer is required to run these tests locally.
            string parameters = _fixture.ExportTestFilterQueryParameters();

            // Trigger export request and check for export status
            Uri contentLocation = await _fixture.TestFhirClient.ExportAsync(path: "Patient/", parameters: parameters);

            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(_fixture.TestPatientCompartmentResources, dataFromExport, _outputHelper));
        }

        [Fact]
        public async Task GivenFhirServer_WhenAllObservationAndPatientDataIsExported_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // NOTE: Azurite or Azure Storage Explorer is required to run these tests locally.
            string[] testResorceTypes = { "Observation", "Patient" };
            var parameters = _fixture.ExportTestFilterQueryParameters(testResorceTypes);

            // Trigger export request and check for export status
            Uri contentLocation = await _fixture.TestFhirClient.ExportAsync(parameters: parameters);

            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            var expectedResources = _fixture.TestResources
                .Where(r => testResorceTypes.Contains(r.Key.resourceType))
                .ToDictionary(x => x.Key, x => x.Value);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(expectedResources, dataFromExport, _outputHelper));
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
        public async Task GivenCompletedPatientExportHasOutput_WhenObservationSmartScopeRequestsExportStatus_ThenServerShouldReturnForbidden()
        {
            Uri contentLocation = await CreateCompletedPatientExportStatusUriAsync();

            string accessToken = await GetSmartAccessTokenAsync("system/Observation.read");

            using HttpClient smartClient = CreateUnauthenticatedHttpClient();
            using HttpRequestMessage getStatusRequest = new HttpRequestMessage(HttpMethod.Get, contentLocation);
            getStatusRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using HttpResponseMessage getStatusResponse = await smartClient.SendAsync(getStatusRequest);

            Assert.Equal(HttpStatusCode.Forbidden, getStatusResponse.StatusCode);
        }

        [Fact]
        public async Task GivenFhirServer_WhenPatientObservationDataIsExported_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // NOTE: Azurite or Azure Storage Explorer is required to run these tests locally.
            var parameters = _fixture.ExportTestFilterQueryParameters("Observation");

            // Trigger export request and check for export status
            Uri contentLocation = await _fixture.TestFhirClient.ExportAsync(path: "Patient/", parameters: parameters);

            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            var expectedResources = _fixture.TestPatientCompartmentResources
                .Where(r => r.Key.resourceType == "Observation")
                .ToDictionary(x => x.Key, x => x.Value);

            // Assert both data are equal. Only Observation data is expected due to the type query parameter.
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(expectedResources, dataFromExport, _outputHelper));
        }

        // No need to test both code paths for testing container is written to.
        [Fact]
        public async Task GivenFhirServer_WhenAllDataIsExportedToASpecificContainer_ThenExportedDataIsInTheSpecifiedContianer()
        {
            // NOTE: Azurite or Azure Storage Explorer is required to run these tests locally.
            string testContainer = "test-container";

            // Trigger export request and check for export status
            Uri contentLocation = await _fixture.TestFhirClient.ExportAsync(parameters: $"_container={testContainer}&{_fixture.ExportTestFilterQueryParameters()}");

            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(_fixture.TestResources, dataFromExport, _outputHelper));
            Assert.True(blobUris.All((url) => url.OriginalString.Contains(testContainer)));
        }

        [Fact]
        [Trait(Traits.Category, Categories.ExportLongRunning)]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenFhirServer_WhenDataIsExportedWithHistoryParallel_ThenExportedDataIsSameAsDataInFhirServer()
        {
            await ExportAndSoftDeleteTestHelper(parallel: true, history: true, deletes: false);
        }

        [Fact]
        [Trait(Traits.Category, Categories.ExportLongRunning)]
        public async Task GivenFhirServer_WhenDataIsExportedWithHistoryNotParallel_ThenExportedDataIsSameAsDataInFhirServer()
        {
            await ExportAndSoftDeleteTestHelper(parallel: false, history: true, deletes: false);
        }

        [Fact]
        [Trait(Traits.Category, Categories.ExportLongRunning)]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenFhirServer_WhenDataIsExportedWithSoftDeletesParallel_ThenExportedDataIsSameAsDataInFhirServer()
        {
            await ExportAndSoftDeleteTestHelper(parallel: true, history: false, deletes: true);
        }

        [Fact]
        [Trait(Traits.Category, Categories.ExportLongRunning)]
        public async Task GivenFhirServer_WhenDataIsExportedWithSoftDeletesNotParallel_ThenExportedDataIsSameAsDataInFhirServer()
        {
            await ExportAndSoftDeleteTestHelper(parallel: false, history: false, deletes: true);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenFhirServer_WhenDataIsExportedWithHistoryAndSoftDeletesParallel_ThenExportedDataIsSameAsDataInFhirServer()
        {
            await ExportAndSoftDeleteTestHelper(parallel: true, history: true, deletes: true);
        }

        [Fact]
        [Trait(Traits.Category, Categories.ExportLongRunning)]
        public async Task GivenFhirServer_WhenDataIsExportedWithHistoryAndSoftDeletesNotParallel_ThenExportedDataIsSameAsDataInFhirServer()
        {
            await ExportAndSoftDeleteTestHelper(parallel: false, history: true, deletes: true);
        }

        // _tag filter cannot be used with history or deleted export. Using isParallel to test both SQL code paths.
        private async Task ExportAndSoftDeleteTestHelper(bool parallel, bool history, bool deletes)
        {
            string uniqueFixtureResources = string.Join(',', _fixture.TestResourcesWithHistoryAndDeletes.Keys.Select(x => x.resourceType).Distinct());
            string includeAssociatedDataParam = (history ? "_history" : string.Empty) + (deletes ? (history ? "," : string.Empty) + "_deleted" : string.Empty);

            // Trigger export request and check for export status. _typeFilter and history/soft delete parameters cannot be used together.
            string parallelQueryParam = $"_isParallel={parallel}";
            Uri contentLocation = await _fixture.TestFhirClient.ExportAsync(parameters: $"_since={_fixture.TestDataInsertionTime:O}&_type={uniqueFixtureResources}&includeAssociatedData={includeAssociatedDataParam}&{parallelQueryParam}");

            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Filter data from export to ONLY look for resource ids of test data from the fixture. This will reduce test flakiness from other resources from other tests.
            var filteredDataFromExport = dataFromExport
                .Where(exp => _fixture.TestResourcesWithHistoryAndDeletes.Any(test => test.Key.resourceType == exp.Key.resourceType && test.Key.resourceId == exp.Key.resourceId))
                .ToDictionary(x => x.Key, x => x.Value);

            var expectedResources = _fixture.TestResourcesWithHistoryAndDeletes;

            if (!history)
            {
                expectedResources = _fixture.TestResourcesWithDeletes;
            }

            if (!deletes)
            {
                expectedResources = _fixture.TestResourcesWithHistory;
            }

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(expectedResources, filteredDataFromExport, _outputHelper));
        }

        private HttpClient CreateUnauthenticatedHttpClient()
        {
            return new HttpClient(_fixture.TestFhirServer.CreateMessageHandler())
            {
                BaseAddress = _fixture.TestFhirServer.BaseAddress,
            };
        }

        private async System.Threading.Tasks.Task<Uri> CreateCompletedPatientExportStatusUriAsync()
        {
            var inProcServer = (InProcTestFhirServer)_fixture.TestFhirServer;
            using IServiceScope scope = inProcServer.Server.Host.Services.CreateScope();

            var queueClient = scope.ServiceProvider.GetRequiredService<IQueueClient>();
            var exportRecord = new ExportJobRecord(
                new Uri(_fixture.TestFhirServer.BaseAddress, "$export?_type=Patient"),
                ExportJobType.All,
                ExportFormatTags.ResourceName,
                KnownResourceTypes.Patient,
                filters: null,
                hash: Guid.NewGuid().ToString(),
                rollingFileSizeInMB: 64);

            exportRecord.Output.Add(
                KnownResourceTypes.Patient,
                new List<ExportFileInfo>
                {
                    new ExportFileInfo(KnownResourceTypes.Patient, new Uri("http://localhost/export/Patient.ndjson"), sequence: 1),
                });

            string serializedRecord = JsonConvert.SerializeObject(exportRecord);
            JobInfo jobInfo = await queueClient.EnqueueWithStatusAsync(
                (byte)QueueType.Export,
                groupId: null,
                definition: serializedRecord,
                jobStatus: JobStatus.Completed,
                result: serializedRecord,
                startDate: DateTime.UtcNow,
                cancellationToken: default);

            return new Uri(_fixture.TestFhirServer.BaseAddress, $"_operations/export/{jobInfo.Id}");
        }

        private async System.Threading.Tasks.Task<string> GetSmartAccessTokenAsync(string scope)
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", TestApplications.SmartUserClient.GrantType },
                { "client_id", TestApplications.SmartUserClient.ClientId },
                { "client_secret", TestApplications.SmartUserClient.ClientSecret },
                { "scope", scope },
                { "resource", AuthenticationSettings.Resource },
            });

            using HttpClient authClient = CreateUnauthenticatedHttpClient();
            using HttpResponseMessage response = await authClient.PostAsync(_fixture.TestFhirServer.TokenUri, content);
            response.EnsureSuccessStatusCode();

            string responseJson = await response.Content.ReadAsStringAsync();
            Dictionary<string, JsonElement> tokenResponse = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseJson);
            return tokenResponse["access_token"].GetString();
        }
    }
}
