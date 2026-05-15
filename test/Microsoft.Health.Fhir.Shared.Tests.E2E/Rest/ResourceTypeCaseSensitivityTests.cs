// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Verifies that the FHIR server treats resource type names as case-sensitive,
    /// per the FHIR specification. Mis-cased resource type segments must be rejected
    /// with HTTP 404 Not Found at the routing layer (not 405 Method Not Allowed).
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class ResourceTypeCaseSensitivityTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly HttpClient _client;

        public ResourceTypeCaseSensitivityTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient.HttpClient;
        }

        [Theory]
        [InlineData("GET", "patient/123")]
        [InlineData("GET", "PATIENT/123")]
        [InlineData("GET", "patient")]
        [InlineData("PUT", "patient/123")]
        [InlineData("DELETE", "patient/123")]
        [InlineData("POST", "patient")]
        [InlineData("PATCH", "patient/123")]
        public async Task GivenAMisCasedResourceType_WhenSendingRequest_ThenServerReturnsNotFound(string verb, string path)
        {
            using var request = new HttpRequestMessage(new HttpMethod(verb), path);
            using HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task GivenACorrectlyCasedResourceType_WhenSendingRequest_ThenServerDoesNotReturnNotFoundForRouting()
        {
            using HttpResponseMessage response = await _client.GetAsync("Patient");

            Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
