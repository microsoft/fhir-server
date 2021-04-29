// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Rest;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.TaskHostingBackgroundService
{
    public class TaskHostingTestFixture : HttpIntegrationTestFixture<StartupForTaskHostingTestProvider>
    {
        // private const string LocalConnectionString = "server=(local);Integrated Security=true";
        private const string LocalConnectionString = "Server=tcp:zhouzhou.database.windows.net,1433;Initial Catalog=zhou;Persist Security Info=False;User ID=zhou;Password=Zz+4121691;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";

        public TaskHostingTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
            ConnectionString = Environment.GetEnvironmentVariable("SqlServer:ConnectionString") ?? LocalConnectionString;
        }

        public string ConnectionString { get; private set; }
    }
}
