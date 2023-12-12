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
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(expectedResources, dataFromExport, _outputHelper));
        }
    }
}
