// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Web;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public class ExportTests : IClassFixture<HttpIntegrationTestFixture<Startup>>
    {
        private readonly HttpClient _client;

        public ExportTests(HttpIntegrationTestFixture<Startup> fixture)
        {
            _client = fixture.HttpClient;
        }

        [Theory]
        [InlineData("$export")]
        [InlineData("Patient/$export")]
        [InlineData("Group/id/$export")]
        public async Task WhenRequestingExportWithCorrectHeaders_GivenExportIsEnabled_TheServerShouldReturnNotImplemented(string path)
        {
            HttpRequestMessage request = GenerateExportMessage(path);

            HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        }

        [Theory]
        [InlineData("application/json")]
        [InlineData("applicaiton/xml")]
        [InlineData("*/*")]
        [InlineData("")]
        public async Task WhenRequestingExportWithInvalidAcceptHeader_GivenExportIsEnabled_TheServerShouldReturnBadRequest(string acceptHeaderValue)
        {
            HttpRequestMessage request = GenerateExportMessage(acceptHeader: acceptHeaderValue);

            HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData("respond-async, wait=10")]
        [InlineData("respond-status")]
        [InlineData("*")]
        [InlineData("")]
        public async Task WhenRequestingExportWithInvalidPreferHeader_GivenExportIsEnabled_TheServerShouldReturnBadRequest(string preferHeaderValue)
        {
            HttpRequestMessage request = GenerateExportMessage(preferHeader: preferHeaderValue);

            HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        private HttpRequestMessage GenerateExportMessage(
            string path = "$export",
            string acceptHeader = "application/fhir+json",
            string preferHeader = "respond-async")
        {
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
            };

            request.RequestUri = new Uri(_client.BaseAddress, path);
            request.Headers.Add(HeaderNames.Accept, acceptHeader);
            request.Headers.Add("Prefer", preferHeader);

            return request;
        }
    }
}
