// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Xunit.Abstractions;
using FhirGroup = Hl7.Fhir.Model.Group;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    [Trait(Traits.Category, Categories.ExportDataValidation)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class ExportDataValidationTests : IClassFixture<ExportTestFixture>
    {
        private readonly TestFhirClient _testFhirClient;
        private readonly ITestOutputHelper _outputHelper;
        private readonly FhirJsonParser _fhirJsonParser;
        private readonly ExportTestFixture _fixture;

        public ExportDataValidationTests(ExportTestFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _testFhirClient = fixture.TestFhirClient;
            _outputHelper = testOutputHelper;
            _fhirJsonParser = new FhirJsonParser();
            _fixture = fixture;
        }

        [Fact]
        public async Task GivenFhirServer_WhenGroupDataIsExported_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // NOTE: Azurite is required to run these tests locally.

            // Add data for test
            var (dataInFhirServer, groupId) = await CreateGroupWithPatient(true);

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync($"Group/{groupId}/");
            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Assert both sets of data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(dataInFhirServer, dataFromExport, _outputHelper));
        }

        [Fact]
        public async Task GivenFhirServer_WhenGroupDataIsExportedWithTypeParameter_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // NOTE: Azurite is required to run these tests locally.

            // Add data for test
            var (dataInFhirServer, groupId) = await CreateGroupWithPatient(false);

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync($"Group/{groupId}/", "_type=RelatedPerson,Encounter");
            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromExport =
                await ExportTestHelper.DownloadBlobAndParse(blobUris, _fhirJsonParser, _outputHelper);

            // Assert both sets of data are equal
            Assert.True(ExportTestHelper.ValidateDataFromBothSources(dataInFhirServer, dataFromExport, _outputHelper));
        }

        [Fact]
        public async Task GivenFhirServer_WhenGroupDataWithNoMemberPatientIdIsExported_ThenNoDataIsExported()
        {
            // NOTE: Azurite is required to run these tests locally.

            // Add data for test
            string groupId = await CreateGroupWithoutPatientIds();

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync($"Group/{groupId}/");
            IList<Uri> blobUris = await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation);

            Assert.Empty(blobUris);

            async Task<string> CreateGroupWithoutPatientIds()
            {
                var group = new FhirGroup()
                {
                    Type = FhirGroup.GroupType.Person,
                    Actual = true,
                };

                var groupResponse = await _testFhirClient.CreateAsync(group);
                return groupResponse.Resource.Id;
            }
        }

        [Fact]
        public async Task GivenFhirServer_WhenDataIsExported_ThenExportTaskMetricsNotificationShouldBePosted()
        {
            // NOTE: Azurite is required to run these tests locally.

            if (!_fixture.IsUsingInProcTestServer)
            {
                // This test only works with the in-proc server with a customized metric handler.
                return;
            }

            // Clean notification before tests
            _fixture.MetricHandler.ResetCount();

            // Add data for test
            var (_, groupId) = await CreateGroupWithPatient(true);

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync($"Group/{groupId}/");
            await ExportTestHelper.CheckExportStatus(_testFhirClient, contentLocation);

            // Assert at least one notification handled.
            Assert.Single(_fixture.MetricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
        }

        private async Task<(Dictionary<(string resourceType, string resourceId, string versionId), Resource> serverData, string groupId)> CreateGroupWithPatient(bool includeAllResources)
        {
            // Add data for test
            var patient = new Patient();
            var patientResponse = await _testFhirClient.CreateAsync(patient);
            var patientId = patientResponse.Resource.Id;
            var patientVersionId = patientResponse.Resource.VersionId;

            var relative = new RelatedPerson()
            {
                Patient = new ResourceReference($"{KnownResourceTypes.Patient}/{patientId}"),
            };

            var relativeResponse = await _testFhirClient.CreateAsync(relative);
            var relativeId = relativeResponse.Resource.Id;
            var relativeVersionId = relativeResponse.Resource.VersionId;

            var encounter = new Encounter()
            {
                Status = Encounter.EncounterStatus.InProgress,
                Class = new Coding()
                {
                    Code = "test",
                },
                Subject = new ResourceReference($"{KnownResourceTypes.Patient}/{patientId}"),
            };

            var encounterResponse = await _testFhirClient.CreateAsync(encounter);
            var encounterId = encounterResponse.Resource.Id;
            var encounterVersionId = encounterResponse.Resource.VersionId;

            var observation = new Observation()
            {
                Status = ObservationStatus.Final,
                Code = new CodeableConcept()
                {
                    Coding = new List<Coding>()
                    {
                        new Coding()
                        {
                            Code = "test",
                        },
                    },
                },
                Subject = new ResourceReference($"{KnownResourceTypes.Patient}/{patientId}"),
            };

            var observationResponse = await _testFhirClient.CreateAsync(observation);
            var observationId = observationResponse.Resource.Id;
            var observationVersionId = observationResponse.Resource.VersionId;

            var group = new FhirGroup()
            {
                Type = FhirGroup.GroupType.Person,
                Actual = true,
                Member = new List<FhirGroup.MemberComponent>()
                {
                    new FhirGroup.MemberComponent()
                    {
                        Entity = new ResourceReference($"{KnownResourceTypes.Patient}/{patientId}"),
                    },
                },
            };

            var groupResponse = await _testFhirClient.CreateAsync(group);
            var groupId = groupResponse.Resource.Id;
            var groupVersionId = groupResponse.Resource.VersionId;

            var resourceDictionary = new Dictionary<(string resourceType, string resourceId, string versionId), Resource>();
            resourceDictionary.Add((KnownResourceTypes.RelatedPerson, relativeId, relativeVersionId), relativeResponse.Resource);
            resourceDictionary.Add((KnownResourceTypes.Encounter, encounterId, encounterVersionId), encounterResponse.Resource);

            if (includeAllResources)
            {
                resourceDictionary.Add((KnownResourceTypes.Patient, patientId, patientVersionId), patientResponse.Resource);
                resourceDictionary.Add((KnownResourceTypes.Observation, observationId, observationVersionId), observationResponse.Resource);
                resourceDictionary.Add((KnownResourceTypes.Group, groupId, groupVersionId), groupResponse.Resource);
            }

            return (resourceDictionary, groupId);
        }
    }
}
