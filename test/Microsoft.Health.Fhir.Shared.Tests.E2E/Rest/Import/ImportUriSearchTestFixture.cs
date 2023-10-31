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
    public class ImportUriSearchTestFixture : ImportTestFixture<StartupForImportTestProvider>
    {
        public ImportUriSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        public IReadOnlyList<ValueSet> ValueSets { get; private set; }

        public string FixtureTag { get; set; }

        protected override async Task OnInitializedAsync()
        {
            FixtureTag = Guid.NewGuid().ToString();

            ValueSets = await ImportTestHelper.ImportToServerAsync<ValueSet>(
                TestFhirClient,
                StorageAccount,
                vs => AddValueSet(vs, "http://somewhere.com/test/system"),
                vs => AddValueSet(vs, "urn://localhost/test"),
                vs => AddValueSet(vs, "http://example.org/rdf#54135-9"),
                vs => AddValueSet(vs, "http://example.org/rdf#54135-9-9"));

            void AddValueSet(ValueSet vs, string url)
            {
                vs.Status = PublicationStatus.Active;
                vs.Url = url;
                vs.AddTestTag(FixtureTag);
            }
        }
    }
}
