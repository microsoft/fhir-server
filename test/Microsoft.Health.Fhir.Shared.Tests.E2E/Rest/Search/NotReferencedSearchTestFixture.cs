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

        public Observation Observation { get; private set; }

        public Patient[] Patients { get; private set; }

        protected override async Task OnInitializedAsync()
        {
            // Creates a unique code for searches
            Tag = Guid.NewGuid().ToString();

            Patients = await TestFhirClient.CreateResourcesAsync<Patient>(3, Tag);

            Observation = await TestFhirClient.CreateAsync<Observation>(PopulateObservation(new Observation(), Patients[0], Tag));
        }

        private static Observation PopulateObservation(Observation observation, Patient patient, string tag)
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
            observation.Subject = new ResourceReference(KnownResourceTypes.Patient + "/" + patient.Id);

            return observation;
        }
    }
}
