// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class HealthTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly HttpClient _client;

        public HealthTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient.HttpClient;
        }

        [Fact]
        public async Task GivenAHealthEndpoint_WhenStartingTheFhirServer_ThenTheHealthCheckIsOK()
        {
            using HttpResponseMessage response = await _client.GetAsync("health/check");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task GivenAHealthEndpoint_WhenStartingTheFhirServer_ThenResponseContainsDescription()
        {
            using HttpResponseMessage response = await _client.GetAsync("health/check");
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Contains("description", responseContent);
        }
    }
}
