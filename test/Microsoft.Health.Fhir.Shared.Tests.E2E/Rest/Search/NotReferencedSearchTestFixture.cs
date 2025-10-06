// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class NotReferencedSearchTestFixture : HttpIntegrationTestFixture
    {
        public NotReferencedSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        public string Tag { get; private set; }

        public Observation ObservationSubject { get; private set; }

        public Observation ObservationPerformer { get; private set; }

        public Encounter Encounter { get; private set; }

        public Patient[] Patients { get; private set; }

        protected override async Task OnInitializedAsync()
        {
            // Creates a unique code for searches
            Tag = Guid.NewGuid().ToString();

            Patients = await TestFhirClient.CreateResourcesAsync<Patient>(5, Tag);

            ObservationSubject = PopulateObservation(new Observation(), Tag);
            ObservationSubject.Subject = new ResourceReference(KnownResourceTypes.Patient + "/" + Patients[0].Id);
            ObservationSubject = await TestFhirClient.CreateAsync<Observation>(ObservationSubject);

            ObservationPerformer = PopulateObservation(new Observation(), Tag);
            ObservationPerformer.Performer = new List<ResourceReference>()
            {
                new ResourceReference(KnownResourceTypes.Patient + "/" + Patients[1].Id),
            };
            ObservationPerformer = await TestFhirClient.CreateAsync<Observation>(ObservationPerformer);

            Encounter = await TestFhirClient.CreateAsync(PopulateEncounter(new Encounter(), Patients[2], Tag));
        }

        private static Observation PopulateObservation(Observation observation, string tag)
        {
            observation.Status = ObservationStatus.Final;
            observation.Code = new CodeableConcept
            {
                Coding = new List<Coding>
                    {
                        new Coding
                        {
                            Code = tag,
                            System = "http://fhir-server-test/tag",
                        },
                    },
            };
            observation.Meta = new Meta
            {
                Tag = new List<Coding>
                {
                    new Coding
                    {
                        Code = tag,
                        System = "http://fhir-server-test/tag",
                    },
                },
            };

            return observation;
        }

        private static Encounter PopulateEncounter(Encounter encounter, Patient patient, string tag)
        {
            encounter.Meta = new Meta
            {
                Tag = new List<Coding>
                {
                    new Coding
                    {
                        Code = tag,
                        System = "http://fhir-server-test/tag",
                    },
                },
            };

#if R5
            encounter.Class = new List<CodeableConcept>()
            {
                new CodeableConcept
                {
                    Coding = new List<Coding>
                        {
                            new Coding
                            {
                                Code = "test",
                                System = "test",
                            },
                        },
                },
            };
            encounter.Status = EncounterStatus.Completed;
#else
            encounter.Class = new Coding
            {
                Code = "test",
                System = "test",
            };
            encounter.Status = Encounter.EncounterStatus.Arrived;
#endif

            encounter.Subject = new ResourceReference(KnownResourceTypes.Patient + "/" + patient.Id);

            return encounter;
        }
    }
}
