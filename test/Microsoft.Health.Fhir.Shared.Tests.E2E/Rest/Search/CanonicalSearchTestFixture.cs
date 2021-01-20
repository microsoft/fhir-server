// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class CanonicalSearchTestFixture : HttpIntegrationTestFixture
    {
        public CanonicalSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
            ObservationProfileUri = $"http://hl7.org/fhir/tests/StructureDefinition/test-lab-{Guid.NewGuid()}";
            ObservationProfileUriAlternate = $"http://hl7.org/fhir/tests/StructureDefinition/test-lab-{Guid.NewGuid()}";

            ObservationProfileV1Fragment = "#fragment";
            ObservationProfileV1Version = "|1";
            ObservationProfileV1 = $"{ObservationProfileUri}{ObservationProfileV1Version}{ObservationProfileV1Fragment}";
            ObservationProfileV2 = $"{ObservationProfileUri}|2";
        }

        public Observation GivenObservation1 { get; private set; }

        public Observation GivenObservation2 { get; private set; }

        public Observation GivenObservation3 { get; private set; }

        public string ObservationProfileUri { get; }

        public string ObservationProfileUriAlternate { get; }

        public string ObservationProfileV1Fragment { get; }

        public string ObservationProfileV1Version { get; }

        public string ObservationProfileV1 { get; }

        public string ObservationProfileV2 { get; }

        protected override async Task OnInitializedAsync()
        {
            Observation resource1 = Samples.GetDefaultObservation().ToPoco<Observation>();
            resource1.Meta = new Meta();
            resource1.Meta.Profile = new[]
            {
                ObservationProfileV1,
            };

            GivenObservation1 = (await TestFhirClient.CreateAsync(resource1)).Resource;

            Observation resource2 = Samples.GetDefaultObservation().ToPoco<Observation>();
            resource2.Meta = new Meta();
            resource2.Meta.Profile = new[]
            {
                ObservationProfileV2,
            };

            GivenObservation2 = (await TestFhirClient.CreateAsync(resource2)).Resource;

            Observation resource3 = Samples.GetDefaultObservation().ToPoco<Observation>();
            resource3.Meta = new Meta();
            resource3.Meta.Profile = new[]
            {
                ObservationProfileUriAlternate,
                $"{ObservationProfileUri}{ObservationProfileV1Version}",
            };

            GivenObservation3 = (await TestFhirClient.CreateAsync(resource3)).Resource;
        }

        protected override async Task OnDisposedAsync()
        {
            if (GivenObservation1 != null)
            {
                await TestFhirClient.DeleteAsync(GivenObservation1);
            }

            if (GivenObservation2 != null)
            {
                await TestFhirClient.DeleteAsync(GivenObservation2);
            }

            if (GivenObservation3 != null)
            {
                await TestFhirClient.DeleteAsync(GivenObservation3);
            }
        }
    }
}
