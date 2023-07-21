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
    public class ImportDateSearchTestFixture : ImportTestFixture<StartupForImportTestProvider>
    {
        public ImportDateSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        public string FixtureTag { get; } = Guid.NewGuid().ToString();

        public IReadOnlyList<Observation> Observations { get; private set; }

        protected override async Task OnInitializedAsync()
        {
            Observations = await ImportTestHelper.ImportToServerAsync<Observation>(
                TestFhirClient,
                StorageAccount,
                p => SetObservation(p, "1979-12-31"),                // 1979-12-31T00:00:00.0000000 <-> 1979-12-31T23:59:59.9999999
                p => SetObservation(p, "1980"),                      // 1980-01-01T00:00:00.0000000 <-> 1980-12-31T23:59:59.9999999
                p => SetObservation(p, "1980-05"),                   // 1980-05-01T00:00:00.0000000 <-> 1980-05-31T23:59:59.9999999
                p => SetObservation(p, "1980-05-11"),                // 1980-05-11T00:00:00.0000000 <-> 1980-05-11T23:59:59.9999999
                p => SetObservation(p, "1980-05-11T16:32:15"),       // 1980-05-11T16:32:15.0000000 <-> 1980-05-11T16:32:15.9999999
                p => SetObservation(p, "1980-05-11T16:32:15.500"),   // 1980-05-11T16:32:15.5000000 <-> 1980-05-11T16:32:15.5000000
                p => SetObservation(p, "1981-01-01"));        // 1981-01-01T00:00:00.0000000 <-> 1981-12-31T23:59:59.9999999

            void SetObservation(Observation observation, string date)
            {
                observation.Status = ObservationStatus.Final;
                observation.AddTestTag(FixtureTag);
                observation.Effective = new FhirDateTime(date);
            }
        }
    }
}
