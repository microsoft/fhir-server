// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// End-to-end tests for the $stats endpoint.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class StatsTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;
        private readonly HttpClient _httpClient;

        public StatsTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
            _httpClient = _client.HttpClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAFhirServer_WhenRequestingStats_TheServerShouldReturnParametersResource()
        {
            using HttpResponseMessage response = await _httpClient.GetAsync("$stats");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string content = await response.Content.ReadAsStringAsync();
            Assert.Contains("Parameters", content);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResourcesInTheServer_WhenRequestingStats_TheServerShouldReturnResourceTypeStatistics()
        {
            // Create some test resources
            var patient = Samples.GetJsonSample<Patient>("Patient");
            using FhirResponse<Patient> createdPatient = await _client.CreateAsync(patient);
            Assert.Equal(HttpStatusCode.Created, createdPatient.StatusCode);

            var observation = Samples.GetJsonSample<Observation>("Weight");
            using FhirResponse<Observation> createdObservation = await _client.CreateAsync(observation);
            Assert.Equal(HttpStatusCode.Created, createdObservation.StatusCode);

            // Request stats
            using HttpResponseMessage response = await _httpClient.GetAsync("$stats");
            var fhirResponse = await _client.CreateResponseAsync<Parameters>(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Verify the response contains resource type information
            Assert.True(fhirResponse.Resource.Parameter.TrueForAll(param => param.Name.Equals("resourceType", StringComparison.OrdinalIgnoreCase)));
            Assert.True(fhirResponse.Resource.Parameter.TrueForAll(param => param.Part[0].Name.Equals("name") && param.Part[1].Name.Equals("totalCount") && param.Part[2].Name.Equals("activeCount")));
            Assert.True(fhirResponse.Resource.Parameter.TrueForAll(param => (param.Part[1].Value as Integer64).Value > 0 && (param.Part[2].Value as Integer64).Value > 0));
        }
    }
}
