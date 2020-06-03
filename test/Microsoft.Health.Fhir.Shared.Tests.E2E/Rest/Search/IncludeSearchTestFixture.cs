// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
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

            Organization = TestFhirClient.CreateAsync(new Organization { Meta = meta, Address = new List<Address> { new Address { City = "Seattle" } } }).Result.Resource;
            Practitioner = TestFhirClient.CreateAsync(new Practitioner { Meta = meta }).Result.Resource;

            AdamsPatient = TestFhirClient.CreateAsync(new Patient { Meta = meta, Name = new List<HumanName> { new HumanName { Family = "Adams" } } }).Result.Resource;
            SmithPatient = TestFhirClient.CreateAsync(new Patient { Meta = meta, Name = new List<HumanName> { new HumanName { Family = "Smith" } }, ManagingOrganization = new ResourceReference($"Organization/{Organization.Id}") }).Result.Resource;
            TrumanPatient = TestFhirClient.CreateAsync(new Patient { Meta = meta, Name = new List<HumanName> { new HumanName { Family = "Truman" } } }).Result.Resource;

            AdamsLoincObservation = CreateObservation(AdamsPatient, loincCode);
            SmithLoincObservation = CreateObservation(SmithPatient, loincCode);
            SmithSnomedObservation = CreateObservation(SmithPatient, snomedCode);
            TrumanLoincObservation = CreateObservation(TrumanPatient, loincCode);
            TrumanSnomedObservation = CreateObservation(TrumanPatient, snomedCode);

            SmithSnomedDiagnosticReport = CreateDiagnosticReport(SmithPatient, SmithSnomedObservation, snomedCode);
            TrumanSnomedDiagnosticReport = CreateDiagnosticReport(TrumanPatient, TrumanSnomedObservation, snomedCode);
            SmithLoincDiagnosticReport = CreateDiagnosticReport(SmithPatient, SmithLoincObservation, loincCode);
            TrumanLoincDiagnosticReport = CreateDiagnosticReport(TrumanPatient, TrumanLoincObservation, loincCode);

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

            Observation CreateObservation(Patient patient, CodeableConcept code)
            {
                return TestFhirClient.CreateAsync(
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
                    }).Result.Resource;
            }
        }

        public Organization Organization { get; }

        public Practitioner Practitioner { get; }

        public Group PatientGroup { get; }

        public string Tag { get; }

        public Patient AdamsPatient { get; }

        public Observation AdamsLoincObservation { get; }

        public Patient TrumanPatient { get; }

        public Observation TrumanSnomedObservation { get; }

        public Observation TrumanLoincObservation { get; }

        public DiagnosticReport TrumanSnomedDiagnosticReport { get; }

        public DiagnosticReport TrumanLoincDiagnosticReport { get; }

        public Patient SmithPatient { get; }

        public Observation SmithSnomedObservation { get; }

        public Observation SmithLoincObservation { get; }

        public DiagnosticReport SmithSnomedDiagnosticReport { get; }

        public DiagnosticReport SmithLoincDiagnosticReport { get; }
    }
}
