// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
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

        public Organization Organization { get; private set; }

        public Practitioner Practitioner { get; private set; }

        public Group PatientGroup { get; private set; }

        public string Tag { get; private set; }

        public Patient AdamsPatient { get; private set; }

        public Observation AdamsLoincObservation { get; private set; }

        public Patient TrumanPatient { get; private set; }

        public Observation TrumanSnomedObservation { get; private set; }

        public Observation TrumanLoincObservation { get; private set; }

        public DiagnosticReport TrumanSnomedDiagnosticReport { get; private set; }

        public DiagnosticReport TrumanLoincDiagnosticReport { get; private set; }

        public Patient SmithPatient { get; private set; }

        public Observation SmithSnomedObservation { get; private set; }

        public Observation SmithLoincObservation { get; private set; }

        public DiagnosticReport SmithSnomedDiagnosticReport { get; private set; }

        public DiagnosticReport SmithLoincDiagnosticReport { get; private set; }

        public Location Location { get; private set; }

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

            Organization = (await TestFhirClient.CreateAsync(new Organization { Meta = meta, Address = new List<Address> { new Address { City = "Seattle" } } })).Resource;
            Practitioner = (await TestFhirClient.CreateAsync(new Practitioner { Meta = meta })).Resource;

            AdamsPatient = (await TestFhirClient.CreateAsync(new Patient { Meta = meta, Name = new List<HumanName> { new HumanName { Family = "Adams" } } })).Resource;
            SmithPatient = (await TestFhirClient.CreateAsync(new Patient { Meta = meta, Name = new List<HumanName> { new HumanName { Family = "Smith" } }, ManagingOrganization = new ResourceReference($"Organization/{Organization.Id}") })).Resource;
            TrumanPatient = (await TestFhirClient.CreateAsync(new Patient { Meta = meta, Name = new List<HumanName> { new HumanName { Family = "Truman" } } })).Resource;

            AdamsLoincObservation = await CreateObservation(AdamsPatient, loincCode);
            SmithLoincObservation = await CreateObservation(SmithPatient, loincCode);
            SmithSnomedObservation = await CreateObservation(SmithPatient, snomedCode);
            TrumanLoincObservation = await CreateObservation(TrumanPatient, loincCode);
            TrumanSnomedObservation = await CreateObservation(TrumanPatient, snomedCode);

            SmithSnomedDiagnosticReport = await CreateDiagnosticReport(SmithPatient, SmithSnomedObservation, snomedCode);
            TrumanSnomedDiagnosticReport = await CreateDiagnosticReport(TrumanPatient, TrumanSnomedObservation, snomedCode);
            SmithLoincDiagnosticReport = await CreateDiagnosticReport(SmithPatient, SmithLoincObservation, loincCode);
            TrumanLoincDiagnosticReport = await CreateDiagnosticReport(TrumanPatient, TrumanLoincObservation, loincCode);

            Location = (await TestFhirClient.CreateAsync(new Location
            {
                ManagingOrganization = new ResourceReference($"Organization/{Organization.Id}"),
                Meta = new Meta { Tag = new List<Coding> { new Coding("testTag", Tag) } },
            })).Resource;

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

            async Task<Observation> CreateObservation(Patient patient, CodeableConcept code)
            {
                return (await TestFhirClient.CreateAsync(
                    new Observation()
                    {
                        Meta = meta,
                        Status = ObservationStatus.Final,
                        Code = code,
                        Subject = new ResourceReference($"Patient/{patient.Id}"),
                        Performer = new List<ResourceReference>
                        {
                            new ResourceReference($"Organization/{Organization.Id}"),
                            new ResourceReference($"Practitioner/{Practitioner.Id}"),
                        },
                    })).Resource;
            }
        }
    }
}
