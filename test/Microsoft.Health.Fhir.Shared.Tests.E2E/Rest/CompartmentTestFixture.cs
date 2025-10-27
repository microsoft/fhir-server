﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public class CompartmentTestFixture : HttpIntegrationTestFixture
    {
        public CompartmentTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        public Patient Patient { get; private set; }

        public Observation Observation { get; private set; }

        public Device Device { get; private set; }

        public Encounter Encounter { get; private set; }

        public Condition Condition { get; private set; }

        protected override async Task OnInitializedAsync()
        {
            // Create various resources.
            Device = await TestFhirClient.CreateAsync(Samples.GetJsonFhirSample<Device>("Device-d1"));

            Patient = await TestFhirClient.CreateAsync(Samples.GetJsonFhirSample<Patient>("Patient-f001"));

            string patientReference = $"Patient/{Patient.Id}";

            Observation observationToCreate = Samples.GetJsonFhirSample<Observation>("Observation-For-Patient-f001");

            observationToCreate.Subject.Reference = patientReference;

            observationToCreate.Device = new ResourceReference($"Device/{Device.Id}");

            Observation = await TestFhirClient.CreateAsync(observationToCreate);

            Encounter encounterToCreate = Samples.GetJsonFhirSample<Encounter>("Encounter-For-Patient-f001");

            encounterToCreate.Subject.Reference = patientReference;

            Encounter = await TestFhirClient.CreateAsync(encounterToCreate);

            Condition conditionToCreate = Samples.GetJsonFhirSample<Condition>("Condition-For-Patient-f001");

            conditionToCreate.Subject.Reference = patientReference;

            Condition = await TestFhirClient.CreateAsync(conditionToCreate);
        }
    }
}
