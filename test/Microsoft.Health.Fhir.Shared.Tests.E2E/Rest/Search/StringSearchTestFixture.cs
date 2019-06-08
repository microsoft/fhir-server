// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Web;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class StringSearchTestFixture : HttpIntegrationTestFixture<Startup>
    {
        public StringSearchTestFixture(DataStore dataStore, Format format)
            : base(dataStore, format)
        {
            // Prepare the resources used for string search tests.
            FhirClient.DeleteAllResources(ResourceType.Patient).Wait();

            Patients = FhirClient.CreateResourcesAsync<Patient>(
                p => SetPatientInfo(p, "Seattle", "Smith"),
                p => SetPatientInfo(p, "Portland", "Williams"),
                p => SetPatientInfo(p, "Vancouver", "Anderson"))
                .Result;

            void SetPatientInfo(Patient patient, string city, string family)
            {
                patient.Address = new List<Address>()
                {
                    new Address() { City = city },
                };

                patient.Name = new List<HumanName>()
                {
                    new HumanName() { Family = family },
                };
            }
        }

        public IReadOnlyList<Patient> Patients { get; }
    }
}
