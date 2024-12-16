// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Extensions;
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

        public Patient PatientWithSeeAlsoLink { get; private set; }

        public Patient PatientWithTwoSeeAlsoLinks { get; private set; }

        public Patient PatientWithSeeAlsoLinkToRemove { get; private set; }

        public Patient PatientWithReplacedByLink { get; private set; }

        public Patient PatientWithReferLink { get; private set; }

        public Patient PatientWithReplacesLink { get; private set; }

        public Patient PatientReferencedBySeeAlsoLink { get; private set; }

        public Patient PatientReferencedByRemovedSeeAlsoLink { get; private set; }

        public List<Patient> PatientsReferencedBySeeAlsoLink { get; private set; }

        public Patient PatientReferencedByReferLink { get; private set; }

        public Patient PatientReferencedByReplacesLink { get; private set; }

        public Patient PatientReferencedByReplacedByLink { get; private set; }

        public Organization Organization { get; private set; }

        public Device Device { get; private set; }

        public Device DeviceOfNonExistentPatient { get; private set; }

        public Observation Observation { get; private set; }

        public Observation ObservationOfPatientWithSeeAlsoLinkToRemove { get; private set; }

        public Observation ObservationOfNonExistentPatient { get; private set; }

        public Observation ObservationOfPatientReferencedBySeeAlsoLink { get; private set; }

        public Encounter Encounter { get; private set; }

        public Appointment Appointment { get; private set; }

        internal async Task UpdatePatient(Patient patientToUpdate)
        {
            await TestFhirClient.UpdateAsync(patientToUpdate);
        }

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

            deviceToCreate.AssignPatient(new ResourceReference(patientReference));
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

            deviceToCreate.AssignPatient(new ResourceReference(patientReference));
            DeviceOfNonExistentPatient = await TestFhirClient.CreateAsync(deviceToCreate);

            observationToCreate.Subject.Reference = patientReference;
            ObservationOfNonExistentPatient = await TestFhirClient.CreateAsync(observationToCreate);

            await TestFhirClient.DeleteAsync(NonExistentPatient);

            // Test case #3
            // Create patients to be referenced by links
            patientToCreate = Samples.GetJsonSample<Patient>("PatientWithMinimalData");
            PatientReferencedBySeeAlsoLink = await TestFhirClient.CreateAsync(patientToCreate);

            PatientsReferencedBySeeAlsoLink = new List<Patient>();
            patientToCreate = Samples.GetJsonSample<Patient>("PatientWithMinimalData");
            PatientsReferencedBySeeAlsoLink.Add(await TestFhirClient.CreateAsync(patientToCreate));
            patientToCreate = Samples.GetJsonSample<Patient>("PatientWithMinimalData");
            PatientsReferencedBySeeAlsoLink.Add(await TestFhirClient.CreateAsync(patientToCreate));

            patientToCreate = Samples.GetJsonSample<Patient>("PatientWithMinimalData");
            PatientReferencedByRemovedSeeAlsoLink = await TestFhirClient.CreateAsync(patientToCreate);

            patientToCreate = Samples.GetJsonSample<Patient>("PatientWithMinimalData");
            PatientReferencedByReplacedByLink = await TestFhirClient.CreateAsync(patientToCreate);

            patientToCreate = Samples.GetJsonSample<Patient>("PatientWithMinimalData");
            PatientReferencedByReplacesLink = await TestFhirClient.CreateAsync(patientToCreate);

            patientToCreate = Samples.GetJsonSample<Patient>("PatientWithMinimalData");
            PatientReferencedByReferLink = await TestFhirClient.CreateAsync(patientToCreate);

            // Create patients with different types of links
            PatientWithSeeAlsoLink = await CreatePatientWithLinks(Patient.LinkType.Seealso, new List<Patient> { PatientReferencedBySeeAlsoLink });
            PatientWithTwoSeeAlsoLinks = await CreatePatientWithLinks(Patient.LinkType.Seealso, PatientsReferencedBySeeAlsoLink);
            PatientWithSeeAlsoLinkToRemove = await CreatePatientWithLinks(Patient.LinkType.Seealso, new List<Patient> { PatientReferencedByRemovedSeeAlsoLink });
            PatientWithReplacedByLink = await CreatePatientWithLinks(Patient.LinkType.ReplacedBy, new List<Patient> { PatientReferencedByReplacedByLink });
            PatientWithReplacesLink = await CreatePatientWithLinks(Patient.LinkType.Replaces, new List<Patient> { PatientReferencedByReplacesLink });
            PatientWithReferLink = await CreatePatientWithLinks(Patient.LinkType.Refer, new List<Patient> { PatientReferencedByReferLink });

            // Create Patient compartment resource
            observationToCreate = Samples.GetJsonSample<Observation>("Observation-For-Patient-f001");
            observationToCreate.Subject.Reference = $"Patient/{PatientReferencedBySeeAlsoLink.Id}";
            ObservationOfPatientReferencedBySeeAlsoLink = await TestFhirClient.CreateAsync(observationToCreate);

            observationToCreate = Samples.GetJsonSample<Observation>("Observation-For-Patient-f001");
            observationToCreate.Subject.Reference = $"Patient/{PatientWithSeeAlsoLinkToRemove.Id}";
            ObservationOfPatientWithSeeAlsoLinkToRemove = await TestFhirClient.CreateAsync(observationToCreate);
        }

        private async Task<Patient> CreatePatientWithLinks(Patient.LinkType linkType, List<Patient> patientsReferencedByLink)
        {
            Patient patientWithLink = Samples.GetJsonSample<Patient>("PatientWithMinimalData");
            patientWithLink.Link = new List<Patient.LinkComponent>();

            foreach (Patient patientReferencedByLink in patientsReferencedByLink)
            {
                var link = new Patient.LinkComponent
                {
                    Type = linkType,
                    Other = new ResourceReference($"Patient/{patientReferencedByLink.Id}"),
                };

                patientWithLink.Link.Add(link);
            }

            return await TestFhirClient.CreateAsync(patientWithLink);
        }
    }
}
