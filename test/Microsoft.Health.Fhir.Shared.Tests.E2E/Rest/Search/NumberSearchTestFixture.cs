// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class NumberSearchTestFixture : HttpIntegrationTestFixture
    {
        public NumberSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
            // Prepare the resources used for number search tests.
            FhirClient.DeleteAllResources(ResourceType.RiskAssessment).Wait();

            RiskAssessments = FhirClient.CreateResourcesAsync<RiskAssessment>(
                i => SetRiskAssessment(i, 1),
                i => SetRiskAssessment(i, 4),
                i => SetRiskAssessment(i, 5),
                i => SetRiskAssessment(i, 6),
                i => SetRiskAssessment(i, 100)).Result;

            void SetRiskAssessment(RiskAssessment riskAssessment, int probability)
            {
                riskAssessment.Status = ObservationStatus.Final;
                riskAssessment.Subject = new ResourceReference("Patient/123");
                riskAssessment.Prediction = new List<RiskAssessment.PredictionComponent>
                {
                    new RiskAssessment.PredictionComponent { Probability = new FhirDecimal(probability) },
                };
            }
        }

        public IReadOnlyList<RiskAssessment> RiskAssessments { get; }
    }
}
