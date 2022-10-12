// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Web;
using Microsoft.Health.Test.Utilities;
using Microsoft.Net.Http.Headers;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class OperationVersionsTests : IClassFixture<HttpIntegrationTestFixture<Startup>>
    {
        private readonly HttpClient _client;

        public OperationVersionsTests(HttpIntegrationTestFixture<Startup> fixture)
        {
            _client = fixture.HttpClient;
        }

        [Theory]
        [InlineData("application/json")]
        [InlineData("application/fhir+json")]
        [HttpIntegrationFixtureArgumentSets(formats: Format.Json)]
        public async Task GivenAValidJsonAcceptHeaderIsProvided_WhenVersionsEndpointIsCalled_ThenServerShouldReturnOK(string acceptHeaderValue)
        {
            await CheckContentType(acceptHeaderValue);
        }

        [Theory]
        [InlineData("application/xml")]
        [InlineData("application/fhir+xml")]
        [HttpIntegrationFixtureArgumentSets(formats: Format.Xml)]
        public async Task GivenAValidXmlAcceptHeaderIsProvided_WhenVersionsEndpointIsCalledWithXml_ThenServerShouldReturnOK(string acceptHeaderValue)
        {
            await CheckContentType(acceptHeaderValue);
        }

        [Fact]
        public async Task GivenNoAcceptHeaderIsProvided_WhenVersionsEndpointIsCalled_ThenServerShouldReturnOK()
        {
            using HttpRequestMessage request = GenerateOperationVersionsRequest(string.Empty);
            using HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal("application/fhir+json", response.Content.Headers.ContentType.MediaType);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Theory]
        [InlineData("application/json1")]
        [InlineData("applicaiton/xml")]
        public async Task GivenInvalidAcceptHeaderIsProvided_WhenVersionsEndpointIsCalled_ThenServerShouldReturnNotAcceptable(string acceptHeaderValue)
        {
            using HttpRequestMessage request = GenerateOperationVersionsRequest(acceptHeaderValue);
            using HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotAcceptable, response.StatusCode);
        }

        private async Task CheckContentType(string acceptHeaderValue)
        {
            using HttpRequestMessage request = GenerateOperationVersionsRequest(acceptHeaderValue);
            using HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(acceptHeaderValue, response.Content.Headers.ContentType.MediaType);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
