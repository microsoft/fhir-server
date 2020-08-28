// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Rest;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.Rest.Search
{
    public class IncludeSearchTestFixture : HttpIntegrationTestFixture
    {
        public IncludeSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
            Tag = Guid.NewGuid().ToString();

            // Construct an observation pointing to a patient and a diagnostic report pointing to the observation and the patient along with some not matching entries
            var snomedCode = new CodeableConcept("http://snomed.info/sct", "429858000");
            var loincCode = new CodeableConcept("http://loinc.org", "4548-4");

            var meta = new Meta
            {
                Tag = new List<Coding>
                    {
                        new Coding("testTag", Tag),
                    },
            };

            Medication = TestFhirClient.CreateAsync(new Medication { Meta = meta, Code = new CodeableConcept("http://snomed.info/sct", "16590-619-30", "Percocet tablet") }).Result.Resource;
            Organization = TestFhirClient.CreateAsync(new Organization { Meta = meta, Address = new List<Address> { new Address { City = "Seattle" } } }).Result.Resource;
            Practitioner = TestFhirClient.CreateAsync(new Practitioner { Meta = meta }).Result.Resource;

            // Organization Hierarchy
            LabFOrganization = TestFhirClient.CreateAsync(new Organization { Meta = meta }).Result.Resource;
            LabEOrganization = TestFhirClient.CreateAsync(new Organization { Meta = meta, PartOf = new ResourceReference($"Organization/{LabFOrganization.Id}") }).Result.Resource;
            LabDOrganization = TestFhirClient.CreateAsync(new Organization { Meta = meta, PartOf = new ResourceReference($"Organization/{LabEOrganization.Id}") }).Result.Resource;
            LabCOrganization = TestFhirClient.CreateAsync(new Organization { Meta = meta, PartOf = new ResourceReference($"Organization/{LabDOrganization.Id}") }).Result.Resource;
            LabBOrganization = TestFhirClient.CreateAsync(new Organization { Meta = meta, PartOf = new ResourceReference($"Organization/{LabCOrganization.Id}") }).Result.Resource;
            LabAOrganization = TestFhirClient.CreateAsync(new Organization { Meta = meta, PartOf = new ResourceReference($"Organization/{LabBOrganization.Id}") }).Result.Resource;

            AndersonPractitioner = TestFhirClient.CreateAsync(new Practitioner { Meta = meta, Name = new List<HumanName> { new HumanName { Family = "Anderson" } } }).Result.Resource;
            SanchezPractitioner = TestFhirClient.CreateAsync(new Practitioner { Meta = meta, Name = new List<HumanName> { new HumanName { Family = "Sanchez" } } }).Result.Resource;
            TaylorPractitioner = TestFhirClient.CreateAsync(new Practitioner { Meta = meta, Name = new List<HumanName> { new HumanName { Family = "Taylor" } } }).Result.Resource;

            AdamsPatient = CreatePatient("Adams", AndersonPractitioner, Organization);
            SmithPatient = CreatePatient("Smith",  SanchezPractitioner, Organization);
            TrumanPatient = CreatePatient("Truman",  TaylorPractitioner, Organization);

            AdamsLoincObservation = CreateObservation(AdamsPatient, Practitioner, Organization, loincCode);
            SmithLoincObservation = CreateObservation(SmithPatient, Practitioner, Organization, loincCode);
            SmithSnomedObservation = CreateObservation(SmithPatient, Practitioner, Organization, snomedCode);
            TrumanLoincObservation = CreateObservation(TrumanPatient, Practitioner, Organization, loincCode);
            TrumanSnomedObservation = CreateObservation(TrumanPatient, Practitioner, Organization, snomedCode);

            SmithSnomedDiagnosticReport = CreateDiagnosticReport(SmithPatient, SmithSnomedObservation, snomedCode);
            TrumanSnomedDiagnosticReport = CreateDiagnosticReport(TrumanPatient, TrumanSnomedObservation, snomedCode);
            SmithLoincDiagnosticReport = CreateDiagnosticReport(SmithPatient, SmithLoincObservation, loincCode);
            TrumanLoincDiagnosticReport = CreateDiagnosticReport(TrumanPatient, TrumanLoincObservation, loincCode);

            AdamsMedicationRequest = CreateMedicationRequest(AdamsPatient);
            SmithMedicationRequest = CreateMedicationRequest(SmithPatient);
            TrumanMedicationRequest = CreateMedicationRequest(TrumanPatient);

            AdamsMedicationDispense = CreateMedicationDispense(AdamsMedicationRequest, AdamsPatient, Organization);
            SmithMedicationDispense = CreateMedicationDispense(SmithMedicationRequest, SmithPatient, Organization);
            TrumanMedicationDispense = CreateMedicationDispense(TrumanMedicationRequest, TrumanPatient, Organization);

            CareTeam = CreateCareTeam();

            Location = TestFhirClient.CreateAsync(new Location
            {
                ManagingOrganization = new ResourceReference($"Organization/{Organization.Id}"),
                Meta = new Meta { Tag = new List<Coding> { new Coding("testTag", Tag) } },
            }).Result.Resource;

            var group = new Group
            {
                Meta = new Meta { Tag = new List<Coding> { new Coding("testTag", Tag) } },
                Type = Group.GroupType.Person, Actual = true,
                Member = new List<Group.MemberComponent>
                    {
                        new Group.MemberComponent { Entity = new ResourceReference($"Patient/{AdamsPatient.Id}") },
                        new Group.MemberComponent { Entity = new ResourceReference($"Patient/{SmithPatient.Id}") },
                        new Group.MemberComponent { Entity = new ResourceReference($"Patient/{TrumanPatient.Id}") },
                    },
            };

            PatientGroup = TestFhirClient.CreateAsync(group).Result.Resource;

            DiagnosticReport CreateDiagnosticReport(Patient patient, Observation observation, CodeableConcept code)
            {
                return TestFhirClient.CreateAsync(
                    new DiagnosticReport
                    {
                        Meta = meta,
                        Status = DiagnosticReport.DiagnosticReportStatus.Final,
                        Code = code,
                        Subject = new ResourceReference($"Patient/{patient.Id}"),
                        Result = new List<ResourceReference> { new ResourceReference($"Observation/{observation.Id}") },
                    }).Result.Resource;
            }

            Observation CreateObservation(Patient patient, Practitioner practitioner, Organization organization, CodeableConcept code)
            {
                return TestFhirClient.CreateAsync(
                    new Observation()
                    {
                        Meta = meta,
                        Status = ObservationStatus.Final,
                        Code = code,
                        Subject = new ResourceReference($"Patient/{patient.Id}"),
                        Performer = new List<ResourceReference>()
                        {
                            new ResourceReference($"Organization/{organization.Id}"),
                            new ResourceReference($"Practitioner/{practitioner.Id}"),
                        },
                    }).Result.Resource;
            }

            Patient CreatePatient(string familyName, Practitioner practitioner, Organization organization)
            {
                return TestFhirClient.CreateAsync(
                    new Patient
                    {
                        Meta = meta,
                        Name = new List<HumanName> { new HumanName { Family = familyName } },
                        GeneralPractitioner = new List<ResourceReference>()
                        {
                            new ResourceReference($"Practitioner/{practitioner.Id}"),
                        },
                        ManagingOrganization = new ResourceReference($"Organization/{organization.Id}"),
                    }).Result.Resource;
            }

            MedicationDispense CreateMedicationDispense(MedicationRequest medicationRequest, Patient patient, Organization organization)
            {
               return TestFhirClient.CreateAsync(
                    new MedicationDispense
                    {
                        Meta = meta,
                        AuthorizingPrescription = new List<ResourceReference>
                        {
                            new ResourceReference($"MedicationRequest/{medicationRequest.Id}"),
                        },
                        Subject = new ResourceReference($"Patient/{patient.Id}"),
                        Performer = new List<MedicationDispense.PerformerComponent>()
                        {
                            new MedicationDispense.PerformerComponent()
                            {
                                Actor = new ResourceReference($"Practitioner/{Practitioner.Id}"),
                            },
                        },
#if R5
                        Medication = new CodeableReference
                        {
                            Concept = new CodeableConcept("http://snomed.info/sct", "16590-619-30", "Percocet tablet"),
                            Reference = new ResourceReference($"Medication/{Medication.Id}"),
                        },
#else
                        Medication = new CodeableConcept("http://snomed.info/sct", "16590-619-30", "Percocet tablet"),
#endif
#if Stu3
                        Status = MedicationDispense.MedicationDispenseStatus.InProgress,
#else
                        Status = MedicationDispense.MedicationDispenseStatusCodes.InProgress,
#endif
                    }).Result.Resource;
            }

            MedicationRequest CreateMedicationRequest(Patient patient)
            {
                return TestFhirClient.CreateAsync(
                    new MedicationRequest
                    {
                        Meta = meta,
                        Subject = new ResourceReference($"Patient/{patient.Id}"),
#if Stu3
                        Intent = MedicationRequest.MedicationRequestIntent.Order,
                        Status = MedicationRequest.MedicationRequestStatus.Completed,
                        Requester = new MedicationRequest.RequesterComponent
                        {
                            Agent = new ResourceReference($"Practitioner/{Practitioner.Id}"),
                        },
#else
                        IntentElement = new Code<MedicationRequest.medicationRequestIntent> { Value = MedicationRequest.medicationRequestIntent.Order },
                        StatusElement = new Code<MedicationRequest.medicationrequestStatus> { Value = MedicationRequest.medicationrequestStatus.Completed },
                        Requester = new ResourceReference($"Practitioner/{Practitioner.Id}"),

#endif
#if R5
                        Medication = new CodeableReference
                        {
                            Concept = new CodeableConcept("http://snomed.info/sct", "16590-619-30", "Percocet tablet"),
                            Reference = new ResourceReference($"Medication/{Medication.Id}"),
                        },
#else
                        Medication = new CodeableConcept("http://snomed.info/sct", "16590-619-30", "Percocet tablet"),
#endif
                    }).Result.Resource;
            }

            CareTeam CreateCareTeam()
            {
                return TestFhirClient.CreateAsync(
                    new CareTeam
                    {
                        Meta = meta,
                        Participant = new List<CareTeam.ParticipantComponent>()
                        {
                            new CareTeam.ParticipantComponent { Member = new ResourceReference($"Patient/{AdamsPatient.Id}") },
                            new CareTeam.ParticipantComponent { Member = new ResourceReference($"Patient/{SmithPatient.Id}") },
                            new CareTeam.ParticipantComponent { Member = new ResourceReference($"Patient/{TrumanPatient.Id}") },
                            new CareTeam.ParticipantComponent { Member = new ResourceReference($"Organization/{Organization.Id}") },
                            new CareTeam.ParticipantComponent { Member = new ResourceReference($"Practitioner/{Practitioner.Id}") },
                        },
                    }).Result.Resource;
            }
        }

        public CareTeam CareTeam { get; }

        public Medication Medication { get; }

        public Organization Organization { get; }

        public Organization LabAOrganization { get; }

        public Organization LabBOrganization { get; }

        public Organization LabCOrganization { get; }

        public Organization LabDOrganization { get; }

        public Organization LabEOrganization { get; }

        public Organization LabFOrganization { get; }

        public Practitioner Practitioner { get; }

        public Practitioner AndersonPractitioner { get; }

        public Practitioner SanchezPractitioner { get; }

        public Practitioner TaylorPractitioner { get; }

        public Group PatientGroup { get; }

        public string Tag { get; }

        public Patient AdamsPatient { get; }

        public Observation AdamsLoincObservation { get; }

        public MedicationDispense AdamsMedicationDispense { get; }

        public MedicationRequest AdamsMedicationRequest { get; }

        public Patient TrumanPatient { get; }

        public Observation TrumanSnomedObservation { get; }

        public Observation TrumanLoincObservation { get; }

        public DiagnosticReport TrumanSnomedDiagnosticReport { get; }

        public DiagnosticReport TrumanLoincDiagnosticReport { get; }

        public MedicationDispense TrumanMedicationDispense { get; }

        public MedicationRequest TrumanMedicationRequest { get; }

        public Patient SmithPatient { get; }

        public Observation SmithSnomedObservation { get; }

        public Observation SmithLoincObservation { get; }

        public DiagnosticReport SmithSnomedDiagnosticReport { get; }

        public DiagnosticReport SmithLoincDiagnosticReport { get; }

        public MedicationDispense SmithMedicationDispense { get; }

        public MedicationRequest SmithMedicationRequest { get; }

        public Location Location { get; }
    }
}