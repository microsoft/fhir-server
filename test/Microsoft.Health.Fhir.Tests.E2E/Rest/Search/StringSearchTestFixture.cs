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
    public class StringSearchTestFixture : HttpIntegrationTestFixture
    {
        public StringSearchTestFixture(DataStore dataStore, Format format, FhirVersion fhirVersion)
            : base(dataStore, format, fhirVersion)
        {
            // Prepare the resources used for string search tests.
            FhirClient.DeleteAllResources(KnownResourceTypes.Patient).Wait();

            Patients = FhirClient.CreateResourcesAsync(
                    FhirClient.GetDefaultPatient,
                    p => SetPatientInfo(p, "Seattle", "Smith"),
                    p => SetPatientInfo(p, "Portland", "Williams"),
                    p => SetPatientInfo(p, "Vancouver", "Anderson"))
                .Result;

            ResourceElement SetPatientInfo(ResourceElement patient, string city, string family)
            {
                patient = FhirClient.UpdatePatientAddressCity(patient, city);
                patient = FhirClient.UpdatePatientFamilyName(patient, family);

                return patient;
            }
        }

        public IReadOnlyList<ResourceElement> Patients { get; }
    }
}
