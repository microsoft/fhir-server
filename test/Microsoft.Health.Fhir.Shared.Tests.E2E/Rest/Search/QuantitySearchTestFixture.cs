// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class QuantitySearchTestFixture : HttpIntegrationTestFixture
    {
        public QuantitySearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        public IReadOnlyList<Observation> Observations { get; private set; }

        protected override async Task OnInitializedAsync()
        {
            // Prepare the resources used for number search tests.
            await TestFhirClient.DeleteAllResources(ResourceType.Observation);

            Observations = await TestFhirClient.CreateResourcesAsync<Observation>(
                o => SetObservation(o, 0.002m, "unit1", "system1"),
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
