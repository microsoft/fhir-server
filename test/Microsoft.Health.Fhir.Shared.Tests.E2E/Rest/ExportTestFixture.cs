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
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
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

        public Dictionary<(string resourceType, string resourceId, string versionId), Resource> TestResourcesWithHistory { get; } = new();

        public Dictionary<(string resourceType, string resourceId, string versionId), Resource> TestResources => TestResourcesWithHistory
            .GroupBy(entry => entry.Key.resourceId)
            .Select(group => group.OrderByDescending(entry => entry.Value.Meta.LastUpdated).First())
            .Where(entry => !entry.Value.Meta.Extension.Any(extension =>
                extension.Url == "http://azurehealthcareapis.com/data-extensions/deleted-state"
                && ((FhirString)extension.Value).Value == "soft-deleted"))
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        public string FixtureTag { get; } = Guid.NewGuid().ToString();

        public DateTime TestDataInsertionTime { get; } = DateTime.UtcNow;

        public string ExportTestResourcesQueryParameters => $"_type=Patient,Observation&_typeFilter=Patient%3F_tag%3D{FixtureTag},Observation%3F_tag%3D{FixtureTag}";

        protected override async Task OnInitializedAsync()
        {
            string NewGuidString() => Guid.NewGuid().ToString();

            void AddResourceToTestResources(Resource resource) =>
                TestResourcesWithHistory[(resource.TypeName, resource.Id, resource.VersionId)] = resource;

            Meta testDataMeta = new Meta()
            {
                Tag = new List<Coding>()
                {
                    new Coding("http://e2e-test", FixtureTag),
                },
            };

            List<(bool updateResource, bool deleteResource)> testResourceData = new()
            {
                (false, false),
                (false, true),
                (true, false),
                (true, true),
            };

            foreach (var d in testResourceData)
            {
                var patient = new Patient()
                {
                    Id = NewGuidString(),
                    Meta = testDataMeta,
                };

                var observation = new Observation()
                {
                    Id = NewGuidString(),
                    Meta = testDataMeta,
                    Status = ObservationStatus.Final,
                    Code = new CodeableConcept("http://loinc.org", "12345-6"),
                    Subject = new ResourceReference($"Patient/{patient.Id}"),
                };

                var patientResponse = await TestFhirClient.UpdateAsync(patient);
                var observationResponse = await TestFhirClient.UpdateAsync(observation);

                AddResourceToTestResources(patientResponse.Resource);
                AddResourceToTestResources(observationResponse.Resource);

                if (d.updateResource)
                {
                    patient = patientResponse.Resource.DeepCopy() as Patient;
                    patient.Name.Add(new HumanName() { Family = "Updated" });

                    observation = observationResponse.Resource.DeepCopy() as Observation;
                    observation.Code.Coding.Add(new Coding("http://loinc.org", "12345-7"));

                    patientResponse = await TestFhirClient.UpdateAsync(patient);
                    observationResponse = await TestFhirClient.UpdateAsync(observation);

                    AddResourceToTestResources(patientResponse.Resource);
                    AddResourceToTestResources(observationResponse.Resource);
                }

                if (d.deleteResource)
                {
                    await TestFhirClient.DeleteAsync($"Patient/{patient.Id}");
                    await TestFhirClient.DeleteAsync($"Observation/{observation.Id}");

                    // Since we don't get the version id back from the delete response, we need to pull
                    // this data from the server.
                    foreach (var resourceInfo in new List<(string Type, string Id)> { ("Patient", patient.Id), ("Observation", observation.Id) })
                    {
                        var allResourcesWithDeleted = await TestFhirClient.SearchAsync($"{resourceInfo.Type}/{resourceInfo.Id}/_history");
                        var deletedResource = allResourcesWithDeleted.Resource.Entry.OrderByDescending(x => x.Resource.Meta.LastUpdated).First().Resource;
                        deletedResource.Meta.Extension.Add(new Extension("http://azurehealthcareapis.com/data-extensions/deleted-state", new FhirString("soft-deleted")));
                        AddResourceToTestResources(deletedResource);
                    }
                }
            }
        }
    }
}
