// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DotLiquid.Util;
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

        public (List<Resource> Existing, List<Resource> Import) NewImplicitVersionIdResources { get; private set; }

        public (List<Resource> Existing, List<Resource> Import) ConflictingVersionResources { get; private set; }

        public (List<Resource> Existing, List<Resource> Import) ImportAndDeleteResources { get; private set; }

        /*
        public List<Resource> ExistingResources => NewImplicitVersionIdResources.Existing
            .Concat(ConflictingVersionResources.Existing)
            .Concat(ImportAndDeleteResources.Existing).ToList();

        public List<Resource> ImportResources => NewImplicitVersionIdResources.Import
            .Concat(ConflictingVersionResources.Import)
            .Concat(ImportAndDeleteResources.Import).ToList();
        */
        public List<Resource> ExistingResources => ConflictingVersionResources.Existing;

        public List<Resource> ImportResources => ConflictingVersionResources.Import;

        // TODO - Add a test case for resources without version id or last updated.

        // TODO - Test when resources are already in the database.

        public string FixtureTag { get; } = Guid.NewGuid().ToString();

        protected override async Task OnInitializedAsync()
        {
            // NewImplicitVersionIdResources = GetNewImplicitVersionIdResources();
            var conflictingVersionResources = GetConflictingVersionResources();
            var existingConflictingVersionResources = await SavePrerequisiteResourcesViaHttp(conflictingVersionResources.Existing);
            ConflictingVersionResources = (existingConflictingVersionResources, conflictingVersionResources.Import);

            // ImportAndDeleteResources = GetImportAndDeleteResources();

            // Add resources for testing of import on existing resources.
            await SavePrerequisiteResourcesViaHttp(ExistingResources);

            // Execute import request under test.
            await ImportTestHelper.ImportToServerAsync(
                TestFhirClient,
                StorageAccount,
                ImportResources.ToArray());
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
                    CreateTestPatient(id: sharedId, lastUpdated: DateTimeOffset.UtcNow.AddMinutes(-1)),
                },
                new()
                {
                    CreateTestPatient(id: sharedId, lastUpdated: DateTimeOffset.UtcNow),
                });
        }

        private (List<Resource> Existing, List<Resource> Import) GetConflictingVersionResources()
        {
            string sharedId = Guid.NewGuid().ToString("N");
            return (
                new()
                {
                    CreateTestPatient(id: sharedId, lastUpdated: DateTimeOffset.UtcNow.AddMinutes(-1), versionId: "1"),
                },
                new()
                {
                    CreateTestPatient(id: sharedId, lastUpdated: DateTimeOffset.UtcNow, versionId: "1"),
                });
        }

        private (List<Resource> Existing, List<Resource> Import) GetImportAndDeleteResources()
        {
            string explicitVersionUpdatedGuid = Guid.NewGuid().ToString("N");
            string implicitVersionExplicitUpdatedGuid = Guid.NewGuid().ToString("N");
            string explicitVersionImplicitUpdatedGuid = Guid.NewGuid().ToString("N");
            string implicitVersionUpdatedGuid = Guid.NewGuid().ToString("N");

            return (
                new(),
                new()
                {
                    // The order of these is important for the test. Indexes 1, 3, 5 are expected to be available via search w/o history.
                    CreateTestPatient(id: explicitVersionUpdatedGuid, lastUpdated: DateTimeOffset.UtcNow.AddSeconds(-1), versionId: "1"),
                    CreateTestPatient(id: explicitVersionUpdatedGuid, lastUpdated: DateTimeOffset.UtcNow, versionId: "2", deleted: true),
                    CreateTestPatient(id: implicitVersionExplicitUpdatedGuid, lastUpdated: DateTimeOffset.UtcNow.AddSeconds(-1)),
                    CreateTestPatient(id: implicitVersionExplicitUpdatedGuid, lastUpdated: DateTimeOffset.UtcNow, deleted: true),
                    CreateTestPatient(id: explicitVersionImplicitUpdatedGuid, versionId: "1"),
                    CreateTestPatient(id: explicitVersionImplicitUpdatedGuid, versionId: "2", deleted: true),
                    CreateTestPatient(id: implicitVersionUpdatedGuid),
                    CreateTestPatient(id: implicitVersionUpdatedGuid, deleted: true),
                });
        }

        private Patient CreateTestPatient(string id = null, DateTimeOffset? lastUpdated = null, string versionId = null, bool deleted = false)
        {
            var rtn = new Patient()
            {
                Id = id ?? Guid.NewGuid().ToString("N"),
            }
            .AddTestTag(FixtureTag);

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
