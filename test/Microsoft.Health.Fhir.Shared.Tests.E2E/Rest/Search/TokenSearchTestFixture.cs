// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class TokenSearchTestFixture : HttpIntegrationTestFixture
    {
        public TokenSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
            // Prepare the resources used for number search tests.
            FhirClient.DeleteAllResources(ResourceType.Observation).Wait();

            Observations = FhirClient.CreateResourcesAsync<Observation>(
                o => SetObservation(o, cc => cc.Coding.Add(new Coding("system1", "code1"))),
                o => SetObservation(o, cc => cc.Coding.Add(new Coding("system2", "code2"))),
                o => SetObservation(o, cc => cc.Text = "text"),
                o => SetObservation(o, cc => cc.Coding.Add(new Coding("system1", "code2", "text2"))),
                o => SetObservation(o, cc => cc.Coding.Add(new Coding("system3", "code3", "text"))),
                o => SetObservation(o, cc =>
                {
                    cc.Text = "text";
                    cc.Coding.Add(new Coding("system1", "code1"));
                    cc.Coding.Add(new Coding("system3", "code2"));
                }),
                o => SetObservation(o, cc =>
                {
                    cc.Coding.Add(new Coding("system2", "code1"));
                    cc.Coding.Add(new Coding("system3", "code3", "text2"));
                }),
                o => SetObservation(o, cc => cc.Coding.Add(new Coding(null, "code3")))).Result;

            void SetObservation(Observation observation, Action<CodeableConcept> codeableConceptCustomizer)
            {
                observation.Code = new CodeableConcept("system", "code");
                observation.Status = ObservationStatus.Registered;

                var codeableConcept = new CodeableConcept();

                codeableConceptCustomizer(codeableConcept);

                observation.Value = codeableConcept;
            }
        }

        public IReadOnlyList<Observation> Observations { get; }
    }
}
