// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Crucible.Client;

namespace Microsoft.Health.Fhir.Tests.E2E.Crucible
{
    extern alias E2EStu3;

    public class CrucibleDataSource
    {
        private const int PastResultsValidInMinutes = 10;

        public CrucibleDataSource()
        {
            TestRun = new Lazy<ServerTestRun>(() => GetTestRunAsync().GetAwaiter().GetResult());
        }

        public static string CrucibleEnvironmentUrl => Environment.GetEnvironmentVariable("CrucibleEnvironmentUrl");

        public static string TestEnvironmentUrl => Environment.GetEnvironmentVariable("TestEnvironmentUrl");

        public static string TestEnvironmentName => Environment.GetEnvironmentVariable("TestEnvironmentName");

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
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(TestEnvironmentUrl),
            };

            var fhirClient = new E2EStu3::Microsoft.Health.Fhir.Tests.E2E.FhirClient(httpClient, Format.Json, FhirVersion.Stu3);

            if (fhirClient.SecuritySettings.SecurityEnabled)
            {
                await client.AuthorizeServerAsync(fhirClient.SecuritySettings.AuthorizeUrl, fhirClient.SecuritySettings.TokenUrl);
            }

            var supportedTests = await client.GetSupportedTestsAsync();

            var ids = supportedTests.Where(x => x.Supported).Select(x => x.Id).ToArray();

            return ids;
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
    }
}
