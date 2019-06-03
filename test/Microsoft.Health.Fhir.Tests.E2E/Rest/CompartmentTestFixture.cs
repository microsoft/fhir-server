// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public class CompartmentTestFixture : HttpIntegrationTestFixture, IAsyncLifetime
    {
        public CompartmentTestFixture(DataStore dataStore, Format format, FhirVersion fhirVersion)
            : base(dataStore, format, fhirVersion)
        {
        }

        public ResourceElement Patient { get; private set; }

        public ResourceElement Observation { get; private set; }

        public ResourceElement Device { get; private set; }

        public ResourceElement Encounter { get; private set; }

        public ResourceElement Condition { get; private set; }

        public async Task InitializeAsync()
        {
            // Create various resources.
            Patient = (await FhirClient.CreateAsync(FhirClient.GetJsonSample("Patient-f001"))).Resource;

            string patientReference = $"Patient/{Patient.Id}";

            Device = (await FhirClient.CreateAsync(FhirClient.GetJsonSample("Device-d1"))).Resource;

            ResourceElement observationToCreate = FhirClient.GetJsonSample("Observation-For-Patient-f001");

            observationToCreate = FhirClient.UpdateObservationSubject(observationToCreate, patientReference);

            observationToCreate = FhirClient.UpdateObservationDevice(observationToCreate, $"Device/{Device.Id}");

            Observation = (await FhirClient.CreateAsync(observationToCreate)).Resource;

            ResourceElement encounterToCreate = FhirClient.GetJsonSample("Encounter-For-Patient-f001");

            encounterToCreate = FhirClient.UpdateEncounterSubject(encounterToCreate, patientReference);

            Encounter = (await FhirClient.CreateAsync(encounterToCreate)).Resource;

            ResourceElement conditionToCreate = FhirClient.GetJsonSample("Condition-For-Patient-f001");

            conditionToCreate = FhirClient.UpdateConditionSubject(conditionToCreate, patientReference);

            Condition = (await FhirClient.CreateAsync(conditionToCreate)).Resource;
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }
    }
}
