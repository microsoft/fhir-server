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
    public class DateSearchTestFixture : HttpIntegrationTestFixture
    {
        public DateSearchTestFixture(DataStore dataStore, Format format, FhirVersion fhirVersion)
            : base(dataStore, format, fhirVersion)
        {
            Code = Guid.NewGuid().ToString();
            System = "http://fhir-server-test/guid";

            Observations = FhirClient.CreateResourcesAsync(
                FhirClient.GetDefaultObservation,
                p => SetObservation(p, "1979-12-31"),                // 1979-12-31T00:00:00.0000000 <-> 1979-12-31T23:59:59.9999999
                p => SetObservation(p, "1980"),                      // 1980-01-01T00:00:00.0000000 <-> 1980-12-31T23:59:59.9999999
                p => SetObservation(p, "1980-05"),                   // 1980-05-01T00:00:00.0000000 <-> 1980-05-31T23:59:59.9999999
                p => SetObservation(p, "1980-05-11"),                // 1980-05-11T00:00:00.0000000 <-> 1980-05-11T23:59:59.9999999
                p => SetObservation(p, "1980-05-11T16:32:15"),       // 1980-05-11T16:32:15.0000000 <-> 1980-05-11T16:32:15.9999999
                p => SetObservation(p, "1980-05-11T16:32:15.500"),   // 1980-05-11T16:32:15.5000000 <-> 1980-05-11T16:32:15.5000000
                p => SetObservation(p, "1981-01-01")).Result;        // 1981-01-01T00:00:00.0000000 <-> 1981-12-31T23:59:59.9999999

            ResourceElement SetObservation(ResourceElement observation, string date)
            {
                observation = FhirClient.UpdateObservationStatus(observation, "Final");
                observation = FhirClient.AddObservationCoding(observation, System, Code);
                observation = FhirClient.UpdateObservationEffectiveDate(observation, date);
                return observation;
            }
        }

        public string Code { get; }

        public string System { get; }

        public IReadOnlyList<ResourceElement> Observations { get; }
    }
}
