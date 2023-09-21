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

        public List<Resource> ExistingResources => NewImplicitVersionIdResources.Existing
            .Concat(ConflictingVersionResources.Existing)
            .Concat(ImportAndDeleteResources.Existing).ToList();

        public List<Resource> ImportResources => NewImplicitVersionIdResources.Import
            .Concat(ConflictingVersionResources.Import)
            .Concat(ImportAndDeleteResources.Import).ToList();

        // TODO - Add a test case for resources without version id or last updated.

        // TODO - Test when resources are already in the database.

        public string FixtureTag { get; } = Guid.NewGuid().ToString();

        protected override async Task OnInitializedAsync()
        {
            NewImplicitVersionIdResources = GetNewImplicitVersionIdResources();
            ConflictingVersionResources = GetConflictingVersionResources();
            ImportAndDeleteResources = GetImportAndDeleteResources();

            // Add resources for testing of import on existing resources.
            await SavePrerequisiteResourcesViaHttp(ExistingResources);

            // Execute import request under test.
            await ImportTestHelper.ImportToServerAsync(
                TestFhirClient,
                StorageAccount,
                ImportResources.ToArray());
        }

        private async Task SavePrerequisiteResourcesViaHttp(List<Resource> existingServerResources)
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

            return;
        }

        private (List<Resource> Existing, List<Resource> Import) GetNewImplicitVersionIdResources()
        {
            string sharedId = Guid.NewGuid().ToString("N");
            return (
                new()
                {
                    DefaultPatient(id: sharedId, lastUpdated: DateTimeOffset.UtcNow.AddMinutes(-1)),
                },
                new()
                {
                    PatientNoVersionOrLastUpdated(id: sharedId),
                });
        }

        private (List<Resource> Existing, List<Resource> Import) GetConflictingVersionResources()
        {
            string sharedId = Guid.NewGuid().ToString("N");
            return (
                new()
                {
                    DefaultPatient(id: sharedId, lastUpdated: DateTimeOffset.UtcNow.AddMinutes(-1), versionId: "1"),
                },
                new()
                {
                    DefaultPatient(id: sharedId, lastUpdated: DateTimeOffset.UtcNow, versionId: "1"),
                });
        }

        private (List<Resource> Existing, List<Resource> Import) GetImportAndDeleteResources()
        {
            string sharedId = Guid.NewGuid().ToString("N");
            return (
                new(),
                new()
                {
                    DefaultPatient(id: sharedId),
                    DefaultPatient(id: sharedId, lastUpdated: DateTimeOffset.UtcNow, versionId: "2"),
                });
        }

        private Patient PatientNoVersionOrLastUpdated(string id = null, bool deleted = false)
        {
            var rtn = new Patient()
            {
                Id = id ?? Guid.NewGuid().ToString("N"),
            }
            .AddTestTag(FixtureTag);

            if (deleted)
            {
                rtn.Meta.Extension = new List<Extension> { { new Extension(KnownFhirPaths.AzureSoftDeletedExtensionUrl, new FhirString("soft-deleted")) } };
            }

            return rtn;
        }

        private Patient PatientNoVersion(string id = null, DateTimeOffset? lastUpdated = null, bool deleted = false)
        {
            var rtn = PatientNoVersionOrLastUpdated(id: id, deleted: deleted);
            rtn.Meta.LastUpdated = lastUpdated ?? DateTimeOffset.UtcNow;
            return rtn;
        }

        private Patient PatientNoLastUpdated(string id = null, string versionId = null, bool deleted = false)
        {
            var rtn = PatientNoVersionOrLastUpdated(id: id, deleted: deleted);
            rtn.Meta.VersionId = versionId ?? "1";
            return rtn;
        }

        private Patient DefaultPatient(string id = null, DateTimeOffset? lastUpdated = null, string versionId = null, bool deleted = false)
        {
            var rtn = PatientNoVersionOrLastUpdated(id: id, deleted: deleted);
            rtn.Meta.LastUpdated = lastUpdated ?? DateTimeOffset.UtcNow;
            rtn.Meta.VersionId = versionId ?? "1";
            return rtn;
        }
    }
}
