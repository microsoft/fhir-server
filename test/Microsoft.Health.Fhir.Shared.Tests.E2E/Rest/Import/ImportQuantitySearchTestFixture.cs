// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    public class ImportQuantitySearchTestFixture : ImportTestFixture<StartupForImportTestProvider>
    {
        public ImportQuantitySearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        public IReadOnlyList<Observation> Observations { get; private set; }

        public string FixtureTag { get; } = Guid.NewGuid().ToString();

        protected override async Task OnInitializedAsync()
        {
            Observations = await ImportTestHelper.ImportToServerAsync<Observation>(
                TestFhirClient,
                StorageAccount,
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
                observation.AddTestTag(FixtureTag);
                observation.Value = new Quantity(quantity, unit, system);
            }
        }
    }
}
