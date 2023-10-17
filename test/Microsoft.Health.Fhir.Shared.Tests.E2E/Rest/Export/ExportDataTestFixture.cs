﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Antlr4.Runtime.Atn;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Metric;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Export
{
    public class ExportDataTestFixture : HttpIntegrationTestFixture<StartupForExportTestProvider>
    {
        private MetricHandler _metricHandler;

        public ExportDataTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
            DataStore = dataStore;
        }

        public MetricHandler MetricHandler
        {
            get => _metricHandler ?? (_metricHandler = (MetricHandler)(TestFhirServer as InProcTestFhirServer)?.Server.Host.Services.GetRequiredService<INotificationHandler<ExportTaskMetricsNotification>>());
        }

        internal DataStore DataStore { get; private set; }

        internal Dictionary<(string resourceType, string resourceId, string versionId), Resource> TestResourcesWithHistoryAndDeletes { get; } = new();

        internal Dictionary<(string resourceType, string resourceId, string versionId), Resource> TestResourcesWithHistory => TestResourcesWithHistoryAndDeletes
            .Where(entry => !entry.Value.Meta.Extension.Any(extension =>
                extension.Url == "http://azurehealthcareapis.com/data-extensions/deleted-state"
                && ((FhirString)extension.Value).Value == "soft-deleted"))
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        internal Dictionary<(string resourceType, string resourceId, string versionId), Resource> TestResourcesWithDeletes => TestResourcesWithHistoryAndDeletes
            .GroupBy(entry => entry.Key.resourceId)
            .Select(group => group.OrderByDescending(entry => entry.Value.Meta.LastUpdated).First())
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        internal Dictionary<(string resourceType, string resourceId, string versionId), Resource> TestResources =>
            TestResourcesWithHistory.Where(pair => TestResourcesWithDeletes.ContainsKey(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);

        // If the patient is deleted but the child resources are not, they should not be returned in patient centric exports.
        internal Dictionary<(string resourceType, string resourceId, string versionId), Resource> TestPatientCompartmentResources => TestResources
            .Where(x => x.Key.resourceType != "Encounter" || TestResources.Keys.Any(pat => pat.resourceType == "Patient" && pat.resourceId == (x.Value as Encounter).Subject.Reference.Split("/")[1]))
            .Where(x => x.Key.resourceType != "Observation" || TestResources.Keys.Any(pat => pat.resourceType == "Patient" && pat.resourceId == (x.Value as Observation).Subject.Reference.Split("/")[1]))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        internal Dictionary<string, Uri> ExportTestCasesContentUrls { get; } = new();

        internal string FixtureTag { get; } = Guid.NewGuid().ToString();

        internal DateTime TestDataInsertionTime { get; } = DateTime.UtcNow;

        internal string ExportTestFilterQueryParameters(params string[] uniqueResourceTypes)
        {
            if (uniqueResourceTypes.Length == 0)
            {
                uniqueResourceTypes = TestResourcesWithHistoryAndDeletes.Keys.Select(x => x.resourceType).Distinct().ToArray();
            }

            var typeFilterPart = string.Join(',', uniqueResourceTypes.Select(rt => $"{rt}%3F_tag%3D{FixtureTag}"));

            return $"_type={string.Join(',', uniqueResourceTypes)}&_typeFilter={typeFilterPart}";
        }

        protected override async Task OnInitializedAsync()
        {
            await SaveTestResourcesToServer();

            await StartTestExportOperations();
        }

        private async Task StartTestExportOperations()
        {
            // Shared Parameters
            var uniqueFixtureResources = string.Join(',', TestResourcesWithHistoryAndDeletes.Keys.Select(x => x.resourceType).Distinct());
            string sinceAndTypeExportParameters = $"_since={TestDataInsertionTime:o}&_type={uniqueFixtureResources}";

            // All data export tests
            ExportTestCasesContentUrls.Add(
                $"{nameof(ExportDataTests.GivenFhirServer_WhenAllDataIsExported_ThenExportedDataIsSameAsDataInFhirServer)}-since",
                await TestFhirClient.ExportAsync(parameters: sinceAndTypeExportParameters));

            ExportTestCasesContentUrls.Add(
                $"{nameof(ExportDataTests.GivenFhirServer_WhenAllDataIsExported_ThenExportedDataIsSameAsDataInFhirServer)}-tag",
                await TestFhirClient.ExportAsync(parameters: ExportTestFilterQueryParameters()));

            // Patient centric export tests
            ExportTestCasesContentUrls.Add(
                $"{nameof(ExportDataTests.GivenFhirServer_WhenPatientDataIsExported_ThenExportedDataIsSameAsDataInFhirServer)}-since",
                await TestFhirClient.ExportAsync(path: "Patient/", parameters: sinceAndTypeExportParameters));

            ExportTestCasesContentUrls.Add(
                $"{nameof(ExportDataTests.GivenFhirServer_WhenPatientDataIsExported_ThenExportedDataIsSameAsDataInFhirServer)}-tag",
                await TestFhirClient.ExportAsync(path: "Patient/", parameters: ExportTestFilterQueryParameters()));

            // Patient/Observation resource system export tests
            string[] testResorceTypes = { "Observation", "Patient" };
            ExportTestCasesContentUrls.Add(
                $"{nameof(ExportDataTests.GivenFhirServer_WhenAllObservationAndPatientDataIsExported_ThenExportedDataIsSameAsDataInFhirServer)}-since",
                await TestFhirClient.ExportAsync(parameters: $"_since={TestDataInsertionTime:o}&_type={string.Join(',', testResorceTypes)}"));

            ExportTestCasesContentUrls.Add(
                $"{nameof(ExportDataTests.GivenFhirServer_WhenAllObservationAndPatientDataIsExported_ThenExportedDataIsSameAsDataInFhirServer)}-tag",
                await TestFhirClient.ExportAsync(parameters: ExportTestFilterQueryParameters(testResorceTypes)));

            // Patient entric Observation resource Patient export tests
            ExportTestCasesContentUrls.Add(
                $"{nameof(ExportDataTests.GivenFhirServer_WhenPatientObservationDataIsExported_ThenExportedDataIsSameAsDataInFhirServer)}-since",
                await TestFhirClient.ExportAsync(path: "Patient/", parameters: $"_since={TestDataInsertionTime:o}&_type=Observation"));

            ExportTestCasesContentUrls.Add(
                $"{nameof(ExportDataTests.GivenFhirServer_WhenPatientObservationDataIsExported_ThenExportedDataIsSameAsDataInFhirServer)}-tag",
                await TestFhirClient.ExportAsync(path: "Patient/", parameters: ExportTestFilterQueryParameters("Observation")));

            // Export to specific container
            string testContainer = "test-container";
            ExportTestCasesContentUrls.Add(
                nameof(ExportDataTests.GivenFhirServer_WhenAllDataIsExportedToASpecificContainer_ThenExportedDataIsInTheSpecifiedContianer),
                await TestFhirClient.ExportAsync(parameters: $"_container={testContainer}&{ExportTestFilterQueryParameters()}"));

            // Export with history only
            ExportTestCasesContentUrls.Add(
                $"{nameof(ExportDataTests.GivenFhirServer_WhenDataIsExportedWithHistory_ThenExportedDataIsSameAsDataInFhirServer)}-_isParallel=true",
                await TestFhirClient.ExportAsync(parameters: $"_since={TestDataInsertionTime:O}&_type={uniqueFixtureResources}&includeAssociatedData=_history&_isParallel=true"));

            ExportTestCasesContentUrls.Add(
                $"{nameof(ExportDataTests.GivenFhirServer_WhenDataIsExportedWithHistory_ThenExportedDataIsSameAsDataInFhirServer)}-_isParallel=false",
                await TestFhirClient.ExportAsync(parameters: $"_since={TestDataInsertionTime:O}&_type={uniqueFixtureResources}&includeAssociatedData=_history&_isParallel=false"));

            // Export with delete only
            ExportTestCasesContentUrls.Add(
                $"{nameof(ExportDataTests.GivenFhirServer_WhenDataIsExportedWithSoftDeletes_ThenExportedDataIsSameAsDataInFhirServer)}-_isParallel=true",
                await TestFhirClient.ExportAsync(parameters: $"_since={TestDataInsertionTime:O}&_type={uniqueFixtureResources}&includeAssociatedData=_deleted&_isParallel=true"));

            ExportTestCasesContentUrls.Add(
                $"{nameof(ExportDataTests.GivenFhirServer_WhenDataIsExportedWithSoftDeletes_ThenExportedDataIsSameAsDataInFhirServer)}-_isParallel=false",
                await TestFhirClient.ExportAsync(parameters: $"_since={TestDataInsertionTime:O}&_type={uniqueFixtureResources}&includeAssociatedData=_deleted&_isParallel=false"));

            // Export with history and deletes
            ExportTestCasesContentUrls.Add(
                $"{nameof(ExportDataTests.GivenFhirServer_WhenDataIsExportedWithHistoryAndSoftDeletes_ThenExportedDataIsSameAsDataInFhirServer)}-_isParallel=true",
                await TestFhirClient.ExportAsync(parameters: $"_since={TestDataInsertionTime:O}&_type={uniqueFixtureResources}&includeAssociatedData=_history,_deleted&_isParallel=true"));

            ExportTestCasesContentUrls.Add(
                $"{nameof(ExportDataTests.GivenFhirServer_WhenDataIsExportedWithHistoryAndSoftDeletes_ThenExportedDataIsSameAsDataInFhirServer)}-_isParallel=false",
                await TestFhirClient.ExportAsync(parameters: $"_since={TestDataInsertionTime:O}&_type={uniqueFixtureResources}&includeAssociatedData=_history,_deleted&_isParallel=false"));
        }

        private async Task SaveTestResourcesToServer()
        {
            void AddResourceToTestResources(Resource resource) =>
                TestResourcesWithHistoryAndDeletes[(resource.TypeName, resource.Id, resource.VersionId)] = resource;

            void AddResourcesToTestResources(List<Resource> resources) => resources.ForEach(AddResourceToTestResources);

            var testResourcesInfo = GenerateTestResources().Select(x => (x, false)).ToList();

            while (testResourcesInfo.Count > 0)
            {
                var testResourceResponse = await SaveResourceListToServer(testResourcesInfo);

                AddResourcesToTestResources(testResourceResponse);

                testResourcesInfo = new();

                for (int i = 0; i < testResourceResponse.Count; i++)
                {
                    var resource = testResourceResponse[i];

                    if (resource.Meta.Extension.Any(x => x.Url == KnownFhirPaths.AzureSoftDeletedExtensionUrl))
                    {
                        // Skip already deleted resources for now.
                        // TODO - add un-deletes in here.
                        continue;
                    }
                    else if (i % 10 == 0)
                    {
                        testResourcesInfo.Add((resource.DeepCopy() as Resource, true));
                    }
                    else if (i % 4 == 1)
                    {
                        Resource updatedResource = resource.DeepCopy() as Resource;

                        if (updatedResource is Patient)
                        {
                            Patient updatedPatient = updatedResource as Patient;
                            updatedPatient.Name.Add(new()
                            {
                                Given = updatedPatient.Name.First().Given,
                                Family = $"UpdatedFromVersion{updatedResource.Meta.VersionId}",
                            });
                            testResourcesInfo.Add((updatedPatient, false));
                        }

                        if (updatedResource is Encounter)
                        {
                            Encounter updatedEncounter = updatedResource as Encounter;
                            updatedEncounter.Type.Add(new CodeableConcept("http://e2e-test", $"UpdatedFromVersion{updatedResource.Meta.VersionId}"));
                            testResourcesInfo.Add((updatedEncounter, false));
                        }

                        if (updatedResource is Observation)
                        {
                            Observation updatedObservation = updatedResource as Observation;
                            updatedObservation.Category.Add(new CodeableConcept("http://e2e-test", $"UpdatedFromVersion{updatedResource.Meta.VersionId}"));
                            testResourcesInfo.Add((updatedObservation, false));
                        }
                    }
                }
            }
        }

        private async System.Threading.Tasks.Task<List<Resource>> SaveResourceListToServer(List<(Resource resource, bool delete)> entries)
        {
            if (entries.Count > 500)
            {
                throw new ArgumentException("The number of resources to save must be less than or equal to 500.");
            }

            var bundle = new Bundle
            {
                Type = Bundle.BundleType.Batch,
                Entry = new List<Bundle.EntryComponent>(),
            };

            foreach (var entry in entries)
            {
                bundle.Entry.Add(new Bundle.EntryComponent
                {
                    Resource = entry.resource,
                    Request = new Bundle.RequestComponent
                    {
                        Method = entry.delete ? Bundle.HTTPVerb.DELETE : Bundle.HTTPVerb.PUT,
                        Url = $"{entry.resource.TypeName}/{entry.resource.Id}",
                    },
                    FullUrl = $"{TestFhirClient.HttpClient.BaseAddress}{entry.resource.TypeName}/{entry.resource.Id}",
                });
            }

            FhirResponse<Bundle> response = await TestFhirClient.PostBundleAsync(bundle);

            response.Response.EnsureSuccessStatusCode();

            List<Resource> rtn = new();

            for (int i = 0; i < response.Resource.Entry.Count; i++)
            {
                var inputResource = entries[i].resource;
                var responseEntry = response.Resource.Entry[i];

                if (responseEntry.Resource is not null)
                {
                    rtn.Add(responseEntry.Resource);
                }
                else
                {
                    var allResourcesWithDeleted = await TestFhirClient.SearchAsync($"{inputResource.TypeName}/{inputResource.Id}/_history");
                    var deletedResource = allResourcesWithDeleted.Resource.Entry.OrderByDescending(x => x.Resource.Meta.LastUpdated).First().Resource;
                    deletedResource.Meta.Extension.Add(new Extension(KnownFhirPaths.AzureSoftDeletedExtensionUrl, new FhirString("soft-deleted")));

                    // The history endpoint does not return the version id in the resource, so we need to parse it from the etag.
                    var etagVersionMatch = Regex.Match(responseEntry.Response.Etag, @"\d+");

                    if (deletedResource.Meta.VersionId is null && etagVersionMatch.Success)
                    {
                        deletedResource.Meta.VersionId = etagVersionMatch.Value;
                    }

                    rtn.Add(deletedResource);
                }
            }

            return rtn;
        }

        // 27 patients, 54 encounters, and, 108 observations.
        private List<Resource> GenerateTestResources(int numberOfPatients = 27, int numberOfEncountersPerPatient = 2, int numberOfObservationsPerEncounter = 2)
        {
            var resources = new List<Resource>();

            for (int i = 0; i < numberOfPatients; i++)
            {
                var patient = new Patient()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Meta = new() { Tag = new List<Coding>() { new Coding("http://e2e-test", FixtureTag) }},
                    Active = true,
                    Name = new List<HumanName>() { new HumanName() { Family = $"Test{i}", Given = new List<string> { "Export", "History", "SoftDelete" } } },
                };
                resources.Add(patient);

                for (int j = 0; j < numberOfEncountersPerPatient; j++)
                {
                    Encounter encounter = new()
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Meta = new() { Tag = new List<Coding>() { new Coding("http://e2e-test", FixtureTag) } },
                        Status = Encounter.EncounterStatus.Planned,
                        Type = new() { new CodeableConcept("http://e2e-test", $"Test{i}") },
                        Class = new Coding("http://e2e-test", $"Test{i}"),
                        Subject = new ResourceReference($"Patient/{patient.Id}"),
                    };
                    resources.Add(encounter);

                    for (int k = 0; k < numberOfObservationsPerEncounter; k++)
                    {
                        Observation observation = new()
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Meta = new() { Tag = new List<Coding>() { new Coding("http://e2e-test", FixtureTag) } },
                            Status = ObservationStatus.Preliminary,
                            Category = new() { new CodeableConcept("http://e2e-test", $"Test{i}") },
                            Code = new CodeableConcept("http://e2e-test", $"Test{i}"),
                            Subject = new ResourceReference($"Patient/{patient.Id}"),

                            // Encounter = new ResourceReference($"Encounter/{encounter.Id}"),
                        };
                        resources.Add(observation);
                    }
                }
            }

            return resources;
        }
    }
}
