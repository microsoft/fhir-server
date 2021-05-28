// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public class EverythingOperationTestFixture : HttpIntegrationTestFixture
    {
        public EverythingOperationTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        public Patient Patient { get; private set; }

        public Patient NonExistentPatient { get; private set; }

        public Organization Organization { get; private set; }

        public Device Device { get; private set; }

        public Device DeviceOfNonExistentPatient { get; private set; }

        public Observation Observation { get; private set; }

        public Observation ObservationOfNonExistentPatient { get; private set; }

        public Encounter Encounter { get; private set; }

        public Appointment Appointment { get; private set; }

        protected override async Task OnInitializedAsync()
        {
            // Test case #1
            // Create resources that directly referenced by the Patient resource
            Organization = await TestFhirClient.CreateAsync(Samples.GetJsonSample<Organization>("Organization"));
            string organizationReference = $"Organization/{Organization.Id}";

            // Create Patient resource
            Patient patientToCreate = Samples.GetJsonSample<Patient>("Patient-f001");
            patientToCreate.ManagingOrganization.Reference = organizationReference;
            patientToCreate.GeneralPractitioner = new List<ResourceReference>
            {
                new(organizationReference),
            };
            Patient = await TestFhirClient.CreateAsync(patientToCreate);
            string patientReference = $"Patient/{Patient.Id}";

            // Create resources that references the Patient resource
            Device deviceToCreate = Samples.GetJsonSample<Device>("Device-d1");
            deviceToCreate.Patient = new ResourceReference(patientReference);
            Device = await TestFhirClient.CreateAsync(deviceToCreate);

            // Create Patient compartment resources
            Observation observationToCreate = Samples.GetJsonSample<Observation>("Observation-For-Patient-f001");
            observationToCreate.Subject.Reference = patientReference;
            Observation = await TestFhirClient.CreateAsync(observationToCreate);

            Encounter encounterToCreate = Samples.GetJsonSample<Encounter>("Encounter-For-Patient-f001");
            encounterToCreate.Subject.Reference = patientReference;
            Encounter = await TestFhirClient.CreateAsync(encounterToCreate);

            Appointment appointmentToCreate = Samples.GetJsonSample<Appointment>("Appointment");
            appointmentToCreate.Participant = new List<Appointment.ParticipantComponent>
            {
                new()
                {
                    Actor = new ResourceReference(patientReference),
                },
            };
            Appointment = await TestFhirClient.CreateAsync(appointmentToCreate);

            // Test case #2
            // Create resources for a non-existent patient
            patientToCreate.Id = "non-existent-patient-id";
            NonExistentPatient = await TestFhirClient.CreateAsync(patientToCreate);
            patientReference = $"Patient/{NonExistentPatient.Id}";

            deviceToCreate.Patient = new ResourceReference(patientReference);
            DeviceOfNonExistentPatient = await TestFhirClient.CreateAsync(deviceToCreate);

            observationToCreate.Subject.Reference = patientReference;
            ObservationOfNonExistentPatient = await TestFhirClient.CreateAsync(observationToCreate);

            await TestFhirClient.DeleteAsync(NonExistentPatient);
        }
    }
}
