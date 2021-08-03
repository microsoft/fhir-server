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
    public class NumberSearchTestFixture : HttpIntegrationTestFixture
    {
        public NumberSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        public IReadOnlyList<RiskAssessment> RiskAssessments { get; private set; }

        public string Tag { get; private set; }

        protected override async Task OnInitializedAsync()
        {
            Tag = Guid.NewGuid().ToString();

            var meta = new Meta
            {
                Tag = new List<Coding>
                    {
                        new Coding("testTag", Tag),
                    },
            };

            // Prepare the resources used for number search tests.

            RiskAssessments = await TestFhirClient.CreateResourcesAsync<RiskAssessment>(
                i => SetRiskAssessment(i, 1),
                i => SetRiskAssessment(i, 4),
                i => SetRiskAssessment(i, 5),
                i => SetRiskAssessment(i, 6),
                i => SetRiskAssessment(i, 100));

            void SetRiskAssessment(RiskAssessment riskAssessment, int probability)
            {
                riskAssessment.Meta = meta;
                riskAssessment.Status = ObservationStatus.Final;
                riskAssessment.Subject = new ResourceReference("Patient/123");
                riskAssessment.Prediction = new List<RiskAssessment.PredictionComponent>
                {
                    new RiskAssessment.PredictionComponent { Probability = new FhirDecimal(probability) },
                };
            }
        }
    }
}
