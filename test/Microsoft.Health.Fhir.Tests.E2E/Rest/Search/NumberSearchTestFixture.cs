// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class NumberSearchTestFixture : SearchTestFixture
    {
        public IReadOnlyList<Immunization> Immunizations { get; private set; }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();

            Immunizations = await CreateResourcesAsync<Immunization>(
                i => i.Identifier,
                i => SetImmunization(i, 1),
                i => SetImmunization(i, 4),
                i => SetImmunization(i, 5),
                i => SetImmunization(i, 6),
                i => SetImmunization(i, 100));

            void SetImmunization(Immunization immunization, int doseSequence)
            {
                immunization.Patient = new ResourceReference("Patient/123");
                immunization.VaccineCode = new CodeableConcept("vaccine", "code");
                immunization.Status = Immunization.ImmunizationStatusCodes.Completed;
                immunization.NotGiven = false;
                immunization.PrimarySource = true;

                immunization.VaccinationProtocol = new List<Immunization.VaccinationProtocolComponent>()
                {
                    new Immunization.VaccinationProtocolComponent()
                    {
                        DoseSequence = doseSequence,
                    },
                };
            }
        }
    }
}
