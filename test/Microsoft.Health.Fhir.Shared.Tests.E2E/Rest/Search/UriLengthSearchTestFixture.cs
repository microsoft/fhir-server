// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    /// <summary>
    /// Creates resources whose URI search values are longer than the 256 characters that SQL Server indexes (MSRC 165443).
    /// </summary>
    public class UriLengthSearchTestFixture : HttpIntegrationTestFixture
    {
        public const int IndexedLength = 256;

        public UriLengthSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        /// <summary>
        /// Gets a URI prefix of exactly <see cref="IndexedLength"/> characters shared by <see cref="AllowProfile"/> and <see cref="PrivateProfile"/>.
        /// </summary>
        public string SharedPrefix { get; } = CreateSharedPrefix();

        public string AllowProfile => SharedPrefix + "allow";

        public string PrivateProfile => SharedPrefix + "private";

        public string UnrelatedProfile { get; } = "https://example.org/fhir/StructureDefinition/unrelated";

        public string FixtureTag { get; private set; }

        public Patient AllowPatient { get; private set; }

        public Patient PrivatePatient { get; private set; }

        public Patient UnrelatedPatient { get; private set; }

        protected override async Task OnInitializedAsync()
        {
            FixtureTag = Guid.NewGuid().ToString();

            Patient[] patients = await TestFhirClient.CreateResourcesAsync<Patient>(
                p => SetProfiles(p, AllowProfile),
                p => SetProfiles(p, PrivateProfile),
                p => SetProfiles(p, UnrelatedProfile));

            AllowPatient = patients[0];
            PrivatePatient = patients[1];
            UnrelatedPatient = patients[2];
        }

        public void SetProfiles(Patient patient, params string[] profiles) => SetTagAndProfiles(patient, FixtureTag, profiles);

        public void SetTagAndProfiles(Patient patient, string tag, params string[] profiles)
        {
            patient.Meta = new Meta
            {
                Profile = profiles,
            };
            patient.Meta.Tag.Add(new Coding(null, tag));
        }

        private static string CreateSharedPrefix()
        {
            const string start = "https://example.org/fhir/StructureDefinition/";
            return start + new string('b', IndexedLength - start.Length);
        }
    }
}
