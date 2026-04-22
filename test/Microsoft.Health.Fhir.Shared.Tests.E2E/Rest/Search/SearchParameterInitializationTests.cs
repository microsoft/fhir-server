// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
        private static readonly TimeSpan FailureGracePeriod = TimeSpan.FromSeconds(30);

        [Theory]
        [InlineData(DataStore.SqlServer)]
        [InlineData(DataStore.CosmosDb)]
        public async Task GivenDefinitionStageFailure_WhenServerStarts_ThenHealthCheckReportsDegraded(DataStore dataStore)
        {
            await ExecuteAsync<StartupWithDefinitionInitializationFailure>(
                dataStore,
                async httpClient =>
                {
                    // Wait for the retry loop to exhaust (3 attempts in the definition manager)
                    await Task.Delay(FailureGracePeriod);

                    using HttpResponseMessage response = await httpClient.GetAsync("health/check");
                    string responseContent = await response.Content.ReadAsStringAsync();

                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    Assert.NotEqual("Healthy", GetOverallStatus(responseContent));
                    Assert.NotEqual("Healthy", GetDetailStatus(responseContent, "StorageInitializedHealthCheck"));
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
                    // Wait for the retry loop to exhaust
                    await Task.Delay(FailureGracePeriod);

                    using HttpResponseMessage response = await httpClient.GetAsync("health/check");
                    string responseContent = await response.Content.ReadAsStringAsync();

                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    Assert.NotEqual("Healthy", GetOverallStatus(responseContent));
                    Assert.NotEqual("Healthy", GetDetailStatus(responseContent, "StorageInitializedHealthCheck"));
                });
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
