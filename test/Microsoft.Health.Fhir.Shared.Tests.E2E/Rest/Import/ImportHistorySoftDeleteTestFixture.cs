// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    public class ImportHistorySoftDeleteTestFixture : ImportTestFixture<StartupForImportTestProvider>
    {
        public ImportHistorySoftDeleteTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        public Dictionary<string, (List<Resource> Existing, List<Resource> Import)> TestResources { get; } = new();

        public string FixtureTag { get; } = Guid.NewGuid().ToString();

        protected override async Task OnInitializedAsync()
        {
            TestResources.Add("NewImplicitVersionId", GetNewImplicitVersionIdResources());
            TestResources.Add("ImportOverExistingVersionId", GetImportOverExistingVersionIdResources());
            TestResources.Add("ImportWithSameVersionId", GetImportWithSameVersionIdResources());
            TestResources.Add("ImportAndDeleteExplicitVersionUpdate", GetImportAndDeleteExplicitVersionUpdatedResources());
            TestResources.Add("ImportAndDeleteImplicitVersionUpdated", GetImportAndDeleteImplicitVersionExplicitUpdatedResources());

            // Save existing test resources to the server. Update test resources with the new version id/last updated.
            foreach (var resourceInfo in TestResources)
            {
                var existingResources = await SavePrerequisiteResourcesViaHttp(resourceInfo.Value.Existing);
                TestResources[resourceInfo.Key] = (existingResources, resourceInfo.Value.Import);

                foreach (var importResource in resourceInfo.Value.Import)
                {
                    importResource.AddTestTag(FixtureTag);
                }
            }

            // Execute import request under test.
            await ImportTestHelper.ImportToServerAsync(
                TestFhirClient,
                StorageAccount,
                TestResources.Values.SelectMany(x => x.Import).ToArray());
        }

        private async Task<List<Resource>> SavePrerequisiteResourcesViaHttp(List<Resource> existingServerResources)
        {
            Bundle existingResourceBundle = new()
            {
                Type = Bundle.BundleType.Transaction,
                Entry = existingServerResources.Select(r => new Bundle.EntryComponent
                {
                    Resource = r,
                    Request = new() { Method = Bundle.HTTPVerb.PUT, Url = $"/{r.TypeName}/{r.Id}" },
                }).ToList(),
            };

            var result = await TestFhirClient.PostBundleAsync(existingResourceBundle);

            return result.Resource.Entry.Select(_ => _.Resource).ToList();
        }

        private (List<Resource> Existing, List<Resource> Import) GetNewImplicitVersionIdResources()
        {
            string sharedId = Guid.NewGuid().ToString("N");
            return (
                new()
                {
                    CreateTestPatient(id: sharedId),
                },
                new()
                {
                    CreateTestPatient(id: sharedId, lastUpdated: DateTimeOffset.UtcNow.AddMinutes(1)),
                });
        }

        private (List<Resource> Existing, List<Resource> Import) GetImportOverExistingVersionIdResources()
        {
            string sharedId = Guid.NewGuid().ToString("N");
            return (
                new()
                {
                    CreateTestPatient(id: sharedId),
                },
                new()
                {
                    CreateTestPatient(id: sharedId, lastUpdated: DateTimeOffset.UtcNow.AddMinutes(1), versionId: "1"),
                });
        }

        private (List<Resource> Existing, List<Resource> Import) GetImportWithSameVersionIdResources()
        {
            string sharedId = Guid.NewGuid().ToString("N");
            return (
                new(),
                new()
                {
                    CreateTestPatient(id: sharedId, lastUpdated: DateTimeOffset.UtcNow, versionId: "1"),
                    CreateTestPatient(id: sharedId, lastUpdated: DateTimeOffset.UtcNow.AddMinutes(1), versionId: "1"),
                });
        }

        private (List<Resource> Existing, List<Resource> Import) GetImportAndDeleteExplicitVersionUpdatedResources()
        {
            List<string> sharedIds = new();

            for (int i = 0; i < 4; i++)
            {
                sharedIds.Add(Guid.NewGuid().ToString("N"));
            }

            return (
                new(),
                new()
                {
                    CreateTestPatient(id: sharedIds[0], lastUpdated: DateTimeOffset.UtcNow.AddSeconds(-1), versionId: "1"),
                    CreateTestPatient(id: sharedIds[0], lastUpdated: DateTimeOffset.UtcNow, versionId: "2", deleted: true),
                    CreateTestPatient(id: sharedIds[1], lastUpdated: DateTimeOffset.UtcNow, versionId: "2", deleted: true),
                    CreateTestPatient(id: sharedIds[1], lastUpdated: DateTimeOffset.UtcNow.AddSeconds(-1), versionId: "1"),

                    CreateTestPatient(id: sharedIds[2], lastUpdated: DateTimeOffset.UtcNow.AddSeconds(-1), versionId: "1", deleted: true),
                    CreateTestPatient(id: sharedIds[2], lastUpdated: DateTimeOffset.UtcNow, versionId: "2"),
                    CreateTestPatient(id: sharedIds[3], lastUpdated: DateTimeOffset.UtcNow, versionId: "2"),
                    CreateTestPatient(id: sharedIds[3], lastUpdated: DateTimeOffset.UtcNow.AddSeconds(-1), versionId: "1", deleted: true),
                });
        }

        private (List<Resource> Existing, List<Resource> Import) GetImportAndDeleteImplicitVersionExplicitUpdatedResources()
        {
            List<string> sharedIds = new();

            for (int i = 0; i < 12; i++)
            {
                sharedIds.Add(Guid.NewGuid().ToString("N"));
            }

            return (
                new(),
                new()
                {
                    CreateTestPatient(id: sharedIds[0], lastUpdated: DateTimeOffset.UtcNow.AddSeconds(-1)),
                    CreateTestPatient(id: sharedIds[0], lastUpdated: DateTimeOffset.UtcNow, deleted: true),
                    CreateTestPatient(id: sharedIds[1], lastUpdated: DateTimeOffset.UtcNow, deleted: true),
                    CreateTestPatient(id: sharedIds[1], lastUpdated: DateTimeOffset.UtcNow.AddSeconds(-1)),

                    CreateTestPatient(id: sharedIds[2], lastUpdated: DateTimeOffset.UtcNow.AddSeconds(-1), deleted: true),
                    CreateTestPatient(id: sharedIds[2], lastUpdated: DateTimeOffset.UtcNow),
                    CreateTestPatient(id: sharedIds[3], lastUpdated: DateTimeOffset.UtcNow),
                    CreateTestPatient(id: sharedIds[3], lastUpdated: DateTimeOffset.UtcNow.AddSeconds(-1), deleted: true),

                    CreateTestPatient(id: sharedIds[4], versionId: "1"),
                    CreateTestPatient(id: sharedIds[4], versionId: "2", deleted: true),
                    CreateTestPatient(id: sharedIds[5], versionId: "2", deleted: true),
                    CreateTestPatient(id: sharedIds[5], versionId: "1"),

                    CreateTestPatient(id: sharedIds[6], versionId: "1", deleted: true),
                    CreateTestPatient(id: sharedIds[6], versionId: "2"),
                    CreateTestPatient(id: sharedIds[7], versionId: "2"),
                    CreateTestPatient(id: sharedIds[7], versionId: "1", deleted: true),

                    CreateTestPatient(id: sharedIds[8]),
                    CreateTestPatient(id: sharedIds[8], deleted: true),
                    CreateTestPatient(id: sharedIds[9], deleted: true),
                    CreateTestPatient(id: sharedIds[9]),

                    CreateTestPatient(id: sharedIds[10], deleted: true),
                    CreateTestPatient(id: sharedIds[10]),
                    CreateTestPatient(id: sharedIds[11]),
                    CreateTestPatient(id: sharedIds[11], deleted: true),
                });
        }

        private Patient CreateTestPatient(string id = null, DateTimeOffset? lastUpdated = null, string versionId = null, bool deleted = false)
        {
            var rtn = new Patient()
            {
                Id = id ?? Guid.NewGuid().ToString("N"),
                Meta = new(),
            };

            if (lastUpdated is not null)
            {
                rtn.Meta = new Meta { LastUpdated = lastUpdated };
            }

            if (versionId is not null)
            {
                rtn.Meta.VersionId = versionId;
            }

            if (deleted)
            {
                rtn.Meta.Extension = new List<Extension> { { new Extension(KnownFhirPaths.AzureSoftDeletedExtensionUrl, new FhirString("soft-deleted")) } };
            }

            return rtn;
        }
    }
}
