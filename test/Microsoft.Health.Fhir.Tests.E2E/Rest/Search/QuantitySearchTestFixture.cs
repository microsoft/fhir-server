// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class QuantitySearchTestFixture : SearchTestFixture
    {
        public IReadOnlyList<Observation> Observations { get; private set; }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();

            Observations = await CreateResourcesAsync<Observation>(
                o => o.Identifier,
                o => SetObservation(o, 1.0m, "unit1", "system1"),
                o => SetObservation(o, 3.12m, "unit1", "system2"),
                o => SetObservation(o, 4.0m, "unit1", "system1"),
                o => SetObservation(o, 5.0m, "unit1", "system1"),
                o => SetObservation(o, 5.0m, "unit2", "system2"),
                o => SetObservation(o, 6.0m, "unit2", "system2"),
                o => SetObservation(o, 8.95m, "unit2", "system1"),
                o => SetObservation(o, 10.0m, "unit1", "system1"));

            void SetObservation(Observation observation, decimal quantity, string unit, string system)
            {
                observation.Code = new CodeableConcept("system", "code");
                observation.Status = ObservationStatus.Registered;

                observation.Value = new Quantity(quantity, unit, system);
            }
        }
    }
}
