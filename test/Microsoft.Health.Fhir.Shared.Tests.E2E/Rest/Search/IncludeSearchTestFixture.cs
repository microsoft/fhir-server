// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Extensions;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.Rest.Search
{
    public class IncludeSearchTestFixture : HttpIntegrationTestFixture
    {
        public IncludeSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        public CareTeam CareTeam { get; private set; }

        public Medication PercocetMedication { get; private set; }

        public Medication TramadolMedication { get; private set; }

        public Organization Organization { get; private set; }

        public Organization LabAOrganization { get; private set; }

        public Organization LabBOrganization { get; private set; }

        public Organization LabCOrganization { get; private set; }

        public Organization LabDOrganization { get; private set; }

        public Organization LabEOrganization { get; private set; }

        public Organization LabFOrganization { get; private set; }

        public Organization DeletedOrganization { get; private set; }

        public Device DeletedDevice { get; private set; }

        public Practitioner Practitioner { get; private set; }

        public Practitioner AndersonPractitioner { get; private set; }

        public Practitioner SanchezPractitioner { get; private set; }

        public Practitioner TaylorPractitioner { get; private set; }

        public Group PatientGroup { get; private set; }

        public string Tag { get; private set; }

        public Patient PatiPatient { get; private set; }

        public Patient AdamsPatient { get; private set; }

        public Patient PatientWithDeletedOrganization { get; private set; }

        public Observation AdamsLoincObservation { get; private set; }

        public MedicationDispense AdamsMedicationDispense { get; private set; }

        public MedicationRequest AdamsMedicationRequest { get; private set; }

        public Patient TrumanPatient { get; private set; }

        public Observation ObservationWithUntypedReferences { get; private set; }

        public Observation TrumanSnomedObservation { get; private set; }

        public Observation TrumanLoincObservation { get; private set; }

        public DiagnosticReport TrumanSnomedDiagnosticReport { get; private set; }

        public DiagnosticReport TrumanLoincDiagnosticReport { get; private set; }

        public MedicationDispense TrumanMedicationDispenseWithoutRequest { get; private set; }

        public Patient SmithPatient { get; private set; }

        public Observation SmithSnomedObservation { get; private set; }

        public Observation SmithLoincObservation { get; private set; }

        public DiagnosticReport SmithSnomedDiagnosticReport { get; private set; }

        public DiagnosticReport SmithLoincDiagnosticReport { get; private set; }

        public MedicationDispense SmithMedicationDispense { get; private set; }

        public MedicationRequest SmithMedicationRequest { get; private set; }

        public Location Location { get; private set; }

        public Location LocationPartOfSelf { get; private set; }

        protected override async Task OnInitializedAsync()
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

            PercocetMedication = (await TestFhirClient.CreateAsync(new Medication { Meta = meta, Code = new CodeableConcept("http://snomed.info/sct", "16590-619-30", "Percocet tablet") })).Resource;
            TramadolMedication = (await TestFhirClient.CreateAsync(new Medication { Meta = meta, Code = new CodeableConcept("http://snomed.info/sct", "108505002", "Tramadol hydrochloride (substance)") })).Resource;
#if !R5
            Organization = (await TestFhirClient.CreateAsync(new Organization { Meta = meta, Address = new List<Address> { new Address { City = "Seattle" } } })).Resource;
            DeletedOrganization = (await TestFhirClient.CreateAsync(new Organization { Meta = meta, Address = new List<Address> { new Address { City = "SeattleForgotten" } } })).Resource;
#else
            Organization = (await TestFhirClient.CreateAsync(new Organization { Meta = meta, Contact = new List<ExtendedContactDetail> { new ExtendedContactDetail { Address = new Address() { City = "Seattle" } } } })).Resource;
            DeletedOrganization = (await TestFhirClient.CreateAsync(new Organization { Meta = meta, Contact = new List<ExtendedContactDetail> { new ExtendedContactDetail { Address = new Address { City = "SeattleForgotten" } } } })).Resource;
#endif
            Organization.Name = "Updated";

            Organization = (await TestFhirClient.UpdateAsync(Organization)).Resource;

            Practitioner = (await TestFhirClient.CreateAsync(new Practitioner { Meta = meta })).Resource;
            PatiPatient = await CreatePatient("Pati", Practitioner, Organization, "1990-01-01");
            PatientWithDeletedOrganization = await CreatePatient("NonExisting", Practitioner, DeletedOrganization, "1990-01-01");

            var resource = new Device
            {
                Meta = meta,
            }.AssignPatient(new ResourceReference($"Patient/{PatientWithDeletedOrganization.Id}"));

            DeletedDevice = (await TestFhirClient.CreateAsync(resource)).Resource;

            await TestFhirClient.DeleteAsync(DeletedOrganization);
            await TestFhirClient.DeleteAsync(DeletedDevice);

            // Organization Hierarchy
            LabFOrganization = TestFhirClient.CreateAsync(new Organization { Meta = meta }).Result.Resource;
            LabEOrganization = (await TestFhirClient.CreateAsync(new Organization { Meta = meta, PartOf = new ResourceReference($"Organization/{LabFOrganization.Id}") })).Resource;
            LabDOrganization = (await TestFhirClient.CreateAsync(new Organization { Meta = meta, PartOf = new ResourceReference($"Organization/{LabEOrganization.Id}") })).Resource;
            LabCOrganization = (await TestFhirClient.CreateAsync(new Organization { Meta = meta, PartOf = new ResourceReference($"Organization/{LabDOrganization.Id}") })).Resource;
            LabBOrganization = (await TestFhirClient.CreateAsync(new Organization { Meta = meta, PartOf = new ResourceReference($"Organization/{LabCOrganization.Id}") })).Resource;
            LabAOrganization = (await TestFhirClient.CreateAsync(new Organization { Meta = meta, PartOf = new ResourceReference($"Organization/{LabBOrganization.Id}") })).Resource;

            AndersonPractitioner = (await TestFhirClient.CreateAsync(new Practitioner { Meta = meta, Name = new List<HumanName> { new HumanName { Family = "Anderson" } } })).Resource;
            SanchezPractitioner = (await TestFhirClient.CreateAsync(new Practitioner { Meta = meta, Name = new List<HumanName> { new HumanName { Family = "Sanchez" } } })).Resource;
            TaylorPractitioner = (await TestFhirClient.CreateAsync(new Practitioner { Meta = meta, Name = new List<HumanName> { new HumanName { Family = "Taylor" } } })).Resource;

            AdamsPatient = await CreatePatient("Adams", AndersonPractitioner, Organization, "1974-12-25");
            SmithPatient = await CreatePatient("Smith", SanchezPractitioner, Organization, "1981-07-02");
            TrumanPatient = await CreatePatient("Truman", TaylorPractitioner, Organization, "1941-01-15");

            AdamsLoincObservation = await CreateObservation(AdamsPatient, Practitioner, Organization, loincCode, "1990-06-12");
            SmithLoincObservation = await CreateObservation(SmithPatient, Practitioner, Organization, loincCode, "2008-04-10");
            SmithSnomedObservation = await CreateObservation(SmithPatient, Practitioner, Organization, snomedCode, "1977-09-01");
            TrumanLoincObservation = await CreateObservation(TrumanPatient, Practitioner, Organization, loincCode, "2018-11-09");
            TrumanSnomedObservation = await CreateObservation(TrumanPatient, Practitioner, Organization, snomedCode, "1980-03-17");
            ObservationWithUntypedReferences = await CreateObservation(AdamsPatient, Practitioner, Organization, loincCode, "1990-06-12", untypedReferences: true);

            SmithSnomedDiagnosticReport = await CreateDiagnosticReport(SmithPatient, SmithSnomedObservation, snomedCode);
            TrumanSnomedDiagnosticReport = await CreateDiagnosticReport(TrumanPatient, TrumanSnomedObservation, snomedCode);
            SmithLoincDiagnosticReport = await CreateDiagnosticReport(SmithPatient, SmithLoincObservation, loincCode);
            TrumanLoincDiagnosticReport = await CreateDiagnosticReport(TrumanPatient, TrumanLoincObservation, loincCode);

            AdamsMedicationRequest = await CreateMedicationRequest(AdamsPatient, AndersonPractitioner, PercocetMedication);
            SmithMedicationRequest = await CreateMedicationRequest(SmithPatient, SanchezPractitioner, PercocetMedication);

            AdamsMedicationDispense = await CreateMedicationDispense(AdamsMedicationRequest, AdamsPatient, TramadolMedication);
            SmithMedicationDispense = await CreateMedicationDispense(SmithMedicationRequest, SmithPatient, TramadolMedication);
            TrumanMedicationDispenseWithoutRequest = await CreateMedicationDispense(null, TrumanPatient, TramadolMedication);

            CareTeam = await CreateCareTeam();

            Location = (await TestFhirClient.CreateAsync(new Location
            {
                ManagingOrganization = new ResourceReference($"Organization/{Organization.Id}"),
                Meta = meta,
            })).Resource;

            LocationPartOfSelf = (await TestFhirClient.CreateAsync(new Location())).Resource;

            LocationPartOfSelf.PartOf = new ResourceReference($"{LocationPartOfSelf.TypeName}/{LocationPartOfSelf.Id}");
            LocationPartOfSelf = (await TestFhirClient.UpdateAsync(LocationPartOfSelf)).Resource;

            var group = new Group
            {
                Meta = meta,
                Type = Group.GroupType.Person,
#if !R5
                Actual = true,
#endif
                Member = new List<Group.MemberComponent>
                {
                    new Group.MemberComponent { Entity = new ResourceReference($"Patient/{AdamsPatient.Id}") },
                    new Group.MemberComponent { Entity = new ResourceReference($"Patient/{SmithPatient.Id}") },
                    new Group.MemberComponent { Entity = new ResourceReference($"Patient/{TrumanPatient.Id}") },
                },
            };

            PatientGroup = (await TestFhirClient.CreateAsync(@group)).Resource;

            async Task<DiagnosticReport> CreateDiagnosticReport(Patient patient, Observation observation, CodeableConcept code)
            {
                return (await TestFhirClient.CreateAsync(
                    new DiagnosticReport
                    {
                        Meta = meta,
                        Status = DiagnosticReport.DiagnosticReportStatus.Final,
                        Code = code,
                        Subject = new ResourceReference($"Patient/{patient.Id}"),
                        Result = new List<ResourceReference> { new ResourceReference($"Observation/{observation.Id}") },
                    })).Resource;
            }

            async Task<Observation> CreateObservation(Patient patient, Practitioner practitioner, Organization organization, CodeableConcept code, string effectiveDate, bool untypedReferences = false)
            {
                return (await TestFhirClient.CreateAsync(
                    new Observation()
                    {
                        Meta = meta,
                        Status = ObservationStatus.Final,
                        Code = code,
                        Subject = new ResourceReference(untypedReferences ? patient.Id : $"Patient/{patient.Id}"),
                        Performer = new List<ResourceReference>()
                        {
                            new ResourceReference(untypedReferences ? organization.Id : $"Organization/{organization.Id}"),
                            new ResourceReference(untypedReferences ? practitioner.Id : $"Practitioner/{practitioner.Id}"),
                        },
                        Effective = new FhirDateTime(effectiveDate),
                    })).Resource;
            }

            async Task<Patient> CreatePatient(string familyName, Practitioner practitioner, Organization organization, string birthDate)
            {
                return (await TestFhirClient.CreateAsync(
                    new Patient
                    {
                        Meta = meta,
                        BirthDate = birthDate,
                        Name = new List<HumanName> { new HumanName { Family = familyName } },
                        GeneralPractitioner = new List<ResourceReference>()
                        {
                            new ResourceReference($"Practitioner/{practitioner.Id}"),
                        },
                        ManagingOrganization = new ResourceReference($"Organization/{organization.Id}"),
                    })).Resource;
            }

            async Task<MedicationDispense> CreateMedicationDispense(MedicationRequest medicationRequest, Patient patient, Medication medication)
            {
                return (await TestFhirClient.CreateAsync(
                    new MedicationDispense
                    {
                        Meta = meta,
                        AuthorizingPrescription = medicationRequest == null
                            ? null
                            : new List<ResourceReference>
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
                            Concept = medication.Code,
                            Reference = new ResourceReference($"Medication/{medication.Id}"),
                        },
#else
                        Medication = medication.Code,
#endif
#if Stu3
                        Status = MedicationDispense.MedicationDispenseStatus.InProgress,
#else
                        Status = MedicationDispense.MedicationDispenseStatusCodes.InProgress,
#endif
                    })).Resource;
            }

            async Task<MedicationRequest> CreateMedicationRequest(Patient patient, Practitioner practitioner, Medication medication)
            {
                return (await TestFhirClient.CreateAsync(
                    new MedicationRequest
                    {
                        Meta = meta,
                        Subject = new ResourceReference($"Patient/{patient.Id}"),
#if Stu3
                        Intent = MedicationRequest.MedicationRequestIntent.Order,
                        Status = MedicationRequest.MedicationRequestStatus.Completed,
                        Requester = new MedicationRequest.RequesterComponent
                        {
                            Agent = new ResourceReference($"Practitioner/{practitioner.Id}"),
                        },
#else
                        IntentElement = new Code<MedicationRequest.MedicationRequestIntent> { Value = MedicationRequest.MedicationRequestIntent.Order },
                        StatusElement = new Code<MedicationRequest.MedicationrequestStatus> { Value = MedicationRequest.MedicationrequestStatus.Completed },
                        Requester = new ResourceReference($"Practitioner/{practitioner.Id}"),

#endif
#if R5
                        Medication = new CodeableReference
                        {
                            Concept = medication.Code,
                            Reference = new ResourceReference($"Medication/{medication.Id}"),
                        },
#else
                        Medication = medication.Code,
#endif
                    })).Resource;
            }

            async Task<CareTeam> CreateCareTeam()
            {
                return (await TestFhirClient.CreateAsync(
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
                    })).Resource;
            }
        }
    }
}
