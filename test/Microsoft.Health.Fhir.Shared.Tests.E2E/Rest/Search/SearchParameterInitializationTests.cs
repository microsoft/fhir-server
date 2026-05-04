// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    /// <summary>
    /// E2E tests that validate the server health check correctly reflects search parameter
    /// initialization failures. The initialization chain is:
    /// 1. StorageInitializedNotification → SearchParameterDefinitionManager.EnsureInitializedAsync
    /// 2. SearchParameterDefinitionManagerInitialized → SearchParameterStatusManager.EnsureInitializedAsync
    /// 3. SearchParametersInitializedNotification → StorageInitializedHealthCheck sets healthy
    ///
    /// Success-path health check validation is already covered by <see cref="Rest.HealthTests"/>.
    /// These tests focus on verifying that initialization failures are externally observable.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterInitializationTests
    {
        private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

        [Theory]
        [InlineData(DataStore.SqlServer)]
        [InlineData(DataStore.CosmosDb)]
        public async Task GivenDefinitionStageFailure_WhenServerStarts_ThenHealthCheckReportsDegraded(DataStore dataStore)
        {
            await ExecuteAsync<StartupWithDefinitionInitializationFailure>(
                dataStore,
                async httpClient =>
                {
                    (HttpStatusCode statusCode, string responseContent) = await WaitForNonHealthyStatusAsync(httpClient, PollTimeout, PollInterval);

                    Assert.Equal(HttpStatusCode.OK, statusCode);

                    string detailStatus = GetDetailStatus(responseContent, "StorageInitializedHealthCheck");
                    Assert.NotNull(detailStatus);
                    Assert.Equal("Degraded", detailStatus);
                });
        }

        [Theory]
        [InlineData(DataStore.SqlServer)]
        [InlineData(DataStore.CosmosDb)]
        public async Task GivenStatusStageFailure_WhenServerStarts_ThenHealthCheckReportsDegraded(DataStore dataStore)
        {
            await ExecuteAsync<StartupWithStatusInitializationFailure>(
                dataStore,
                async httpClient =>
                {
                    (HttpStatusCode statusCode, string responseContent) = await WaitForNonHealthyStatusAsync(httpClient, PollTimeout, PollInterval);

                    Assert.Equal(HttpStatusCode.OK, statusCode);

                    string detailStatus = GetDetailStatus(responseContent, "StorageInitializedHealthCheck");
                    Assert.NotNull(detailStatus);
                    Assert.Equal("Degraded", detailStatus);
                });
        }

        private static async Task<(HttpStatusCode StatusCode, string ResponseContent)> WaitForNonHealthyStatusAsync(
            HttpClient httpClient,
            TimeSpan timeout,
            TimeSpan pollInterval)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < timeout)
            {
                using HttpResponseMessage response = await httpClient.GetAsync("health/check");
                string content = await response.Content.ReadAsStringAsync();
                string overallStatus = GetOverallStatus(content);

                if (overallStatus != null && !string.Equals(overallStatus, "Healthy", StringComparison.Ordinal))
                {
                    return (response.StatusCode, content);
                }

                await Task.Delay(pollInterval);
            }

            // Final attempt after timeout
            using HttpResponseMessage finalResponse = await httpClient.GetAsync("health/check");
            string finalContent = await finalResponse.Content.ReadAsStringAsync();
            return (finalResponse.StatusCode, finalContent);
        }

        private static async Task ExecuteAsync<TStartup>(DataStore dataStore, Func<HttpClient, Task> action)
            where TStartup : class
        {
            await using var server = new InProcTestFhirServer(dataStore, typeof(TStartup));
            await server.ConfigureSecurityOptions();

            HttpClient httpClient = server.GetTestFhirClient(ResourceFormat.Json, reusable: false).HttpClient;
            await action(httpClient);
        }

        private static string GetOverallStatus(string responseContent)
            => JObject.Parse(responseContent)["overallStatus"]?.Value<string>();

        private static string GetDetailStatus(string responseContent, string healthCheckName)
            => JObject.Parse(responseContent)["details"]?
                .FirstOrDefault(detail => string.Equals(detail["name"]?.Value<string>(), healthCheckName, StringComparison.Ordinal))
                ?["status"]?.Value<string>();
    }
}
