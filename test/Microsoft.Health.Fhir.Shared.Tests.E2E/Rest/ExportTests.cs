// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
    public class ExportTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly HttpClient _client;
        private const string PreferHeaderName = "Prefer";
        private const string DestinationTypeQueryParamName = "_destinationType";
        private const string DestinationConnectionQueryParamName = "_destinationConnectionSettings";
        private const string SupportedDestinationType = "in-memory";

        public ExportTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.HttpClient;
        }

        [Theory]
        [InlineData("Patient/$export")]
        [InlineData("Group/id/$export")]
        public async Task GivenExportIsEnabled_WhenRequestingExportWithCorrectHeadersAndParams_ThenServerShouldReturnNotImplemented(string path)
        {
            HttpRequestMessage request = GenerateExportRequest(path);

            HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        }

        [Fact]
        public async Task GivenExportIsEnabled_WhenRequestingExportWithCorrectHeadersAndParams_ThenServerShouldReturnAcceptedAndNonEmptyContentLocationHeader()
        {
            HttpRequestMessage request = GenerateExportRequest();

            HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            var uri = response.Content.Headers.ContentLocation;
            Assert.False(string.IsNullOrWhiteSpace(uri.ToString()));
        }

        [Fact]
        public async Task GivenExportIsEnabled_WhenRequestingExportWithMissingDestinationConnectionParam_ThenServerShouldReturnBadRequest()
        {
            var queryParam = new Dictionary<string, string>()
            {
                { DestinationTypeQueryParamName, SupportedDestinationType },
            };
            HttpRequestMessage request = GenerateExportRequest(queryParams: queryParam);

            HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task GivenExportIsEnabled_WhenRequestingExportWithMissingDestinationTypeParam_ThenServerShouldReturnBadRequest()
        {
            var queryParam = new Dictionary<string, string>()
            {
                { DestinationConnectionQueryParamName, "destinationConnection" },
            };
            HttpRequestMessage request = GenerateExportRequest(queryParams: queryParam);

            HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task GivenExportIsEnabled_WhenRequestingExportWithUnsupportedDestinationTypeParam_ThenServerShouldReturnBadRequest()
        {
            var queryParam = new Dictionary<string, string>()
            {
                { DestinationTypeQueryParamName, "unsupportedDestinationType" },
                { DestinationConnectionQueryParamName, "destinationConnection" },
            };
            HttpRequestMessage request = GenerateExportRequest(queryParams: queryParam);

            HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task GivenExportJobExists_WhenRequestingExportStatus_ThenServerShouldReturnAccepted()
        {
            // Sending an export request so that a job record will be created in the system.
            HttpRequestMessage request = GenerateExportRequest();

            HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            // Prepare get export status request.
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
        public async Task GivenExportJobDoesNotExist_WhenRequestingExportStatus_ThenServerShouldReturnNotFound()
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
        public async Task GivenExportIsEnabled_WhenRequestingExportWithInvalidAcceptHeader_ThenServerShouldReturnBadRequest(string acceptHeaderValue)
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
        public async Task GivenExportIsEnabled_WhenRequestingExportWithInvalidPreferHeader_ThenServerShouldReturnBadRequest(string preferHeaderValue)
        {
            HttpRequestMessage request = GenerateExportRequest(preferHeader: preferHeaderValue);

            HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        private HttpRequestMessage GenerateExportRequest(
            string path = "$export",
            string acceptHeader = ContentType.JSON_CONTENT_HEADER,
            string preferHeader = "respond-async",
            Dictionary<string, string> queryParams = null)
        {
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
            };

            request.Headers.Add(HeaderNames.Accept, acceptHeader);
            request.Headers.Add(PreferHeaderName, preferHeader);

            if (queryParams == null)
            {
                queryParams = new Dictionary<string, string>()
                {
                    { DestinationTypeQueryParamName, SupportedDestinationType },
                    { DestinationConnectionQueryParamName, "connectionString" },
                };
            }

            path = QueryHelpers.AddQueryString(path, queryParams);
            request.RequestUri = new Uri(_client.BaseAddress, path);

            return request;
        }
    }
}
