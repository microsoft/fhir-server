// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Crucible.Client;
using Microsoft.Health.Fhir.Tests.E2E.Rest;

namespace Microsoft.Health.Fhir.Tests.E2E.Crucible
{
    public class CrucibleDataSource
    {
        private const int PastResultsValidInMinutes = 10;
        private static DataStore _dataStore;

        public CrucibleDataSource(DataStore dataStore)
        {
            _dataStore = dataStore;
            TestRun = new Lazy<ServerTestRun>(() => GetTestRunAsync().GetAwaiter().GetResult());
        }

        public static string CrucibleEnvironmentUrl => Environment.GetEnvironmentVariable("CrucibleEnvironmentUrl");

        public static string TestEnvironmentUrl => _dataStore.Equals(DataStore.SqlServer) ? Environment.GetEnvironmentVariable($"TestEnvironmentUrl{Constants.TestEnvironmentVariableVersionSqlSuffix}") : Environment.GetEnvironmentVariable($"TestEnvironmentUrl{Constants.TestEnvironmentVariableVersionSuffix}");

        public static string TestEnvironmentName => _dataStore.Equals(DataStore.SqlServer) ? Environment.GetEnvironmentVariable("TestEnvironmentName") + Constants.TestEnvironmentVariableVersionSqlSuffix : Environment.GetEnvironmentVariable("TestEnvironmentName") + Constants.TestEnvironmentVariableVersionSuffix;

        public Lazy<ServerTestRun> TestRun { get; }

        public static CrucibleClient CreateClient()
        {
            if (string.IsNullOrEmpty(CrucibleEnvironmentUrl) || string.IsNullOrEmpty(TestEnvironmentUrl))
            {
                return null;
            }

            var client = new CrucibleClient();
            client.SetTestServerAsync(CrucibleEnvironmentUrl, TestEnvironmentUrl, TestEnvironmentName).GetAwaiter().GetResult();

            return client;
        }

        public static async Task<string[]> GetSupportedIdsAsync(CrucibleClient client)
        {
            await client.RefreshConformanceStatementAsync();

            using (var testFhirServerFactory = new TestFhirServerFactory())
            {
                var testFhirClient = testFhirServerFactory
                    .GetTestFhirServer(_dataStore, null)
                    .GetTestFhirClient(ResourceFormat.Json);

                if (testFhirClient?.SecurityEnabled == true)
                {
                    await client.AuthorizeServerAsync(testFhirClient.AuthorizeUri, testFhirClient.TokenUri);
                }

                var supportedTests = await client.GetSupportedTestsAsync();

                var ids = supportedTests.Where(x => x.Supported).Select(x => x.Id).ToArray();

                return ids;
            }
        }

        private async Task<ServerTestRun> GetTestRunAsync()
        {
            var crucible = CreateClient();

            if (crucible == null)
            {
                return null;
            }

            var pastRuns = await crucible.PastTestRunsAsync();
            if (pastRuns != null && pastRuns.TestRun.Any() && pastRuns.TestRun.First().LastUpdated.GetValueOrDefault() >
                DateTimeOffset.Now.AddMinutes(-PastResultsValidInMinutes))
            {
                var lastRun = await crucible.GetTestRunStatusAsync(pastRuns.TestRun.First().Id);
                return new ServerTestRun(crucible.ServerBase, lastRun);
            }

            var ids = await GetSupportedIdsAsync(crucible);
            var result = await crucible.RunTestsAndWaitAsync(ids, true);

            return new ServerTestRun(crucible.ServerBase, result);
        }

        public static DataStore GetDataStore()
        {
            return _dataStore;
        }
    }
}
