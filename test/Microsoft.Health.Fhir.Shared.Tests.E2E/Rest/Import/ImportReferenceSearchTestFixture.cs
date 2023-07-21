// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    public class ImportReferenceSearchTestFixture : ImportTestFixture<StartupForImportTestProvider>
    {
        public ImportReferenceSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        public string FixtureTag { get; } = Guid.NewGuid().ToString();

        public IReadOnlyList<Patient> Patients { get; private set; }

        protected override async Task OnInitializedAsync()
        {
            Patients = await ImportTestHelper.ImportToServerAsync<Patient>(
                TestFhirClient,
                StorageAccount,
                p => p.AddTestTag(FixtureTag).ManagingOrganization = new ResourceReference("Organization/123"),
                p => p.AddTestTag(FixtureTag).ManagingOrganization = new ResourceReference("Organization/abc"),
                p => p.AddTestTag(FixtureTag).ManagingOrganization = new ResourceReference("ijk"), // type not specified, but known constrained to be Organization
                p => p.AddTestTag(FixtureTag).GeneralPractitioner = new List<ResourceReference> { new ResourceReference("Practitioner/p1") },
                p => p.AddTestTag(FixtureTag).GeneralPractitioner = new List<ResourceReference> { new ResourceReference("p2") }); // type not specified and not known because it could be Practitioner, Organization, or PractitionerRole
        }
    }
}
