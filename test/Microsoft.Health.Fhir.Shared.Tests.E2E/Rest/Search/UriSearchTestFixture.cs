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
    public class UriSearchTestFixture : HttpIntegrationTestFixture
    {
        public UriSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        public IReadOnlyList<ValueSet> ValueSets { get; private set; }

        public string FixtureTag { get; set; }

        protected override async Task OnInitializedAsync()
        {
            // Prepare the resources used for URI search tests.
            FixtureTag = Guid.NewGuid().ToString();

            ValueSets = await TestFhirClient.CreateResourcesAsync<ValueSet>(
                vs => AddValueSet(vs, "http://somewhere.com/test/system"),
                vs => AddValueSet(vs, "urn://localhost/test"),
                vs => AddValueSet(vs, "http://example.org/rdf#54135-9"));

            void AddValueSet(ValueSet vs, string url)
            {
                vs.Status = PublicationStatus.Active;
                vs.Url = url;
                vs.Meta = new Meta();
                vs.Meta.Tag.Add(new Coding(null, FixtureTag));
            }
        }
    }
}
