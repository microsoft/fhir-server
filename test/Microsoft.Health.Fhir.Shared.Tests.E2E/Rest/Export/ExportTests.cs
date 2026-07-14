// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Export
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class ExportTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly HttpIntegrationTestFixture _fixture;
        private readonly HttpClient _client;
        private const string PreferHeaderName = "Prefer";
        private const string SmartPatientAId = "smart-patient-A";
        private const string SmartPatientBId = "smart-patient-B";

        public ExportTests(HttpIntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.HttpClient;
        }

        [Theory]
        [InlineData("Observation/$export")]
        [InlineData("Patient/id/$export")]
        public async Task GivenExportIsEnabled_WhenRequestingExportByTypeWithAnInvalidResourceType_ThenServerShouldReturnBadRequest(string path)
        {
            using HttpRequestMessage request = GenerateExportRequest(path);

            using HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData("$export")]
        [InlineData("Patient/$export")]
        [InlineData("Group/123456/$export")]
        public async Task GivenExportIsEnabled_WhenRequestingExportWithCorrectHeaders_ThenServerShouldReturnAcceptedAndNonEmptyContentLocationHeader(string path)
        {
            using HttpRequestMessage request = GenerateExportRequest(path);

            using HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            var uri = response.Content.Headers.ContentLocation;
            Assert.False(string.IsNullOrWhiteSpace(uri.ToString()));

            await GenerateAndSendCancelExportMessage(response.Content.Headers.ContentLocation);
        }

        [Theory]
        [InlineData("$export")]
        [InlineData("Patient/$export")]
        [InlineData("Group/123456/$export")]
        public async Task GivenExportIsEnabled_WhenRequestingExportWithUnsupportedQueryParam_ThenServerShouldReturnBadRequest(string path)
        {
            var queryParam = new Dictionary<string, string>()
            {
                { "anyQueryParam", "anyValue" },
            };
            using HttpRequestMessage request = GenerateExportRequest(path, queryParams: queryParam);

            using HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData("time", KnownQueryParameterNames.Since)]
        [InlineData("2021-06-13T00:00:00Z ", KnownQueryParameterNames.Since)]
        [InlineData("time", KnownQueryParameterNames.Till)]
        [InlineData("2021-06-13T00:00:00Z ", KnownQueryParameterNames.Till)]
        public async Task GivenUnparsableTime_WhenRequestionExportWithIt_ThenServerShouldReturnBadRequest(string time, string queryParameter)
        {
            var queryParam = new Dictionary<string, string>()
            {
                { KnownQueryParameterNames.Type, "Patient" },
                { queryParameter, time},
            };
            using HttpRequestMessage request = GenerateExportRequest("$export", queryParams: queryParam);

            using HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData("$export")]
        [InlineData("Patient/$export")]
        [InlineData("Group/123456/$export")]
        public async Task GivenExportIsEnabled_WhenRequestingExportWithSupportedQueryParam_ThenServerShouldReturnAcceptedAndNonEmptyContentLocationHeader(string path)
        {
            var queryParam = new Dictionary<string, string>()
            {
                { KnownQueryParameterNames.Since, DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffzzz") },
                { KnownQueryParameterNames.Type, "Patient" },
                { KnownQueryParameterNames.Container, "test-container" },
            };
            using HttpRequestMessage request = GenerateExportRequest(path, queryParams: queryParam);

            using HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            var uri = response.Content.Headers.ContentLocation;
            Assert.False(string.IsNullOrWhiteSpace(uri.ToString()));

            await GenerateAndSendCancelExportMessage(response.Content.Headers.ContentLocation);
        }

        [Fact]
        public async Task GivenExportJobExists_WhenRequestingExportStatus_ThenServerShouldReturnAccepted()
        {
            // Sending an export request so that a job record will be created in the system.
            using HttpRequestMessage request = GenerateExportRequest();

            using HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            // Prepare get export status request.
            var uri = response.Content.Headers.ContentLocation;
            using HttpRequestMessage getStatusRequest = new HttpRequestMessage()
            {
                Method = HttpMethod.Get,
                RequestUri = uri,
            };

            using HttpResponseMessage getStatusResponse = await _client.SendAsync(getStatusRequest);

            Assert.Equal(HttpStatusCode.Accepted, getStatusResponse.StatusCode);

            await GenerateAndSendCancelExportMessage(response.Content.Headers.ContentLocation);
        }

        [SkippableFact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
        public async Task GivenPatientSmartScope_WhenExportingItsPatientWithExplicitType_ThenCreateStatusAndCancelShouldSucceed()
        {
            Skip.If(!_fixture.IsUsingInProcTestServer, "Requires in-proc development identity provider to issue deterministic SMART patient identities.");
            await UpsertSmartExportFixturesAsync();

            using HttpClient patientClient = await CreateSmartHttpClientAsync(TestApplications.SmartPatientA, "patient/Patient.read");
            using HttpRequestMessage exportRequest = GenerateExportRequest(
                $"Patient/{SmartPatientAId}/$export",
                queryParams: new Dictionary<string, string> { { "_type", "Patient" } });
            using HttpResponseMessage exportResponse = await patientClient.SendAsync(exportRequest);

            Assert.Equal(HttpStatusCode.Accepted, exportResponse.StatusCode);
            Uri contentLocation = exportResponse.Content.Headers.ContentLocation;
            Assert.NotNull(contentLocation);

            using HttpResponseMessage getStatusResponse = await patientClient.GetAsync(contentLocation);
            AssertStatusIsAcceptedOrOk(getStatusResponse.StatusCode);

            using HttpResponseMessage cancelResponse = await patientClient.DeleteAsync(contentLocation);
            Assert.Equal(HttpStatusCode.Accepted, cancelResponse.StatusCode);
        }

        [SkippableFact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
        public async Task GivenPatientSmartScope_WhenExportingItsPatientWithoutExplicitType_ThenServerShouldReturnForbidden()
        {
            Skip.If(!_fixture.IsUsingInProcTestServer, "Requires in-proc development identity provider to issue deterministic SMART patient identities.");
            await UpsertSmartExportFixturesAsync();

            using HttpClient patientClient = await CreateSmartHttpClientAsync(TestApplications.SmartPatientA, "patient/Patient.read");
            using HttpRequestMessage exportRequest = GenerateExportRequest($"Patient/{SmartPatientAId}/$export");
            using HttpResponseMessage exportResponse = await patientClient.SendAsync(exportRequest);

            Assert.Equal(HttpStatusCode.Forbidden, exportResponse.StatusCode);
        }

        [SkippableFact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
        public async Task GivenPatientSmartScope_WhenExportingDifferentPatient_ThenServerShouldReturnForbidden()
        {
            Skip.If(!_fixture.IsUsingInProcTestServer, "Requires in-proc development identity provider to issue deterministic SMART patient identities.");
            await UpsertSmartExportFixturesAsync();

            using HttpClient patientClient = await CreateSmartHttpClientAsync(TestApplications.SmartPatientA, "patient/Patient.read");
            using HttpRequestMessage exportRequest = GenerateExportRequest(
                $"Patient/{SmartPatientBId}/$export",
                queryParams: new Dictionary<string, string> { { "_type", "Patient" } });
            using HttpResponseMessage exportResponse = await patientClient.SendAsync(exportRequest);

            Assert.Equal(HttpStatusCode.Forbidden, exportResponse.StatusCode);
        }

        [SkippableFact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
        public async Task GivenPatientExportJob_WhenDifferentPatientRequestsStatusOrCancellation_ThenServerShouldReturnNotFound()
        {
            Skip.If(!_fixture.IsUsingInProcTestServer, "Requires in-proc development identity provider to issue deterministic SMART patient identities.");
            await UpsertSmartExportFixturesAsync();

            using HttpClient patientAClient = await CreateSmartHttpClientAsync(TestApplications.SmartPatientA, "patient/Patient.read");
            using HttpRequestMessage exportRequest = GenerateExportRequest(
                $"Patient/{SmartPatientAId}/$export",
                queryParams: new Dictionary<string, string> { { "_type", "Patient" } });
            using HttpResponseMessage exportResponse = await patientAClient.SendAsync(exportRequest);

            Assert.Equal(HttpStatusCode.Accepted, exportResponse.StatusCode);
            Uri contentLocation = exportResponse.Content.Headers.ContentLocation;
            Assert.NotNull(contentLocation);

            using HttpClient patientBClient = await CreateSmartHttpClientAsync(TestApplications.SmartPatientB, "patient/Patient.read");
            using HttpResponseMessage getStatusResponse = await patientBClient.GetAsync(contentLocation);
            Assert.Equal(HttpStatusCode.NotFound, getStatusResponse.StatusCode);

            using HttpResponseMessage unauthorizedCancelResponse = await patientBClient.DeleteAsync(contentLocation);
            Assert.Equal(HttpStatusCode.NotFound, unauthorizedCancelResponse.StatusCode);

            using HttpResponseMessage authorizedCancelResponse = await patientAClient.DeleteAsync(contentLocation);
            Assert.Equal(HttpStatusCode.Accepted, authorizedCancelResponse.StatusCode);
        }

        [SkippableFact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
        public async Task GivenUserSmartScope_WhenExportingPatientInPractitionerCompartment_ThenServerShouldReturnAccepted()
        {
            Skip.If(!_fixture.IsUsingInProcTestServer, "Requires in-proc development identity provider to issue deterministic SMART practitioner identities.");
            await UpsertSmartExportFixturesAsync();

            using HttpClient practitionerClient = await CreateSmartHttpClientAsync(TestApplications.SmartPractitionerA, "user/Patient.read");
            using HttpRequestMessage exportRequest = GenerateExportRequest(
                $"Patient/{SmartPatientAId}/$export",
                queryParams: new Dictionary<string, string> { { "_type", "Patient" } });
            using HttpResponseMessage exportResponse = await practitionerClient.SendAsync(exportRequest);

            Assert.Equal(HttpStatusCode.Accepted, exportResponse.StatusCode);
            Uri contentLocation = exportResponse.Content.Headers.ContentLocation;
            Assert.NotNull(contentLocation);

            using HttpResponseMessage cancelResponse = await practitionerClient.DeleteAsync(contentLocation);
            Assert.Equal(HttpStatusCode.Accepted, cancelResponse.StatusCode);
        }

        [SkippableFact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
        public async Task GivenUserSmartScope_WhenExportingPatientOutsidePractitionerCompartment_ThenServerShouldReturnForbidden()
        {
            Skip.If(!_fixture.IsUsingInProcTestServer, "Requires in-proc development identity provider to issue deterministic SMART practitioner identities.");
            await UpsertSmartExportFixturesAsync();

            using HttpClient practitionerClient = await CreateSmartHttpClientAsync(TestApplications.SmartPractitionerB, "user/Patient.read");
            using HttpRequestMessage exportRequest = GenerateExportRequest(
                $"Patient/{SmartPatientAId}/$export",
                queryParams: new Dictionary<string, string> { { "_type", "Patient" } });
            using HttpResponseMessage exportResponse = await practitionerClient.SendAsync(exportRequest);

            Assert.Equal(HttpStatusCode.Forbidden, exportResponse.StatusCode);
        }

        [SkippableFact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
        public async Task GivenUserSmartScope_WhenExportingMultiplePatientIds_ThenServerShouldReturnForbidden()
        {
            Skip.If(!_fixture.IsUsingInProcTestServer, "Requires in-proc development identity provider to issue deterministic SMART practitioner identities.");
            await UpsertSmartExportFixturesAsync();

            using HttpClient practitionerClient = await CreateSmartHttpClientAsync(TestApplications.SmartPractitionerA, "user/Patient.read");
            using HttpRequestMessage exportRequest = GenerateExportRequest(
                $"Patient/{SmartPatientAId},{SmartPatientBId}/$export",
                queryParams: new Dictionary<string, string> { { "_type", "Patient" } });
            using HttpResponseMessage exportResponse = await practitionerClient.SendAsync(exportRequest);

            Assert.Equal(HttpStatusCode.Forbidden, exportResponse.StatusCode);
        }

        [Fact]
        public async Task GivenExportJobDoesNotExist_WhenRequestingExportStatus_ThenServerShouldReturnNotFound()
        {
            string getPath = OperationsConstants.Operations + "/" + OperationsConstants.Export + "/" + Guid.NewGuid();
            using HttpRequestMessage getStatusRequest = new HttpRequestMessage()
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(_client.BaseAddress, getPath),
            };

            using HttpResponseMessage getStatusResponse = await _client.SendAsync(getStatusRequest);
            Assert.Equal(HttpStatusCode.NotFound, getStatusResponse.StatusCode);
        }

        [Theory]
        [InlineData("application/json")]
        [InlineData("applicaiton/xml")]
        [InlineData("*/*")]
        [InlineData("")]
        public async Task GivenExportIsEnabled_WhenRequestingExportWithInvalidAcceptHeader_ThenServerShouldReturnBadRequest(string acceptHeaderValue)
        {
            using HttpRequestMessage request = GenerateExportRequest(acceptHeader: acceptHeaderValue);

            using HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData("respond-async, wait=10")]
        [InlineData("respond-status")]
        [InlineData("*")]
        [InlineData("")]
        public async Task GivenExportIsEnabled_WhenRequestingExportWithInvalidPreferHeader_ThenServerShouldReturnBadRequest(string preferHeaderValue)
        {
            using HttpRequestMessage request = GenerateExportRequest(preferHeader: preferHeaderValue);

            using HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData("../secret")]
        [InlineData("..\\secret")]
        [InlineData("..%2fsecret")]
        public async Task GivenExportIsEnabled_WhenContainerContainsPathTraversal_ThenServerShouldReturnBadRequest(string container)
        {
            var queryParam = new Dictionary<string, string>
            {
                { KnownQueryParameterNames.Container, container },
            };

            using HttpRequestMessage request = GenerateExportRequest("$export", queryParams: queryParam);

            using HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData("../secret/config.json")]
        [InlineData("..\\secret\\config.json")]
        [InlineData("..%2fsecret%2fconfig.json")]
        public async Task GivenExportIsEnabled_WhenAnonymizationConfigContainsPathTraversal_ThenServerShouldReturnBadRequest(string anonymizationConfig)
        {
            var queryParam = new Dictionary<string, string>
            {
                { KnownQueryParameterNames.Container, "test-container" },
                { KnownQueryParameterNames.AnonymizationConfigurationLocation, anonymizationConfig },
            };

            using HttpRequestMessage request = GenerateExportRequest("$export", queryParams: queryParam);

            using HttpResponseMessage response = await _client.SendAsync(request);

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

            if (queryParams != null)
            {
                path = QueryHelpers.AddQueryString(path, queryParams);
            }

            request.RequestUri = new Uri(_client.BaseAddress, path);

            return request;
        }

        private HttpClient CreateUnauthenticatedHttpClient()
        {
            return new HttpClient(_fixture.TestFhirServer.CreateMessageHandler())
            {
                BaseAddress = _fixture.TestFhirServer.BaseAddress,
            };
        }

        private async Task<HttpClient> CreateSmartHttpClientAsync(TestApplication application, string scope)
        {
            string accessToken = await GetSmartAccessTokenAsync(application, scope);
            HttpClient smartClient = CreateUnauthenticatedHttpClient();
            smartClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return smartClient;
        }

        private async Task<string> GetSmartAccessTokenAsync(TestApplication application, string scope)
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", application.GrantType },
                { "client_id", application.ClientId },
                { "client_secret", application.ClientSecret },
                { "scope", scope },
                { "resource", AuthenticationSettings.Resource },
            });

            using HttpClient authClient = CreateUnauthenticatedHttpClient();
            using HttpResponseMessage response = await authClient.PostAsync(_fixture.TestFhirServer.TokenUri, content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseJson);
            return tokenResponse["access_token"].GetString();
        }

        private async Task UpsertSmartExportFixturesAsync()
        {
            await UpsertFhirResourceAsync("Practitioner", "smart-practitioner-A", """{"resourceType":"Practitioner","id":"smart-practitioner-A"}""");
            await UpsertFhirResourceAsync("Practitioner", "smart-practitioner-B", """{"resourceType":"Practitioner","id":"smart-practitioner-B"}""");
            await UpsertFhirResourceAsync(
                "Patient",
                SmartPatientAId,
                """{"resourceType":"Patient","id":"smart-patient-A","generalPractitioner":[{"reference":"Practitioner/smart-practitioner-A"}]}""");
            await UpsertFhirResourceAsync(
                "Patient",
                SmartPatientBId,
                """{"resourceType":"Patient","id":"smart-patient-B","generalPractitioner":[{"reference":"Practitioner/smart-practitioner-B"}]}""");
        }

        private async Task UpsertFhirResourceAsync(string resourceType, string id, string json)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"{resourceType}/{id}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/fhir+json"),
            };
            using HttpResponseMessage response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        private static void AssertStatusIsAcceptedOrOk(HttpStatusCode statusCode)
        {
            Assert.True(
                statusCode == HttpStatusCode.Accepted || statusCode == HttpStatusCode.OK,
                $"Expected Accepted or OK for export status but got {statusCode}.");
        }

        // Currently our tests do not validate the data that is being exported.
        // So once the tests are done we would like to cancel the export request
        // to try to prevent the worker from actually processing the export.
        private async Task GenerateAndSendCancelExportMessage(Uri contentLocation)
        {
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Delete,
            };

            request.RequestUri = contentLocation;

            await _client.SendAsync(request);
        }

        /// <summary>
        /// Verifies the full user-facing export cancellation lifecycle per the FHIR Bulk Data IG
        /// (https://hl7.org/fhir/uv/bulkdata/STU2/export.html#bulk-data-delete-request):
        ///   1. Create an export job → 202 Accepted with Content-Location.
        ///   2. Cancel the job (DELETE Content-Location) → 202 Accepted.
        ///   3. Get status (GET Content-Location) → 404 Not Found.
        ///   4. Cancel again (DELETE Content-Location) → 404 Not Found.
        /// </summary>
        [Fact]
        public async Task GivenExportJobExists_WhenCancelledThenStatusCheckedThenCancelledAgain_ThenCancelReturns202AndSubsequentCallsReturn404()
        {
            // Step 1 — Create an export job
            using HttpRequestMessage exportRequest = GenerateExportRequest();
            using HttpResponseMessage exportResponse = await _client.SendAsync(exportRequest);

            Assert.Equal(HttpStatusCode.Accepted, exportResponse.StatusCode);

            Uri contentLocation = exportResponse.Content.Headers.ContentLocation;
            Assert.NotNull(contentLocation);

            // Step 2 — Cancel the job (DELETE) → 202 Accepted
            using HttpRequestMessage cancelRequest = new HttpRequestMessage(HttpMethod.Delete, contentLocation);
            using HttpResponseMessage cancelResponse = await _client.SendAsync(cancelRequest);

            Assert.Equal(HttpStatusCode.Accepted, cancelResponse.StatusCode);

            // Step 3 — Get status (GET) → 404 Not Found
            using HttpRequestMessage getStatusRequest = new HttpRequestMessage(HttpMethod.Get, contentLocation);
            using HttpResponseMessage getStatusResponse = await _client.SendAsync(getStatusRequest);

            Assert.Equal(HttpStatusCode.NotFound, getStatusResponse.StatusCode);

            // Step 4 — Cancel again (DELETE) → 404 Not Found
            using HttpRequestMessage cancelAgainRequest = new HttpRequestMessage(HttpMethod.Delete, contentLocation);
            using HttpResponseMessage cancelAgainResponse = await _client.SendAsync(cancelAgainRequest);

            Assert.Equal(HttpStatusCode.NotFound, cancelAgainResponse.StatusCode);
        }
    }
}
