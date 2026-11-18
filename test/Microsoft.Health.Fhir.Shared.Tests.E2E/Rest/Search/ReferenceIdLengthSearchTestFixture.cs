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
    /// <summary>
    /// Creates resources that reference ids longer than the maximum resource id length (64 characters by default).
    /// Such reference ids are indexed truncated to the maximum length.
    /// </summary>
    public class ReferenceIdLengthSearchTestFixture : HttpIntegrationTestFixture
    {
        public const int MaxResourceIdLength = 64;

        public ReferenceIdLengthSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        /// <summary>
        /// Gets a valid practitioner id of exactly <see cref="MaxResourceIdLength"/> characters.
        /// </summary>
        public string PractitionerId { get; } = CreateId('a');

        /// <summary>
        /// Gets a practitioner id one character longer than the maximum, that starts with <see cref="PractitionerId"/>.
        /// </summary>
        public string OverlongPractitionerId => PractitionerId + "x";

        public string UnrelatedPractitionerId { get; } = CreateId('b');

        public string FixtureTag { get; private set; }

        /// <summary>
        /// Gets a patient whose general practitioner is <see cref="PractitionerId"/>.
        /// </summary>
        public Patient AllowPatient { get; private set; }

        /// <summary>
        /// Gets a patient whose general practitioner is <see cref="OverlongPractitionerId"/>.
        /// </summary>
        public Patient PrivatePatient { get; private set; }

        /// <summary>
        /// Gets a patient whose general practitioner is <see cref="UnrelatedPractitionerId"/>.
        /// </summary>
        public Patient UnrelatedPatient { get; private set; }

        /// <summary>
        /// Gets an observation about <see cref="AllowPatient"/> performed by <see cref="PractitionerId"/>.
        /// </summary>
        public Observation AllowObservation { get; private set; }

        /// <summary>
        /// Gets an observation about <see cref="PrivatePatient"/> performed by <see cref="OverlongPractitionerId"/>.
        /// </summary>
        public Observation PrivateObservation { get; private set; }

        /// <summary>
        /// Gets an observation about <see cref="UnrelatedPatient"/> performed by <see cref="UnrelatedPractitionerId"/>.
        /// </summary>
        public Observation UnrelatedObservation { get; private set; }

        protected override async Task OnInitializedAsync()
        {
            FixtureTag = Guid.NewGuid().ToString();

            Patient[] patients = await TestFhirClient.CreateResourcesAsync<Patient>(
                p => SetPractitioner(p, FixtureTag, PractitionerId),
                p => SetPractitioner(p, FixtureTag, OverlongPractitionerId),
                p => SetPractitioner(p, FixtureTag, UnrelatedPractitionerId));

            AllowPatient = patients[0];
            PrivatePatient = patients[1];
            UnrelatedPatient = patients[2];

            Observation[] observations = await TestFhirClient.CreateResourcesAsync<Observation>(
                o => SetObservation(o, AllowPatient, PractitionerId),
                o => SetObservation(o, PrivatePatient, OverlongPractitionerId),
                o => SetObservation(o, UnrelatedPatient, UnrelatedPractitionerId));

            AllowObservation = observations[0];
            PrivateObservation = observations[1];
            UnrelatedObservation = observations[2];
        }

        public static void SetPractitioner(Patient patient, string tag, string practitionerId)
        {
            patient.Meta = new Meta();
            patient.Meta.Tag.Add(new Coding(null, tag));

            if (practitionerId != null)
            {
                patient.GeneralPractitioner.Add(new ResourceReference($"Practitioner/{practitionerId}"));
            }
        }

        private static string CreateId(char fill)
        {
            string unique = Guid.NewGuid().ToString("N");
            return unique + new string(fill, MaxResourceIdLength - unique.Length);
        }

        private void SetObservation(Observation observation, Patient subject, string practitionerId)
        {
            observation.Meta = new Meta();
            observation.Meta.Tag.Add(new Coding(null, FixtureTag));
            observation.Status = ObservationStatus.Final;
            observation.Code = new CodeableConcept("http://loinc.org", "1234-5");
            observation.Subject = new ResourceReference($"Patient/{subject.Id}");
            observation.Performer = new List<ResourceReference> { new ResourceReference($"Practitioner/{practitionerId}") };
        }
    }
}
