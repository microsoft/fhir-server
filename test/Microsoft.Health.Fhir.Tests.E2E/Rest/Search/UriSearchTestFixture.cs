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
    public class UriSearchTestFixture : HttpIntegrationTestFixture
    {
        public UriSearchTestFixture(DataStore dataStore, Format format, FhirVersion fhirVersion)
            : base(dataStore, format, fhirVersion)
        {
            // Prepare the resources used for URI search tests.
            FhirClient.DeleteAllResources("ValueSet").Wait();

            ValueSets = FhirClient.CreateResourcesAsync(
                FhirClient.GetEmptyValueSet,
                vs => UpdateValueSet(vs, "http://somewhere.com/test/system"),
                vs => UpdateValueSet(vs, "urn://localhost/test")).Result;

            ResourceElement UpdateValueSet(ResourceElement vs, string url)
            {
                vs = FhirClient.UpdateValueSetStatus(vs, "Active");
                vs = FhirClient.UpdateValueSetUrl(vs, url);
                return vs;
            }
        }

        public IReadOnlyList<ResourceElement> ValueSets { get; }
    }
}
