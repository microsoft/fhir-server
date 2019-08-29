// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Microsoft.Health.Fhir.Web;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.Rest
{
    [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
    public class OperationVersionsTests : IClassFixture<HttpIntegrationTestFixture<Startup>>
    {
        private readonly HttpClient _client;

        public OperationVersionsTests(HttpIntegrationTestFixture<Startup> fixture)
        {
            _client = fixture.HttpClient;
        }

        [Theory]
        [InlineData("application/json")]
        [InlineData("application/xml")]
        [InlineData("application/fhir+json")]
        [InlineData("application/fhir+xml")]
        [InlineData("*/*")]
        public async Task WhenVersionsEndpointIsCalled_GivenValidAcceptHeaderIsProvided_ThenServerShouldReturnOK(string acceptHeaderValue)
        {
            HttpRequestMessage request = GenerateOperationVersionsRequest(acceptHeaderValue);
            HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Theory]
        [InlineData("application/json1")]
        [InlineData("applicaiton/xml")]
        public async Task WhenVersionsEndpointIsCalled_GivenInvalidAcceptHeaderIsProvided_ThenServerShouldReturnUnsupportedMediaType(string acceptHeaderValue)
        {
            HttpRequestMessage request = GenerateOperationVersionsRequest(acceptHeaderValue);
            HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }

        private HttpRequestMessage GenerateOperationVersionsRequest(
            string acceptHeader,
            string path = "$versions")
        {
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
            };

            request.Headers.Add(HeaderNames.Accept, acceptHeader);
            request.RequestUri = new Uri(_client.BaseAddress, path);

            return request;
        }
    }
}
