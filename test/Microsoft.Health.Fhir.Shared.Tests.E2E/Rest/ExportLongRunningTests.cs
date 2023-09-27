// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class ExportLongRunningTests : IClassFixture<ExportTestFixture>
    {
        private readonly TestFhirClient _testFhirClient;
        private readonly ITestOutputHelper _outputHelper;
        private readonly FhirJsonParser _fhirJsonParser;
        private readonly ExportTestFixture _fixture;

        public ExportLongRunningTests(ExportTestFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _testFhirClient = fixture.TestFhirClient;
            _outputHelper = testOutputHelper;
            _fhirJsonParser = new FhirJsonParser();
            _fixture = fixture;
        }

        [Fact]
        public async Task GivenFhirServer_WhenAllDataIsExported_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // NOTE: Azurite or Azure Storage Explorer is required to run these tests locally.

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync(parameters: $"_since={_fixture.TestDataInsertionTime:o}");
            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation, timeToWaitInMinutes: 15);

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

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync(path: "Patient/", parameters: _fixture.ExportTestResourcesQueryParameters);
            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation, timeToWaitInMinutes: 15);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(_fixture.TestResources, dataFromExport, _outputHelper));
        }

        [Fact]
        public async Task GivenFhirServer_WhenAllObservationAndPatientDataIsExported_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // NOTE: Azurite or Azure Storage Explorer is required to run these tests locally.

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync(parameters: $"_type=Observation,Patient&{_fixture.ExportTestResourcesQueryParameters}");
            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation, timeToWaitInMinutes: 15);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(_fixture.TestResources, dataFromExport, _outputHelper));
        }

        [Fact]
        public async Task GivenFhirServer_WhenPatientObservationDataIsExported_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // NOTE: Azurite or Azure Storage Explorer is required to run these tests locally.

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync("Patient/", $"_type=Observation&_typeFilter=Observation%3F_tag%3D{_fixture.FixtureTag}");
            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation, timeToWaitInMinutes: 15);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            var expectedResources = _fixture.TestResources
                .Where(r => r.Key.resourceType == ResourceType.Observation.ToString())
                .ToDictionary(x => x.Key, x => x.Value);

            // Assert both data are equal. Only Observation data is expected due to the type query parameter.
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(expectedResources, dataFromExport, _outputHelper));
        }

        [Fact]
        public async Task GivenFhirServer_WhenAllDataIsExportedToASpecificContainer_ThenExportedDataIsInTheSpecifiedContianer()
        {
            // NOTE: Azurite or Azure Storage Explorer is required to run these tests locally.

            string testContainer = "test-container";

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync(parameters: $"_container={testContainer}&{_fixture.ExportTestResourcesQueryParameters}");
            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation, timeToWaitInMinutes: 15);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(_fixture.TestResources, dataFromExport, _outputHelper));
            Assert.True(blobUris.All((url) => url.OriginalString.Contains(testContainer)));
        }

        [Fact]
        public async Task GivenFhirServer_WhenDataIsExportedWithHistory_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // NOTE: Azurite or Azure Storage Explorer is required to run these tests locally.

            // Trigger export request and check for export status. _typeFilter and history/soft delete parameters cannot be used together.
            Uri contentLocation = await _testFhirClient.ExportAsync(parameters: $"_since={_fixture.TestDataInsertionTime:O}&_includeHistory=true");
            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation, timeToWaitInMinutes: 15);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(_fixture.TestResourcesWithHistory, dataFromExport, _outputHelper));
        }

        [Fact]
        public async Task GivenFhirServer_WhenDataIsExportedWithSoftDeletes_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // NOTE: Azurite or Azure Storage Explorer is required to run these tests locally.

            // Trigger export request and check for export status. _typeFilter and history/soft delete parameters cannot be used together.
            Uri contentLocation = await _testFhirClient.ExportAsync(parameters: $"_since={_fixture.TestDataInsertionTime:O}&_includeDeleted=true");
            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation, timeToWaitInMinutes: 15);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(_fixture.TestResourcesWithDeletes, dataFromExport, _outputHelper));
        }

        [Fact]
        public async Task GivenFhirServer_WhenDataIsExportedWithHistoryAndSoftDeletes_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // NOTE: Azurite or Azure Storage Explorer is required to run these tests locally.

            // Trigger export request and check for export status. _typeFilter and history/soft delete parameters cannot be used together.
            Uri contentLocation = await _testFhirClient.ExportAsync(parameters: $"_since={_fixture.TestDataInsertionTime:O}&_includeHistory=true&_includeDeleted=true");
            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation, timeToWaitInMinutes: 15);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(_fixture.TestResourcesWithHistoryAndDeletes, dataFromExport, _outputHelper));
        }
    }
}
