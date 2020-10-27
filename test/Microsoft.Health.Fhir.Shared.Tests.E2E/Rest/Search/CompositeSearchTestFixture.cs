// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Task = System.Threading.Tasks.Task;

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
            "DocumentReference-example-relatesTo-code-appends",
            "DocumentReference-example-relatesTo-code-transforms-replaces-target",
            "DocumentReference-example-relatesTo-code-transforms",
        };

        public CompositeSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        public string TestSessionId { get; } = Guid.NewGuid().ToString();

        public IReadOnlyDictionary<string, Observation> Observations { get; private set; }

        public IReadOnlyDictionary<string, DocumentReference> DocumentReferences { get; private set; }

        protected async override Task OnInitializedAsync()
        {
            Observations = await CreateResultDictionary<Observation>(ObservationTestFileNames);
            DocumentReferences = await CreateResultDictionary<DocumentReference>(DocumentReferenceTestFiles);
        }

        private async Task<Dictionary<string, T>> CreateResultDictionary<T>(string[] files)
            where T : Resource
        {
            var resultDictionary = new Dictionary<string, T>(files.Length);

            for (int i = 0; i < files.Length; i++)
            {
                string testFileName = files[i];

                T result = await TestFhirClient.CreateResourcesAsync<T>(() =>
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
                });

                resultDictionary.Add(testFileName, result);
            }

            return resultDictionary;
        }
    }
}
