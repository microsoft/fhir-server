// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Sql
{
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class SchemaTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly HttpClient _client;

        public SchemaTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.HttpClient;
        }

        public static IEnumerable<object[]> Data =>
            new List<object[]>
            {
                new object[] { "_schema/compatibility" },
                new object[] { "_schema/versions/current" },
                new object[] { "_schema/versions/123/script" },
            };

        [Fact]
        public async Task WhenRequestingAvailable_GivenAServerThatHasSchemas_JsonShouldBeReturned()
        {
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(_client.BaseAddress, "_schema/versions"),
            };

            HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var jArrayResponse = JArray.Parse(await response.Content.ReadAsStringAsync());

            Assert.NotEmpty(jArrayResponse);

            JToken firstResult = jArrayResponse.First;
            string scriptUrl = $"{_client.BaseAddress}_schema/versions/{firstResult["id"]}/script";
            Assert.Equal(scriptUrl, firstResult["script"]);
        }

        [Theory]
        [MemberData(nameof(Data))]
        public async Task WhenRequestingSchema_GivenGetMethod_TheServerShouldReturnNotImplemented(string path)
        {
            await SendAndVerifyStatusCode(HttpMethod.Get, path, HttpStatusCode.NotImplemented);
        }

        // Investigate why these return 415 in the test project, but 404 when running in postman
        [Theory]
        [MemberData(nameof(Data))]
        public async Task WhenRequestingSchema_GivenPostMethod_TheServerShouldReturnNotFound(string path)
        {
            await SendAndVerifyStatusCode(HttpMethod.Post, path, HttpStatusCode.UnsupportedMediaType);
        }

        // Investigate why these return 415 in the test project, but 404 when running in postman
        [Theory]
        [MemberData(nameof(Data))]
        public async Task WhenRequestingSchema_GivenPutMethod_TheServerShouldReturnNotFound(string path)
        {
            await SendAndVerifyStatusCode(HttpMethod.Put, path, HttpStatusCode.UnsupportedMediaType);
        }

        [Theory]
        [MemberData(nameof(Data))]
        public async Task WhenRequestingSchema_GivenDeleteMethod_TheServerShouldReturnNotFound(string path)
        {
            await SendAndVerifyStatusCode(HttpMethod.Delete, path, HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task WhenRequestingScript_GivenNonIntegerVersion_TheServerShouldReturnNotFound()
        {
            await SendAndVerifyStatusCode(HttpMethod.Get, "_schema/versions/abc/script", HttpStatusCode.NotFound);
        }

        private async Task SendAndVerifyStatusCode(HttpMethod httpMethod, string path, HttpStatusCode httpStatusCode)
        {
            var request = new HttpRequestMessage
            {
                Method = httpMethod,
                RequestUri = new Uri(_client.BaseAddress, path),
            };

            HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(httpStatusCode, response.StatusCode);
        }
    }
}
