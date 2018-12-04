// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Web;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public class CompartmentTestFixture : HttpIntegrationTestFixture<Startup>, IAsyncLifetime
    {
        public Patient Patient { get; private set; }

        public Observation Observation { get; private set; }

        public Device Device { get; private set; }

        public DeviceComponent DeviceComponent { get; private set; }

        public Encounter Encounter { get; private set; }

        public Condition Condition { get; private set; }

        public async Task InitializeAsync()
        {
            // Create various resources.
            Patient = await FhirClient.CreateAsync(Samples.GetJsonSample<Patient>("Patient-f001"));

            string patientReference = $"Patient/{Patient.Id}";

            Observation observationToCreate = Samples.GetJsonSample<Observation>("Observation-For-Patient-f001");

            observationToCreate.Subject.Reference = patientReference;

            Observation = await FhirClient.CreateAsync(observationToCreate);

            Encounter encounterToCreate = Samples.GetJsonSample<Encounter>("Encounter-For-Patient-f001");

            encounterToCreate.Subject.Reference = patientReference;

            Encounter = await FhirClient.CreateAsync(encounterToCreate);

            Condition conditionToCreate = Samples.GetJsonSample<Condition>("Condition-For-Patient-f001");

            conditionToCreate.Subject.Reference = patientReference;

            Condition = await FhirClient.CreateAsync(conditionToCreate);

            Device = await FhirClient.CreateAsync(Samples.GetJsonSample<Device>("Device-d1"));

            DeviceComponent deviceComponent = Samples.GetJsonSample<DeviceComponent>("DeviceComponent-For-Device-d1");

            deviceComponent.Source.Reference = $"Device/{Device.Id}";

            DeviceComponent = await FhirClient.CreateAsync(deviceComponent);
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }
    }
}
