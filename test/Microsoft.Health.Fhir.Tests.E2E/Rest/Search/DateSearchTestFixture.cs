// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Web;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class DateSearchTestFixture : HttpIntegrationTestFixture<Startup>
    {
        public DateSearchTestFixture()
            : base()
        {
            FhirClient.DeleteAllResources(ResourceType.Observation).Wait();

            Observations = FhirClient.CreateResourcesAsync<Observation>(
                () => new Observation
                {
                    Status = ObservationStatus.Final,
                    Code = new CodeableConcept
                    {
                        Coding = new List<Coding>
                        {
                            new Coding
                            {
                                Code = "260385009",
                                System = "http://snomed.info/sct",
                            },
                        },
                    },
                },
                p => p.Effective = new FhirDateTime("1979-12-31"),                // 1979-12-31T00:00:00.0000000 <-> 1979-12-31T23:59:59.9999999
                p => p.Effective = new FhirDateTime("1980"),                      // 1980-01-01T00:00:00.0000000 <-> 1980-12-31T23:59:59.9999999
                p => p.Effective = new FhirDateTime("1980-05"),                   // 1980-05-01T00:00:00.0000000 <-> 1980-05-31T23:59:59.9999999
                p => p.Effective = new FhirDateTime("1980-05-11"),                // 1980-05-11T00:00:00.0000000 <-> 1980-05-11T23:59:59.9999999
                p => p.Effective = new FhirDateTime("1980-05-11T16:32:15"),       // 1980-05-11T16:32:15.0000000 <-> 1980-05-11T16:32:15.9999999
                p => p.Effective = new FhirDateTime("1980-05-11T16:32:15.500"),   // 1980-05-11T16:32:15.5000000 <-> 1980-05-11T16:32:15.5000000
                p => p.Effective = new FhirDateTime("1981-01-01")).Result;        // 1981-01-01T00:00:00.0000000 <-> 1981-12-31T23:59:59.9999999
        }

        public IReadOnlyList<Observation> Observations { get; }
    }
}
