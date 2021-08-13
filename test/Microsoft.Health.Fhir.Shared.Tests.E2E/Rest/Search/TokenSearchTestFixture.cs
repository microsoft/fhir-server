// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class TokenSearchTestFixture : HttpIntegrationTestFixture
    {
        public TokenSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        public string Tag { get; private set; }

        public IReadOnlyList<Observation> Observations { get; private set; }

        public IReadOnlyCollection<Patient> Patients { get; private set; }

        protected override async Task OnInitializedAsync()
        {
            Tag = Guid.NewGuid().ToString();

            Patients = await TestFhirClient.CreateResourcesAsync<Patient>(
                 p => SetPatient(p, id => { }),
                 p => SetPatient(p, id =>
                  {
                      id.Type = new CodeableConcept();
                      id.Type.Coding = new List<Coding>();
                      id.Type.Coding.Add(new Coding("http://terminology.hl7.org/CodeSystem/v2-0203", "RRI", "RG"));
                      id.Type.Text = "RG";
                      id.Value = "1234";
                  }),
                 p => SetPatient(p, id =>
                   {
                       id.Type = new CodeableConcept();
                       id.Type.Coding = new List<Coding>();
                       id.Type.Coding.Add(new Coding("http://terminology.hl7.org/CodeSystem/v2-0203", "PN", "Personal number"));
                       id.Type.Coding.Add(new Coding("http://terminology.hl7.org/CodeSystem/v3-GenderStatus", "I", "Intact"));
                       id.Type.Text = "Multiple";
                       id.Value = "744-744-6141";
                   }),
                 p => SetPatient(p, id =>
                 {
                     id.Type = new CodeableConcept();
                     id.System = "http://terminology.hl7.org/CodeSystem/v2-0203";
                     id.Value = "744-744-6141";
                 }),
                 p => SetPatient(p, id =>
                 {
                     id.Type = new CodeableConcept();
                     id.System = "http://terminology.hl7.org/CodeSystem/v2-0203";

                     // id.Value = "744-744-6142";
                 }),
                 p => SetPatient(p, id =>
                 {
                     id.Type = new CodeableConcept();
                     id.Value = "744-744-6141";
                 }));

            Observations = await TestFhirClient.CreateResourcesAsync<Observation>(
                o => SetObservation(o, cc => cc.Coding.Add(new Coding("system1", "code1"))),
                o => SetObservation(o, cc => cc.Coding.Add(new Coding("system2", "code2"))),
                o => SetObservation(o, cc => cc.Text = "text"),
                o => SetObservation(o, cc => cc.Coding.Add(new Coding("system1", "code2", "text2"))),
                o => SetObservation(o, cc => cc.Coding.Add(new Coding("system3", "code3", "text"))),
                o => SetObservation(o, cc =>
                {
                    cc.Text = "text";
                    cc.Coding.Add(new Coding("system1", "code1"));
                    cc.Coding.Add(new Coding("system3", "code2"));
                }),
                o => SetObservation(o, cc =>
                {
                    cc.Coding.Add(new Coding("system2", "code1"));
                    cc.Coding.Add(new Coding("system3", "code3", "text2"));
                }),
                o => SetObservation(o, cc => cc.Coding.Add(new Coding(null, "code3"))),
                o =>
                {
                    SetObservation(o, cc => { });
                    o.Category = new List<CodeableConcept>
                    {
                        new CodeableConcept("system", "test"),
                    };
                });

            void SetObservation(Observation observation, Action<CodeableConcept> codeableConceptCustomizer)
            {
                observation.Meta = new Meta
                {
                    Tag = new List<Coding>
                    {
                        new Coding("testTag", Tag),
                    },
                };
                observation.Code = new CodeableConcept("system", "code");
                observation.Status = ObservationStatus.Registered;

                var codeableConcept = new CodeableConcept();

                codeableConceptCustomizer(codeableConcept);

                observation.Value = codeableConcept;
            }

            void SetPatient(Patient patient, Action<Identifier> identiiferCustomizer)
            {
                patient.Meta = new Meta
                {
                    Tag = new List<Coding>
                    {
                        new Coding("testTag", Tag),
                    },
                };
                var identifier = new Identifier();
                identiiferCustomizer(identifier);
                patient.Identifier = new List<Identifier>() { identifier };
            }
        }
    }
}
