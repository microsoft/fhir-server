// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Metric;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public class ExportTestFixture : HttpIntegrationTestFixture<StartupForExportTestProvider>
    {
        private MetricHandler _metricHandler;

        public ExportTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        public MetricHandler MetricHandler
        {
            get => _metricHandler ?? (_metricHandler = (MetricHandler)(TestFhirServer as InProcTestFhirServer)?.Server.Host.Services.GetRequiredService<INotificationHandler<ExportTaskMetricsNotification>>());
        }

        public Dictionary<(string resourceType, string resourceId, string versionId), Resource> TestResourcesWithHistoryAndDeletes { get; } = new();

        public Dictionary<(string resourceType, string resourceId, string versionId), Resource> TestResourcesWithHistory => TestResourcesWithHistoryAndDeletes
            .Where(entry => !entry.Value.Meta.Extension.Any(extension =>
                extension.Url == "http://azurehealthcareapis.com/data-extensions/deleted-state"
                && ((FhirString)extension.Value).Value == "soft-deleted"))
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        public Dictionary<(string resourceType, string resourceId, string versionId), Resource> TestResourcesWithDeletes => TestResourcesWithHistoryAndDeletes
            .GroupBy(entry => entry.Key.resourceId)
            .Select(group => group.OrderByDescending(entry => entry.Value.Meta.LastUpdated).First())
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        public Dictionary<(string resourceType, string resourceId, string versionId), Resource> TestResources =>
            TestResourcesWithHistory.Where(pair => TestResourcesWithDeletes.ContainsKey(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);

        public string FixtureTag { get; } = Guid.NewGuid().ToString();

        public DateTime TestDataInsertionTime { get; } = DateTime.UtcNow;

        public string ExportTestResourcesQueryParameters => $"_type=Patient,Observation&_typeFilter=Patient%3F_tag%3D{FixtureTag},Observation%3F_tag%3D{FixtureTag}";

        protected override async Task OnInitializedAsync()
        {
            void AddResourceToTestResources(Resource resource) =>
                TestResourcesWithHistoryAndDeletes[(resource.TypeName, resource.Id, resource.VersionId)] = resource;

            void AddResourcesToTestResources(List<Resource> resources) => resources.ForEach(AddResourceToTestResources);

            var testResourcesInfo = GenerateTestResources().Select(x => (x, false)).ToList();

            while (testResourcesInfo.Count > 0)
            {
                var testResourceResponse = await SaveResourceListToServer(testResourcesInfo);

                try
                {
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
                            Practitioner deletedResource = resource.DeepCopy() as Practitioner;
                            testResourcesInfo.Add((deletedResource, true));
                        }
                        else if (i % 4 == 1)
                        {
                            Practitioner updatedResource = resource.DeepCopy() as Practitioner;
                            updatedResource.Name.Add(new()
                            {
                                Given = updatedResource.Name.First().Given,
                                Family = $"UpdatedFromVersion{updatedResource.Meta.VersionId}",
                            });

                            testResourcesInfo.Add((updatedResource, false));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }

            Console.WriteLine($"Generated {TestResourcesWithHistoryAndDeletes.Count} resources.");
        }

        private List<Resource> GenerateTestResources(int numberOfResources = 100)
        {
            var resources = new List<Resource>();

            for (int i = 0; i < numberOfResources; i++)
            {
                resources.Add(new Practitioner
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Meta = new() { Tag = new List<Coding>() { new Coding("http://e2e-test", FixtureTag) } },
                    Active = true,
                    Name = new List<HumanName>() { new HumanName() { Family = $"Test{i}", Given = new List<string> { "Export", "History", "SoftDelete" } } },
                });
            }

            return resources;
        }

        private async System.Threading.Tasks.Task<List<Resource>> SaveResourceListToServer(List<(Resource resource, bool delete)> entries)
        {
            if (entries.Count > 500)
            {
                throw new ArgumentException("The number of resources to save must be less than or equal to 500.");
            }

            var bundle = new Bundle
            {
                Type = Bundle.BundleType.Transaction,
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

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new Exception("Could not save resources to server.");
            }

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
                    rtn.Add(deletedResource);
                }
            }

            return rtn;
        }

        /*
        private static List<Resource> GenerateTestResources(int numberOfPatients = 20)
        {
            string[] firstNames = { "John", "Jane", "Robert", "Emily", "Michael", "Sarah", "William", "Anna", "James", "Laura" };

            string[] lastNames = { "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis", "Rodriguez", "Martinez" };

            static string RandomBirthdate()
            {
                DateTime startDate = new DateTime(1970, 1, 1);
                int range = (DateTime.Today - startDate).Days;
                return startDate.AddDays(_random.Next(range)).ToString("yyyy-MM-dd");
            }

            string RandomLastName() => lastNames[_random.Next(lastNames.Length)];

            string RandomFirstName() => firstNames[_random.Next(firstNames.Length)];

            Encounter CreateEncounter(string patientId)
            {
                return new()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Meta = new() { Tag = new List<Coding>() { new Coding("http://e2e-test", FixtureTag) }},
                    Status = Encounter.EncounterStatus.Planned,
                    Subject = new ResourceReference($"Patient/{patientId}"),
                };
            }

            Observation CreateObservation(string patientId, string encounterId)
            {
                return new()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Meta = new() { Tag = new List<Coding>() { new Coding("http://e2e-test", FixtureTag) }},
                    Status = ObservationStatus.Preliminary,
                    Code = new CodeableConcept("http://loinc.org", "12345-6"),
                    Subject = new ResourceReference($"Patient/{patientId}"),
                    Encounter = new ResourceReference($"Encounter/{encounterId}"),
                };
            }

            var resources = new List<Resource>();

            for (int i = 0; i < numberOfPatients; i++)
            {
                var patient = new Patient()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Meta = new() { Tag = new List<Coding>() { new Coding("http://e2e-test", FixtureTag) }},
                    Active = true,
                    Name = new List<HumanName>() { new HumanName() { Family = RandomLastName(), Given = new List<string> { RandomFirstName() }} },
                    BirthDate = RandomBirthdate(),
                };
                resources.Add(patient);

                for (int j = 0; j < _random.Next(1, 5); j++)
                {
                    var encounter = CreateEncounter(patient.Id);
                    resources.Add(encounter);

                    for (int k = 0; k < _random.Next(1, 5); k++)
                    {
                        var observation = CreateObservation(patient.Id, encounter.Id);
                        resources.Add(observation);

                        while (observation.Id.GetHashCode() % 4 != 0)
                        {
                            observation = observation.DeepCopy() as Observation;
                            observation.Code.Coding.Add(new Coding("http://loinc.org", "12345-1"));
                            resources.Add(observation);
                        }
                    }

                    while (encounter.Id.GetHashCode() % 4 != 0)
                    {
                        encounter = encounter.DeepCopy() as Encounter;
                        encounter.ClassHistory.Add(new Encounter.ClassHistoryComponent() { Class = new Coding("http://hl7.org/fhir/v3/ActCode", "EMER") });
                        resources.Add(encounter);
                    }
                }
            }

            return resources;
        }
        */
    }
}
