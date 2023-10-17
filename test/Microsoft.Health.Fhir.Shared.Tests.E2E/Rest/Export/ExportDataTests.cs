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

        [Theory]
        [InlineData("since")]
        [InlineData("tag")]
        public async Task GivenFhirServer_WhenAllDataIsExported_ThenExportedDataIsSameAsDataInFhirServer(string parametersKey)
        {
            // NOTE: Azurite or Azure Storage Explorer is required to run these tests locally.

            // Trigger export request and check for export status
            Uri contentLocation = _fixture.ExportTestCasesContentUrls[$"{nameof(GivenFhirServer_WhenAllDataIsExported_ThenExportedDataIsSameAsDataInFhirServer)}-{parametersKey}"];

            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation, 15);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(_fixture.TestResources, dataFromExport, _outputHelper));
        }

        [Theory]
        [InlineData("since")]
        [InlineData("tag")]
        public async Task GivenFhirServer_WhenPatientDataIsExported_ThenExportedDataIsSameAsDataInFhirServer(string parametersKey)
        {
            // NOTE: Azurite or Azure Storage Explorer is required to run these tests locally.
            Uri contentLocation = _fixture.ExportTestCasesContentUrls[$"{nameof(GivenFhirServer_WhenPatientDataIsExported_ThenExportedDataIsSameAsDataInFhirServer)}-{parametersKey}"];

            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation, 15);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(_fixture.TestPatientCompartmentResources, dataFromExport, _outputHelper));
        }

        [Theory]
        [InlineData("since")]
        [InlineData("tag")]
        public async Task GivenFhirServer_WhenAllObservationAndPatientDataIsExported_ThenExportedDataIsSameAsDataInFhirServer(string parametersKey)
        {
            // NOTE: Azurite or Azure Storage Explorer is required to run these tests locally.
            string[] testResorceTypes = { "Observation", "Patient" };
            Uri contentLocation = _fixture.ExportTestCasesContentUrls[$"{nameof(GivenFhirServer_WhenAllObservationAndPatientDataIsExported_ThenExportedDataIsSameAsDataInFhirServer)}-{parametersKey}"];

            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation, 15);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            var expectedResources = _fixture.TestResources
                .Where(r => testResorceTypes.Contains(r.Key.resourceType))
                .ToDictionary(x => x.Key, x => x.Value);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(expectedResources, dataFromExport, _outputHelper));
        }

        [Theory]
        [InlineData("since")]
        [InlineData("tag")]
        public async Task GivenFhirServer_WhenPatientObservationDataIsExported_ThenExportedDataIsSameAsDataInFhirServer(string parametersKey)
        {
            // NOTE: Azurite or Azure Storage Explorer is required to run these tests locally.
            Uri contentLocation = _fixture.ExportTestCasesContentUrls[$"{nameof(GivenFhirServer_WhenPatientObservationDataIsExported_ThenExportedDataIsSameAsDataInFhirServer)}-{parametersKey}"];

            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation, 15);

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
            Uri contentLocation = _fixture.ExportTestCasesContentUrls[nameof(GivenFhirServer_WhenAllDataIsExportedToASpecificContainer_ThenExportedDataIsInTheSpecifiedContianer)];

            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation, 15);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(_fixture.TestResources, dataFromExport, _outputHelper));
            Assert.True(blobUris.All((url) => url.OriginalString.Contains(testContainer)));
        }

        // _tag filter cannot be used with history or deleted export. Using isParallel to test both SQL code paths.
        [Theory]
        [InlineData("_isParallel=true")]
        [InlineData("_isParallel=false")]
        public async Task GivenFhirServer_WhenDataIsExportedWithHistory_ThenExportedDataIsSameAsDataInFhirServer(string parallelQueryParam)
        {
            // NOTE: Azurite or Azure Storage Explorer is required to run these tests locally.
            Uri contentLocation = _fixture.ExportTestCasesContentUrls[$"{nameof(GivenFhirServer_WhenDataIsExportedWithHistory_ThenExportedDataIsSameAsDataInFhirServer)}-{parallelQueryParam}"];

            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation, 15);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(_fixture.TestResourcesWithHistory, dataFromExport, _outputHelper));
        }

        // _tag filter cannot be used with history or deleted export. Using isParallel to test both SQL code paths.
        [Theory]
        [InlineData("_isParallel=true")]
        [InlineData("_isParallel=false")]
        public async Task GivenFhirServer_WhenDataIsExportedWithSoftDeletes_ThenExportedDataIsSameAsDataInFhirServer(string parallelQueryParam)
        {
            // NOTE: Azurite or Azure Storage Explorer is required to run these tests locally.
            Uri contentLocation = _fixture.ExportTestCasesContentUrls[$"{nameof(GivenFhirServer_WhenDataIsExportedWithSoftDeletes_ThenExportedDataIsSameAsDataInFhirServer)}-{parallelQueryParam}"];

            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation, 15);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(_fixture.TestResourcesWithDeletes, dataFromExport, _outputHelper));
        }

        // _tag filter cannot be used with history or deleted export. Using isParallel to test both SQL code paths.
        [Theory]
        [InlineData("_isParallel=true")]
        [InlineData("_isParallel=false")]
        public async Task GivenFhirServer_WhenDataIsExportedWithHistoryAndSoftDeletes_ThenExportedDataIsSameAsDataInFhirServer(string parallelQueryParam)
        {
            // NOTE: Azurite or Azure Storage Explorer is required to run these tests locally.
            Uri contentLocation = _fixture.ExportTestCasesContentUrls[$"{nameof(GivenFhirServer_WhenDataIsExportedWithHistoryAndSoftDeletes_ThenExportedDataIsSameAsDataInFhirServer)}-{parallelQueryParam}"];

            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation, 15);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(_fixture.TestResourcesWithHistoryAndDeletes, dataFromExport, _outputHelper));
        }
    }
}
