// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class TokenSearchTestFixture : HttpIntegrationTestFixture
    {
        public TokenSearchTestFixture(DataStore dataStore, Format format, FhirVersion fhirVersion)
            : base(dataStore, format, fhirVersion)
        {
            // Prepare the resources used for number search tests.
            FhirClient.DeleteAllResources(KnownResourceTypes.Observation).Wait();

            Observations = FhirClient.CreateResourcesAsync(
                FhirClient.GetDefaultObservation,
                o => SetObservation(o, "system1", "code1", null),
                o => SetObservation(o, "system2", "code2", null),
                o => SetObservation(o, null, null, "text"),
                o => SetObservation(o, "system1", "code2", "text2"),
                o => SetObservation(o, "system3", "code3", "text"),
                o => SetObservation(
                    o,
                    null,
                    null,
                    "text",
                    new (string, string, string)[] { ("system2", "code1", null), ("system3", "code2", null), }),
                o => SetObservation(o, null, null, null, new[] { ("system2", "code1", null), ("system3", "code3", "text2") }),
                o => SetObservation(o, null, "code3", null)).Result;

            ResourceElement SetObservation(ResourceElement observation, string system, string code, string text, (string system, string code, string display)[] codings = null)
            {
                observation = FhirClient.AddObservationCoding(observation, "system", "code");
                observation = FhirClient.UpdateObservationStatus(observation, "Registered");
                observation = FhirClient.UpdateObservationValueCodeableConcept(observation, system, code, text, codings);

                return observation;
            }
        }

        public IReadOnlyList<ResourceElement> Observations { get; }
    }
}
