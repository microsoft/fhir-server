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
    public class QuantitySearchTestFixture : HttpIntegrationTestFixture
    {
        public QuantitySearchTestFixture(DataStore dataStore, Format format, FhirVersion fhirVersion)
            : base(dataStore, format, fhirVersion)
        {
            // Prepare the resources used for number search tests.
            FhirClient.DeleteAllResources(KnownResourceTypes.Observation).Wait();

            Observations = FhirClient.CreateResourcesAsync(
                FhirClient.GetEmptyObservation,
                o => SetObservation(o, 1.0m, "unit1", "system1"),
                o => SetObservation(o, 3.12m, "unit1", "system2"),
                o => SetObservation(o, 4.0m, "unit1", "system1"),
                o => SetObservation(o, 5.0m, "unit1", "system1"),
                o => SetObservation(o, 5.0m, "unit2", "system2"),
                o => SetObservation(o, 6.0m, "unit2", "system2"),
                o => SetObservation(o, 8.95m, "unit2", "system1"),
                o => SetObservation(o, 10.0m, "unit1", "system1")).Result;

            ResourceElement SetObservation(ResourceElement observation, decimal quantity, string unit, string system)
            {
                observation = FhirClient.AddObservationCoding(observation, "system", "code");
                observation = FhirClient.UpdateObservationStatus(observation, "Registered");
                observation = FhirClient.UpdateObservationValueQuantity(observation, quantity, unit, system);

                return observation;
            }
        }

        public IReadOnlyList<ResourceElement> Observations { get; }
    }
}
