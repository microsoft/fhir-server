// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.Category, Categories.Validate)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class ValidateTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly HttpClient _client;
        private readonly JsonSerializer _serializer;

        public ValidateTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.HttpClient;
            _serializer = JsonSerializer.Create();
        }

        [Theory]
        [InlineData("Patient/$validate", "{\"resourceType\":\"Patient\",\"name\":{\"family\":\"test\",\"given\":\"one\"}}")]
        [InlineData("Observation/$validate", "{\"resourceType\":\"Observation\",\"status\":\"registered\",\"code\":{\"coding\":[{\"system\":\"system\",\"code\":\"code\"}]}}")]
        public async void GivenAValidateRequest_WhenTheResourceIsValid_ThenAnOkMessageIsReturned(string path, string payload)
        {
            HttpResponseMessage response = await _client.SendAsync(GenerateValidateMessage(path, payload));

            var contentString = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            CheckOperationOutcomeIssue(
                contentString,
                OperationOutcome.IssueSeverity.Information,
                OperationOutcome.IssueType.Informational,
                "All OK");
        }

        [Theory]
        [InlineData(
            "Patient/$validate",
            "{\"resourceType\":\"Patient\",\"name\":\"test, one\"}",
            "Type checking the data: Since type HumanName is not a primitive, it cannot have a value (at Resource.name[0])")]
        public async void GivenAValidateRequest_WhenTheResourceIsInvalid_ThenADetailedErrorIsReturned(string path, string payload, string expectedIssue)
        {
            HttpResponseMessage response = await _client.SendAsync(GenerateValidateMessage(path, payload));

            var contentString = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            CheckOperationOutcomeIssue(
                    contentString,
                    OperationOutcome.IssueSeverity.Error,
                    OperationOutcome.IssueType.Invalid,
                    expectedIssue);
        }

        [Theory]
        [InlineData(
            "Observation/$validate",
            "{\"resourceType\":\"Observation\",\"code\":{\"coding\":[{\"system\":\"system\",\"code\":\"code\"}]}}",
            "Element with min. cardinality 1 cannot be null",
            "Observation.StatusElement")]
        public async void GivenAValidateRequest_WhenTheResourceIsInvalid_ThenADetailedErrorWithLocationsIsReturned(string path, string payload, string expectedIssue, string location)
        {
            HttpResponseMessage response = await _client.SendAsync(GenerateValidateMessage(path, payload));

            var contentString = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(location, ExtractFromJson(contentString, "location", true));
            CheckOperationOutcomeIssue(
                    contentString,
                    OperationOutcome.IssueSeverity.Error,
                    OperationOutcome.IssueType.Invalid,
                    expectedIssue);
        }

        private void CheckOperationOutcomeIssue(
            string issue,
            OperationOutcome.IssueSeverity? expectedSeverity,
            OperationOutcome.IssueType? expectedCode,
            string expectedMessage)
        {
            var severity = ExtractFromJson(issue, "severity");
            var code = ExtractFromJson(issue, "code");
            var diagnostics = ExtractFromJson(issue, "diagnostics");

            // Check expected outcome
            Assert.Equal(expectedSeverity.ToString().ToLowerInvariant(), severity);
            Assert.Equal(expectedCode.ToString().ToLowerInvariant(), code);
            Assert.Equal(expectedMessage, diagnostics);
        }

        private string ExtractFromJson(string json, string property, bool isArray = false)
        {
            var propertyWithQuotes = property + "\":" + (isArray ? "[" : string.Empty) + "\"";
            var start = json.IndexOf(propertyWithQuotes) + propertyWithQuotes.Length;
            var end = json.IndexOf("\"", start);

            return json.Substring(start, end - start);
        }

        private HttpRequestMessage GenerateValidateMessage(string path, string body)
        {
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
            };

            request.RequestUri = new Uri(_client.BaseAddress, path);
            request.Content = new StringContent(body, Encoding.UTF8, ContentType.JSON_CONTENT_HEADER);

            return request;
        }
    }
}
