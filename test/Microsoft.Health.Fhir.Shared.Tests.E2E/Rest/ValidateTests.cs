// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.Category, Categories.Validate)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class ValidateTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private const string Success = "All OK";
        private readonly TestFhirClient _client;

        public ValidateTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Theory]
        [InlineData("Patient/$validate", "{\"resourceType\":\"Patient\",\"name\":{\"family\":\"test\",\"given\":\"one\"}}")]
        [InlineData("Observation/$validate", "{\"resourceType\":\"Observation\",\"status\":\"registered\",\"code\":{\"coding\":[{\"system\":\"system\",\"code\":\"code\"}]}}")]
        public async void GivenAValidateRequest_WhenTheResourceIsValid_ThenAnOkMessageIsReturned(string path, string payload)
        {
            OperationOutcome outcome = await _client.ValidateAsync(path, payload);

            Assert.Single(outcome.Issue);
            CheckOperationOutcomeIssue(
                outcome.Issue[0],
                OperationOutcome.IssueSeverity.Information,
                OperationOutcome.IssueType.Informational,
                Success);
        }

        [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Xml)]
        [Theory]
        [InlineData("Observation/$validate", "<Observation xmlns=\"http://hl7.org/fhir\"><status value=\"final\"/><code><coding><system value=\"system\"/><code value=\"code\"/></coding></code></Observation>")]
        public async void GivenAValidateRequestInXML_WhenTheResourceIsValid_ThenAnOkMessageIsReturned(string path, string payload)
        {
            OperationOutcome outcome = await _client.ValidateAsync(path, payload, true);

            Assert.Single(outcome.Issue);
            CheckOperationOutcomeIssue(
                outcome.Issue[0],
                OperationOutcome.IssueSeverity.Information,
                OperationOutcome.IssueType.Informational,
                Success);
        }

        [Theory]
        [InlineData(
            "Patient/$validate",
            "{\"resourceType\":\"Patient\",\"name\":\"test, one\"}")]
        public async void GivenAValidateRequest_WhenTheResourceIsInvalid_ThenADetailedErrorIsReturned(string path, string payload)
        {
            OperationOutcome outcome = await _client.ValidateAsync(path, payload);

            Assert.Single(outcome.Issue);
            CheckOperationOutcomeIssue(
                    outcome.Issue[0],
                    OperationOutcome.IssueSeverity.Error,
                    OperationOutcome.IssueType.Invalid,
                    Api.Resources.ParsingError);
        }

        [Theory]
        [InlineData(
            "Observation/$validate",
            "{\"resourceType\":\"Observation\",\"code\":{\"coding\":[{\"system\":\"system\",\"code\":\"code\"}]}}",
            "Element with min. cardinality 1 cannot be null",
            "Observation.StatusElement")]
        [InlineData(
            "Observation/$validate",
            "{\"resourceType\":\"Patient\",\"name\":{\"family\":\"test\",\"given\":\"one\"}}",
            "Resource type in the URL must match resourceType in the resource.",
            "TypeName")]
        public async void GivenAValidateRequest_WhenTheResourceIsInvalid_ThenADetailedErrorWithLocationsIsReturned(string path, string payload, string expectedIssue, string location)
        {
            OperationOutcome outcome = await _client.ValidateAsync(path, payload);

            Assert.Single(outcome.Issue);
            CheckOperationOutcomeIssue(
                    outcome.Issue[0],
                    OperationOutcome.IssueSeverity.Error,
                    OperationOutcome.IssueType.Invalid,
                    expectedIssue,
                    location);
        }

        [Fact]
        public async void GivenAValidateByIdRequest_WhenTheResourceIsValid_ThenAnOkMessageIsReturned()
        {
            var payload = "{\"resourceType\": \"Patient\", \"id\": \"123\"}";

            OperationOutcome outcome = await _client.ValidateAsync("Patient/123/$validate", payload);

            Assert.Single(outcome.Issue);
            CheckOperationOutcomeIssue(
                outcome.Issue[0],
                OperationOutcome.IssueSeverity.Information,
                OperationOutcome.IssueType.Informational,
                Success);
        }

        [Fact]
        public async void GivenAValidateByIdRequest_WhenTheResourceIdDoesNotMatch_ThenADetailedErrorIsReturned()
        {
            var payload = "{\"resourceType\": \"Patient\", \"id\": \"456\"}";

            OperationOutcome outcome = await _client.ValidateAsync("Patient/123/$validate", payload);

            Assert.Single(outcome.Issue);
            CheckOperationOutcomeIssue(
                outcome.Issue[0],
                OperationOutcome.IssueSeverity.Error,
                OperationOutcome.IssueType.Invalid,
                "Id in the URL must match id in the resource.",
                "Patient.id");
        }

        [Fact]
        public async void GivenAValidateByIdRequest_WhenTheResourceIdIsMissing_ThenADetailedErrorIsReturned()
        {
            var payload = "{\"resourceType\": \"Patient\"}";

            OperationOutcome outcome = await _client.ValidateAsync("Patient/123/$validate", payload);

            Assert.Single(outcome.Issue);
            CheckOperationOutcomeIssue(
                outcome.Issue[0],
                OperationOutcome.IssueSeverity.Error,
                OperationOutcome.IssueType.Invalid,
                "Id must be specified in the resource.",
                "Patient.id");
        }

        [Fact]
        public async void GivenAValidateRequest_WhenAValidResourceIsPassedByParameter_ThenAnOkMessageIsReturned()
        {
            var payload = "{\"resourceType\": \"Parameters\", \"parameter\": [{\"name\": \"resource\", \"resource\": {\"resourceType\": \"Patient\", \"id\": \"123\"}}]}";

            OperationOutcome outcome = await _client.ValidateAsync("Patient/$validate", payload);

            Assert.Single(outcome.Issue);
            CheckOperationOutcomeIssue(
                outcome.Issue[0],
                OperationOutcome.IssueSeverity.Information,
                OperationOutcome.IssueType.Informational,
                Success);
        }

        [Fact]
        public async void GivenAValidateRequest_WhenAnInvalidResourceIsPassedByParameter_ThenADetailedErrorIsReturned()
        {
            var payload = "{\"resourceType\": \"Parameters\", \"parameter\": [{\"name\": \"resource\", \"resource\": {\"resourceType\":\"Patient\",\"name\":{\"family\":\"test\",\"given\":\"one\"}}}]}";

            OperationOutcome outcome = await _client.ValidateAsync("Observation/$validate", payload);

            Assert.Single(outcome.Issue);
            CheckOperationOutcomeIssue(
                outcome.Issue[0],
                OperationOutcome.IssueSeverity.Error,
                OperationOutcome.IssueType.Invalid,
                "Resource type in the URL must match resourceType in the resource.",
                "TypeName");
        }

        private void CheckOperationOutcomeIssue(
            OperationOutcome.IssueComponent issue,
            OperationOutcome.IssueSeverity? expectedSeverity,
            OperationOutcome.IssueType? expectedCode,
            string expectedMessage,
            string expectedLocation = null)
        {
            // Check expected outcome
            Assert.Equal(expectedSeverity, issue.Severity);
            Assert.Equal(expectedCode, issue.Code);
            Assert.Equal(expectedMessage, issue.Diagnostics);

            if (expectedLocation != null)
            {
                Assert.Single(issue.LocationElement);
                Assert.Equal(expectedLocation, issue.LocationElement[0].ToString());
            }
        }

        private string ExtractFromJson(string json, string property, bool isArray = false)
        {
            var propertyWithQuotes = property + "\":" + (isArray ? "[" : string.Empty) + "\"";
            var start = json.IndexOf(propertyWithQuotes) + propertyWithQuotes.Length;
            var end = json.IndexOf("\"", start);

            return json.Substring(start, end - start);
        }
    }
}
