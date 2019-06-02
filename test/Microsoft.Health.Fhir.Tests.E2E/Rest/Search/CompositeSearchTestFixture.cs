// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class CompositeSearchTestFixture : HttpIntegrationTestFixture
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
            var resultDictionary = new Dictionary<string, ResourceElement>(TestFileNames.Length);

            for (int i = 0; i < TestFileNames.Length; i++)
            {
                string testFileName = TestFileNames[i];

                ResourceElement result = FhirClient.CreateResourcesAsync<ResourceElement>(() =>
                {
                    ResourceElement observation = FhirClient.GetJsonSample(testFileName);

                    return FhirClient.AddObservationIdentifier(observation, null, TestSessionId);
                }).Result;

                resultDictionary.Add(testFileName, result);
            }

            Observations = resultDictionary;

            FhirVersion = fhirVersion;
        }

        public FhirVersion FhirVersion { get; }

        public string TestSessionId { get; } = Guid.NewGuid().ToString();

        public IReadOnlyDictionary<string, ResourceElement> Observations { get; }
    }
}
