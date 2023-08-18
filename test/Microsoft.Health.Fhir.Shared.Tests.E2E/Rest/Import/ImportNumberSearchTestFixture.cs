// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    public class ImportNumberSearchTestFixture : ImportTestFixture<StartupForImportTestProvider>
    {
        public ImportNumberSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        public IReadOnlyList<RiskAssessment> RiskAssessments { get; private set; }

        public string FixtureTag { get; } = Guid.NewGuid().ToString();

        protected override async Task OnInitializedAsync()
        {
            RiskAssessments = await ImportTestHelper.ImportToServerAsync<RiskAssessment>(
                TestFhirClient,
                StorageAccount,
                i => SetRiskAssessment(i, 1),
                i => SetRiskAssessment(i, 4),
                i => SetRiskAssessment(i, 5),
                i => SetRiskAssessment(i, 6),
                i => SetRiskAssessment(i, 100));

            void SetRiskAssessment(RiskAssessment riskAssessment, int probability)
            {
                riskAssessment.Status = ObservationStatus.Final;
                riskAssessment.Subject = new ResourceReference("Patient/123");
                riskAssessment.AddTestTag(FixtureTag);
                riskAssessment.Prediction = new List<RiskAssessment.PredictionComponent>
                {
                    new RiskAssessment.PredictionComponent { Probability = new FhirDecimal(probability) },
                };
            }
        }
    }
}
