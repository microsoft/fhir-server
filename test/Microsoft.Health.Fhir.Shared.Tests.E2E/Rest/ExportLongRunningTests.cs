// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    [Trait(Traits.Category, Categories.ExportLongRunning)]
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
            // NOTE: Azure Storage Emulator is required to run these tests locally.

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync();
            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation, timeToWaitInMinutes: 15);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Download all resources from fhir server
            Dictionary<(string resourceType, string resourceId), Resource> dataFromFhirServer =
                await ExportTestHelper.GetResourcesFromFhirServer(_testFhirClient, _testFhirClient.HttpClient.BaseAddress, _fhirJsonParser, _outputHelper);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(dataFromFhirServer, dataFromExport, _outputHelper));
        }

        [Fact]
        public async Task GivenFhirServer_WhenPatientDataIsExported_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // NOTE: Azure Storage Emulator is required to run these tests locally.

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync("Patient/");
            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation, timeToWaitInMinutes: 15);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Download resources from fhir server
            Uri address = new Uri(_testFhirClient.HttpClient.BaseAddress, "Patient/");
            Dictionary<(string resourceType, string resourceId), Resource> dataFromFhirServer = await ExportTestHelper.GetResourcesFromFhirServer(_testFhirClient, address, _fhirJsonParser, _outputHelper);

            Dictionary<(string resourceType, string resourceId), Resource> compartmentData = new Dictionary<(string resourceType, string resourceId), Resource>();
            foreach ((string resourceType, string resourceId) key in dataFromFhirServer.Keys)
            {
                address = new Uri(_testFhirClient.HttpClient.BaseAddress, "Patient/" + key.resourceId + "/*");

                // copies all the new values into the compartment data dictionary
                (await ExportTestHelper.GetResourcesFromFhirServer(_testFhirClient, address, _fhirJsonParser, _outputHelper))
                    .ToList()
                    .ForEach(x => compartmentData.TryAdd(x.Key, x.Value));
            }

            compartmentData.ToList().ForEach(x => dataFromFhirServer.TryAdd(x.Key, x.Value));
            dataFromFhirServer.Union(compartmentData);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(dataFromFhirServer, dataFromExport, _outputHelper));
        }

        [Fact]
        public async Task GivenFhirServer_WhenAllObservationAndPatientDataIsExported_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // NOTE: Azure Storage Emulator is required to run these tests locally.

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync(string.Empty, "_type=Observation,Patient");
            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation, timeToWaitInMinutes: 15);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Download resources from fhir server
            Uri address = new Uri(_testFhirClient.HttpClient.BaseAddress, "?_type=Observation,Patient");
            Dictionary<(string resourceType, string resourceId), Resource> dataFromFhirServer =
                await ExportTestHelper.GetResourcesFromFhirServer(_testFhirClient, address, _fhirJsonParser, _outputHelper);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(dataFromFhirServer, dataFromExport, _outputHelper));
        }

        [Fact]
        public async Task GivenFhirServer_WhenPatientObservationDataIsExported_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // NOTE: Azure Storage Emulator is required to run these tests locally.

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync("Patient/", "_type=Observation");
            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation, timeToWaitInMinutes: 15);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Download resources from fhir server
            Uri address = new Uri(_testFhirClient.HttpClient.BaseAddress, "Patient/");
            Dictionary<(string resourceType, string resourceId), Resource> patientData =
                await ExportTestHelper.GetResourcesFromFhirServer(_testFhirClient, address, _fhirJsonParser, _outputHelper);

            Dictionary<(string resourceType, string resourceId), Resource> compartmentData = new Dictionary<(string resourceType, string resourceId), Resource>();
            foreach ((string resourceType, string resourceId) key in patientData.Keys)
            {
                address = new Uri(_testFhirClient.HttpClient.BaseAddress, "Patient/" + key.resourceId + "/Observation");

                // copies all the new values into the compartment data dictionary
                (await ExportTestHelper.GetResourcesFromFhirServer(_testFhirClient, address, _fhirJsonParser, _outputHelper))
                    .ToList()
                    .ForEach(x => compartmentData.TryAdd(x.Key, x.Value));
            }

            compartmentData.ToList().ForEach(x => patientData.TryAdd(x.Key, x.Value));
            patientData.Union(compartmentData);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(compartmentData, dataFromExport, _outputHelper));
        }

        [Fact]
        public async Task GivenFhirServer_WhenAllDataIsExportedToASpecificContainer_ThenExportedDataIsInTheSpecifiedContianer()
        {
            // NOTE: Azure Storage Emulator is required to run these tests locally.

            string testContainer = "test-container";

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync(parameters: $"_container={testContainer}");
            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation, timeToWaitInMinutes: 15);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Download all resources from fhir server
            Dictionary<(string resourceType, string resourceId), Resource> dataFromFhirServer =
                await ExportTestHelper.GetResourcesFromFhirServer(_testFhirClient, _testFhirClient.HttpClient.BaseAddress, _fhirJsonParser, _outputHelper);

            // Assert both data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(dataFromFhirServer, dataFromExport, _outputHelper));
            Assert.True(blobUris.All((url) => url.OriginalString.Contains(testContainer)));
        }
    }
}
