﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Tests.Common;
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
            TestRun = new Lazy<Task>(TestRunAsync);
        }

        public static string CrucibleEnvironmentUrl => EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.CrucibleEnvironmentUrl);

        public static string TestEnvironmentUrl => _dataStore.Equals(DataStore.SqlServer) ? EnvironmentVariables.GetEnvironmentVariable($"{KnownEnvironmentVariableNames.TestEnvironmentUrl}{Constants.TestEnvironmentVariableVersionSqlSuffix}") : EnvironmentVariables.GetEnvironmentVariable($"{KnownEnvironmentVariableNames.TestEnvironmentUrl}{Constants.TestEnvironmentVariableVersionSuffix}");

        public static string TestEnvironmentName => _dataStore.Equals(DataStore.SqlServer) ? EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.TestEnvironmentName) + Constants.TestEnvironmentVariableVersionSqlSuffix : EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.TestEnvironmentName) + Constants.TestEnvironmentVariableVersionSuffix;

        public Lazy<Task> TestRun { get; }

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

            await using (var testFhirServerFactory = new TestFhirServerFactory())
            {
                var testFhirServer = await testFhirServerFactory
                    .GetTestFhirServerAsync(_dataStore, null);

                // Obtaining a client is required for configuring the security options.
                testFhirServer.GetTestFhirClient(ResourceFormat.Json, reusable: false);

                if (testFhirServer.SecurityEnabled)
                {
                    await client.AuthorizeServerAsync(testFhirServer.AuthorizeUri, testFhirServer.TokenUri);
                }

                var supportedTests = await client.GetSupportedTestsAsync();

                var ids = supportedTests.Where(x => x.Supported).Select(x => x.Id).ToArray();

                return ids;
            }
        }

        private async Task TestRunAsync()
        {
            var crucible = CreateClient();

            if (crucible == null)
            {
                return;
            }

            var pastRuns = await crucible.PastTestRunsAsync();
            if (pastRuns != null && pastRuns.TestRun.Any() && pastRuns.TestRun.First().LastUpdated.GetValueOrDefault() >
                DateTimeOffset.Now.AddMinutes(-PastResultsValidInMinutes))
            {
                return;
            }

            var ids = await GetSupportedIdsAsync(crucible);
            await crucible.BeginTestRunAsync(ids, true);
        }

        public static DataStore GetDataStore()
        {
            return _dataStore;
        }
    }
}
