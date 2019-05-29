// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Web;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class CompositeSearchTestFixture : HttpIntegrationTestFixture<Startup>
    {
        private static readonly string[] TestFileNames = new string[]
        {
            "ObservationWith1MinuteApgarScore",
            "ObservationWith20MinuteApgarScore",
            "ObservationWithEyeColor",
            "ObservationWithTemperature",
            "ObservationWithTPMTDiplotype",
            "ObservationWithTPMTHaplotypeOne",
            "ObservationWithBloodPressure",
        };

        public CompositeSearchTestFixture(DataStore dataStore, Format format, FhirVersion fhirVersion)
            : base(dataStore, format, fhirVersion)
        {
            var resultDictionary = new Dictionary<string, Observation>(TestFileNames.Length);

            for (int i = 0; i < TestFileNames.Length; i++)
            {
                string testFileName = TestFileNames[i];

                Observation result = FhirClient.CreateResourcesAsync<Observation>(() =>
                {
                    Observation observation = Samples.GetJsonSample<Observation>(testFileName);

                    observation.Identifier.Add(new Identifier(null, TestSessionId));

                    return observation;
                }).Result;

                resultDictionary.Add(testFileName, result);
            }

            Observations = resultDictionary;
        }

        public string TestSessionId { get; } = Guid.NewGuid().ToString();

        public IReadOnlyDictionary<string, Observation> Observations { get; }
    }
}
