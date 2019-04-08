// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Web;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public class ExportTests : IClassFixture<HttpIntegrationTestFixture<Startup>>
    {
        private readonly HttpClient _client;
        private const string PreferHeaderName = "Prefer";

        public ExportTests(HttpIntegrationTestFixture<Startup> fixture)
        {
            _client = fixture.HttpClient;
        }

        [Theory]
        [InlineData("Patient/$export")]
        [InlineData("Group/id/$export")]
        public async Task WhenRequestingExportWithCorrectHeaders_GivenExportIsEnabled_TheServerShouldReturnNotImplemented(string path)
        {
            HttpRequestMessage request = GenerateExportRequest(path);

            HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        }

        [Fact]
        public async Task WhenRequestingExportWithCorrectHeaders_GivenExportIsEnabled_TheServerShouldReturnAcceptedAndNonEmptyContentLocationHeader()
        {
            string path = "$export";
            HttpRequestMessage request = GenerateExportRequest(path);

            HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            var uri = response.Content.Headers.ContentLocation;
            Assert.False(string.IsNullOrEmpty(uri.ToString()));
        }

        [Fact]
        public async Task WhenRequestingExportStatus_GivenExportJobExists_TheServerShouldReturnAccepted()
        {
            // Sending an export request so that a job record will be created in the system.
            string path = "$export";
            HttpRequestMessage request = GenerateExportRequest(path);

            HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            var uri = response.Content.Headers.ContentLocation;
            HttpRequestMessage getStatusRequest = new HttpRequestMessage()
            {
                Method = HttpMethod.Get,
                RequestUri = uri,
            };

            var getStatusResponse = await _client.SendAsync(getStatusRequest);
            Assert.Equal(HttpStatusCode.Accepted, getStatusResponse.StatusCode);
        }

        [Fact]
        public async Task WhenRequestingExportStatus_GivenExportJobDoesNotExist_TheServerShouldReturnNotFound()
        {
            string getPath = OperationsConstants.Operations + "/" + OperationsConstants.Export + "/" + Guid.NewGuid();
            HttpRequestMessage getStatusRequest = new HttpRequestMessage()
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(_client.BaseAddress, getPath),
            };

            var getStatusResponse = await _client.SendAsync(getStatusRequest);
            Assert.Equal(HttpStatusCode.NotFound, getStatusResponse.StatusCode);
        }

        [Theory]
        [InlineData("application/json")]
        [InlineData("applicaiton/xml")]
        [InlineData("*/*")]
        [InlineData("")]
        public async Task WhenRequestingExportWithInvalidAcceptHeader_GivenExportIsEnabled_TheServerShouldReturnBadRequest(string acceptHeaderValue)
        {
            HttpRequestMessage request = GenerateExportRequest(acceptHeader: acceptHeaderValue);

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
            HttpRequestMessage request = GenerateExportRequest(preferHeader: preferHeaderValue);

            HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        private HttpRequestMessage GenerateExportRequest(
            string path = "$export",
            string acceptHeader = ContentType.JSON_CONTENT_HEADER,
            string preferHeader = "respond-async")
        {
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
            };

            request.RequestUri = new Uri(_client.BaseAddress, path);
            request.Headers.Add(HeaderNames.Accept, acceptHeader);
            request.Headers.Add(PreferHeaderName, preferHeader);

            return request;
        }
    }
}
