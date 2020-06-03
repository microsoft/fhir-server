// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class StringSearchTestFixture : HttpIntegrationTestFixture
    {
        internal const string LongString = "Lorem ipsum dolor sit amet consectetur adipiscing elit. Ut eget ultricies justo. Maecenas bibendum convallis sodales. Vestibulum quis molestie dui. Nulla porta elementum tristique. Aenean neque libero convallis sit amet dui ullamcorper congue lacinia erat. Sed finibus ex ac massa tincidunt tristique. In sed auctor massa. Proin cursus porttitor arcu. Maecenas a leo nunc. Sed pretium porta volutpat. In aliquet tempor sapien vitae laoreet nisl tempor ac. Vestibulum lacus leo luctus vitae pharetra at tempus ac diam. Integer at dui eu dolor gravida vehicula. Phasellus malesuada elit orci quis maximus purus consectetur ac. In semper consequat augue sit amet ultricies.";

        public StringSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
            // Prepare the resources used for string search tests.
            TestFhirClient.DeleteAllResources(ResourceType.Patient).Wait();

            Patients = TestFhirClient.CreateResourcesAsync<Patient>(
                    p => SetPatientInfo(p, "Seattle", "Smith", given: "Bea"),
                    p => SetPatientInfo(p, "Portland", "Williams"),
                    p => SetPatientInfo(p, "Vancouver", "Anderson"),
                    p => SetPatientInfo(p, LongString, "Murphy"),
                    p => SetPatientInfo(p, "Montreal", "Richard", given: "Bea"))
                .Result;

            void SetPatientInfo(Patient patient, string city, string family, string given = null)
            {
                patient.Address = new List<Address>()
                {
                    new Address() { City = city },
                };

                patient.Name = new List<HumanName>()
                {
                    new HumanName() { Family = family, Given = new[] { given } },
                };
            }
        }

        public IReadOnlyList<Patient> Patients { get; }
    }
}
