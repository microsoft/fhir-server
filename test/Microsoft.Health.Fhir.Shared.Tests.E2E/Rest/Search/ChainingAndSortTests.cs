// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public sealed class ChainingAndSortTests : SearchTestsBase<ChainingAndSortTests.ClassFixture>
    {
        public ChainingAndSortTests(ClassFixture fixture)
            : base(fixture)
        {
        }

        public class ClassFixture : HttpIntegrationTestFixture
        {
            public ClassFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
                : base(dataStore, format, testFhirServerFactory)
            {
            }

            protected override async Task OnInitializedAsync()
            {



            }
        }
    }
}
