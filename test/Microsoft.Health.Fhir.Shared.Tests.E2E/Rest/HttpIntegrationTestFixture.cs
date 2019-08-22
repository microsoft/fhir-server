// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Web;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// An <see cref="HttpIntegrationTestFixture{TStartup}"/> where the TStartup is the same <see cref="Startup"/>
    /// class as the web project.
    /// </summary>
    public class HttpIntegrationTestFixture : HttpIntegrationTestFixture<Startup>
    {
        public HttpIntegrationTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }
    }
}
