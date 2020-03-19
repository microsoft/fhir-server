// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class ReferenceSearchTestFixture : HttpIntegrationTestFixture
    {
        public ReferenceSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
            // Prepare the resources used for string search tests.
            FhirClient.DeleteAllResources(ResourceType.Patient).Wait();

            Patients = FhirClient.CreateResourcesAsync<Patient>(
                p => p.ManagingOrganization = new ResourceReference("Organization/123"),
                p => p.ManagingOrganization = new ResourceReference("Organization/abc"))
                .Result;
        }

        public IReadOnlyList<Patient> Patients { get; }
    }
}
