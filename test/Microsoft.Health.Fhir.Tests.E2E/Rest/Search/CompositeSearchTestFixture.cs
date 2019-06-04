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
        private static readonly string[] ObservationTestFileNames =
        {
            "ObservationWith1MinuteApgarScore",
            "ObservationWith20MinuteApgarScore",
            "ObservationWithEyeColor",
            "ObservationWithTemperature",
            "ObservationWithTPMTDiplotype",
            "ObservationWithTPMTHaplotypeOne",
            "ObservationWithBloodPressure",
        };

        private static readonly string[] DocumentReferenceTestFileNames =
        {
            "DocumentReference-example",
            "DocumentReference-example-002",
            "DocumentReference-example-003",
        };

        public CompositeSearchTestFixture(DataStore dataStore, Format format, FhirVersion fhirVersion)
            : base(dataStore, format, fhirVersion)
        {
            var resultDictionary = new Dictionary<string, ResourceElement>(ObservationTestFileNames.Length);

            for (int i = 0; i < ObservationTestFileNames.Length; i++)
            {
                string testFileName = ObservationTestFileNames[i];

                ResourceElement result = FhirClient.CreateResourcesAsync(() =>
                {
                    ResourceElement observation = FhirClient.GetJsonSample(testFileName);

                    return FhirClient.AddObservationIdentifier(observation, null, TestSessionId);
                }).Result;

                resultDictionary.Add(testFileName, result);
            }

            Observations = resultDictionary;

            var documentReferenceDictionary = new Dictionary<string, ResourceElement>(DocumentReferenceTestFileNames.Length);

            for (int i = 0; i < DocumentReferenceTestFileNames.Length; i++)
            {
                string testFileName = DocumentReferenceTestFileNames[i];

                ResourceElement result = FhirClient.CreateResourcesAsync(() =>
                {
                    ResourceElement documentReference = FhirClient.GetJsonSample(testFileName);

                    return FhirClient.AddDocumentReferenceIdentifier(documentReference, null, TestSessionId);
                }).Result;

                documentReferenceDictionary.Add(testFileName, result);
            }

            DocumentReferences = documentReferenceDictionary;

            FhirVersion = fhirVersion;
        }

        public FhirVersion FhirVersion { get; }

        public string TestSessionId { get; } = Guid.NewGuid().ToString();

        public IReadOnlyDictionary<string, ResourceElement> Observations { get; }

        public IReadOnlyDictionary<string, ResourceElement> DocumentReferences { get; }
    }
}
