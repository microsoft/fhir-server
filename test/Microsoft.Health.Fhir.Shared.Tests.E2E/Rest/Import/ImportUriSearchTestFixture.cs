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

        public IReadOnlyList<ConceptMap> ConceptMaps { get; private set; }

        public string FixtureTag { get; set; }

        protected override async Task OnInitializedAsync()
        {
            FixtureTag = Guid.NewGuid().ToString();

            // ValueSet, CodeSystem, and StructureDefinition cannot be imported, so ConceptMap provides the uri search param here.
            ConceptMaps = await ImportTestHelper.ImportToServerAsync<ConceptMap>(
                TestFhirClient,
                StorageAccount,
                cm => AddConceptMap(cm, "http://somewhere.com/test/system"),
                cm => AddConceptMap(cm, "urn://localhost/test"),
                cm => AddConceptMap(cm, "http://example.org/rdf#54135-9"),
                cm => AddConceptMap(cm, "http://example.org/rdf#54135-9-9"));

            void AddConceptMap(ConceptMap conceptMap, string url)
            {
                conceptMap.Status = PublicationStatus.Active;
                conceptMap.Url = url;
                conceptMap.AddTestTag(FixtureTag);
            }
        }
    }
}
