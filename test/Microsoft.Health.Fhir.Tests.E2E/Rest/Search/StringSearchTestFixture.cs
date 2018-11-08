// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class StringSearchTestFixture : SearchTestFixture
    {
        public IReadOnlyList<Patient> Patients { get; private set; }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();

            Patients = await CreateResourcesAsync<Patient>(
                p => p.Identifier,
                p => SetPatientInfo(p, "Seattle", "Smith"),
                p => SetPatientInfo(p, "Portland", "Williams"),
                p => SetPatientInfo(p, "Vancouver", "Anderson"));

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
    }
}
