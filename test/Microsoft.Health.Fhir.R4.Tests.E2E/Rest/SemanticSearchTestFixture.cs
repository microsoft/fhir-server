// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public sealed class SemanticSearchTestFixture : HttpIntegrationTestFixture<StartupForSemanticSearchTests>
    {
        public SemanticSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }
    }
}
