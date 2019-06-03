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
    public class NumberSearchTestFixture : HttpIntegrationTestFixture
    {
        public NumberSearchTestFixture(DataStore dataStore, Format format, FhirVersion fhirVersion)
            : base(dataStore, format, fhirVersion)
        {
            // Prepare the resources used for number search tests.
            FhirClient.DeleteAllResources("RiskAssessment").Wait();

            Immunizations = FhirClient.CreateResourcesAsync(
                FhirClient.GetEmptyRiskAssessment,
                i => SetRiskAssessment(i, 1),
                i => SetRiskAssessment(i, 4),
                i => SetRiskAssessment(i, 5),
                i => SetRiskAssessment(i, 6),
                i => SetRiskAssessment(i, 100)).Result;

            ResourceElement SetRiskAssessment(ResourceElement riskAssessment, int probability)
            {
                riskAssessment = FhirClient.UpdateRiskAssessmentStatus(riskAssessment, "Final");
                riskAssessment = FhirClient.UpdateRiskAssessmentSubject(riskAssessment, "Patient/123");
                riskAssessment = FhirClient.UpdateRiskAssessmentProbability(riskAssessment, probability);

                return riskAssessment;
            }
        }

        public IReadOnlyList<ResourceElement> Immunizations { get; }
    }
}
