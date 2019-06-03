// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class ReferenceSearchTestFixture : HttpIntegrationTestFixture
    {
        public ReferenceSearchTestFixture(DataStore dataStore, Format format, FhirVersion fhirVersion)
            : base(dataStore, format, fhirVersion)
        {
            // Prepare the resources used for string search tests.
            FhirClient.DeleteAllResources("Patient").Wait();

            Patients = FhirClient.CreateResourcesAsync(
                    FhirClient.GetDefaultPatient,
                    p => SetPatient(p, "Organization/123"),
                    p => SetPatient(p, "Organization/abc"))
                .Result;

            ResourceElement SetPatient(ResourceElement patient, string managingOrganization)
            {
                patient = FhirClient.UpdatePatientManagingOrganization(patient, managingOrganization);

                return patient;
            }
        }

        public IReadOnlyList<ResourceElement> Patients { get; }
    }
}
