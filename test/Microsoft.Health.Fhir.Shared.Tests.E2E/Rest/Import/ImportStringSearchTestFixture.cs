// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    public class ImportReferenceSearchTestFixture : ImportTestFixture<StartupForImportReferenceSearchTestProvider>
    {
        public ImportReferenceSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        public IReadOnlyList<Patient> Patients { get; private set; }

        public string FixtureTag { get; } = Guid.NewGuid().ToString();

        protected override async Task OnInitializedAsync()
        {
            await TestFhirClient.DeleteAllResources(ResourceType.Patient);

            Patients = await ImportTestHelper.ImportToServerAsync<Patient>(
                TestFhirClient,
                CloudStorageAccount,
                p => SetPatientInfo(p, "Seattle", "Smith", given: "Bea"),
                p => SetPatientInfo(p, "Portland", "Williams"),
                p => SetPatientInfo(p, "Vancouver", "Anderson"),
                p => SetPatientInfo(p, LongString, "Murphy"),
                p => SetPatientInfo(p, "Montreal", "Richard", given: "Bea"),
                p => SetPatientInfo(p, "New York", "Muller"),
                p => SetPatientInfo(p, "Portland", "Müller"),
                p => SetPatientInfo(p, "Moscow", "Richard,Muller"));

            void SetPatientInfo(Patient patient, string city, string family, string given = null)
            {
                patient.Address = new List<Address>()
                {
                    new Address { City = city },
                };

                patient.Name = new List<HumanName>()
                {
                    new HumanName { Family = family, Given = new[] { given } },
                };

                patient.Meta = new Meta();
                patient.Meta.Tag.Add(new Coding("http://e2e-test", FixtureTag));
            }
        }

        protected override async Task OnDisposedAsync()
        {
            await TestFhirClient.DeleteAllResources(ResourceType.Patient);
        }
    }
}
