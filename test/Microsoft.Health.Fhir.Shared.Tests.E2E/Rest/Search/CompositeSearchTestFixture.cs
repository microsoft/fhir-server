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

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class CompositeSearchTestFixture : HttpIntegrationTestFixture
    {
        private static readonly string[] ObservationTestFileNames =
        {
            "ObservationWith1MinuteApgarScore",
            "ObservationWith20MinuteApgarScore",
            "ObservationWithEyeColor",
            "ObservationWithLongEyeColor",
            "ObservationWithTemperature",
            "ObservationWithTPMTDiplotype",
            "ObservationWithTPMTHaplotypeOne",
            "ObservationWithBloodPressure",
        };

        private static readonly string[] DocumentReferenceTestFiles =
        {
            "DocumentReference-example",
            "DocumentReference-example-002",
            "DocumentReference-example-003",
        };

        public CompositeSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
            Observations = CreateResultDictionary<Observation>(ObservationTestFileNames);
            DocumentReferences = CreateResultDictionary<DocumentReference>(DocumentReferenceTestFiles);
        }

        public string TestSessionId { get; } = Guid.NewGuid().ToString();

        public IReadOnlyDictionary<string, Observation> Observations { get; }

        public IReadOnlyDictionary<string, DocumentReference> DocumentReferences { get; }

        private Dictionary<string, T> CreateResultDictionary<T>(string[] files)
            where T : Resource
        {
            var resultDictionary = new Dictionary<string, T>(files.Length);

            for (int i = 0; i < files.Length; i++)
            {
                string testFileName = files[i];

                T result = FhirClient.CreateResourcesAsync<T>(() =>
                {
                    T resource = Samples.GetJsonSample<T>(testFileName);

                    switch (resource)
                    {
                        case Observation o:
                            o.Identifier.Add(new Identifier(null, TestSessionId));
                            break;
                        case DocumentReference d:
                            d.Identifier.Add(new Identifier(null, TestSessionId));
                            break;
                    }

                    return resource;
                }).Result;

                resultDictionary.Add(testFileName, result);
            }

            return resultDictionary;
        }
    }
}
