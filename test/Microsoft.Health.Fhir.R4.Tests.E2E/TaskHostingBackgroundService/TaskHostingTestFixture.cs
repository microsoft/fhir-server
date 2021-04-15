// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Rest;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.TaskHostingBackgroundService
{
    public class TaskHostingTestFixture : HttpIntegrationTestFixture<StartupForTaskHostingTestProvider>
    {
        public TaskHostingTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }
    }
}
