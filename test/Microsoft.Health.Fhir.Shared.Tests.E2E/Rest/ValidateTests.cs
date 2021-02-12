// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Net;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Tests.E2E.Rest;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.Category, Categories.Validate)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class ValidateTests : IClassFixture<ValidateTextFixture>
    {
        private const string Success = "All OK";
        private readonly TestFhirClient _client;

        public ValidateTests(ValidateTextFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Theory]
        [InlineData("CarePlan/$validate", "Profile-CarePlan", "http://hl7.org/fhir/us/core/StructureDefinition/us-core-careplan")]
        [InlineData("Patient/$validate", "Profile-Patient", "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient")]
        [InlineData("CarePlan/$validate", "Profile-CarePlan", null)]
        [InlineData("Patient/$validate", "Profile-Patient", null)]
        public async void GivenAValidateRequest_WhenTheResourceIsValid_ThenAnOkMessageIsReturned(string path, string filename, string profile)
        {
            var location = Path.GetDirectoryName(GetType().Assembly.Location);
            var pathToZip = Path.Combine(location, "Profiles", $"{ModelInfoProvider.Version}", $"{ModelInfoProvider.Version}.zip");
            Assert.True(File.Exists(pathToZip), $"{pathToZip} doesn't exist");
            var profileReader = new ProfileReaderFromZip(pathToZip);
            Assert.NotEmpty(profileReader.ListSummaries());
            Assert.NotNull(profileReader.ResolveByCanonicalUri("http://hl7.org/fhir/us/core/StructureDefinition/us-core-careplan"));
            OperationOutcome outcome = await _client.ValidateAsync(path, Samples.GetJson(filename), profile);

            Assert.Empty(outcome.Issue.Where(x => x.Severity == OperationOutcome.IssueSeverity.Error));
            Parameters parameters = new Parameters();
            if (!string.IsNullOrEmpty(profile))
            {
                parameters.Parameter.Add(new Parameters.ParameterComponent() { Name = "profile", Value = new FhirString(profile) });
            }

            var parser = new FhirJsonParser();
            parameters.Parameter.Add(new Parameters.ParameterComponent() { Name = "resource", Resource = parser.Parse<Resource>(Samples.GetJson(filename)) });

            outcome = await _client.ValidateAsync(path, parameters.ToJson());

            Assert.Empty(outcome.Issue.Where(x => x.Severity == OperationOutcome.IssueSeverity.Error));
        }

        [Theory]
        [InlineData("Observation/$validate", "Observation-For-Patient-f001", "http://hl7.org/fhir/us/core/StructureDefinition/us-core-observation-lab")]
        [InlineData("Observation/$validate", "Observation-For-Patient-f001", "http://hl7.org/fhir/us/core/StructureDefinition/pediatric-bmi-for-age")]
        [InlineData("Observation/$validate", "Observation-For-Patient-f001", "http://hl7.org/fhir/us/core/StructureDefinition/us-core-smokingstatus")]
        [InlineData("Observation/$validate", "Observation-For-Patient-f001", "http://hl7.org/fhir/us/core/StructureDefinition/pediatric-weight-for-height")]
        [InlineData("Patient/$validate", "Patient", "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient")]
        public async void GivenAValidateRequest_WhenTheResourceIsNonConformToProfile_ThenAnErrorShouldBeReturned(string path, string filename, string profile)
        {
            OperationOutcome outcome = await _client.ValidateAsync(path, Samples.GetJson(filename), profile);

            Assert.NotEmpty(outcome.Issue.Where(x => x.Severity == OperationOutcome.IssueSeverity.Error));

            Parameters parameters = new Parameters();
            if (!string.IsNullOrEmpty(profile))
            {
                parameters.Parameter.Add(new Parameters.ParameterComponent() { Name = "profile", Value = new FhirString(profile) });
            }

            var parser = new FhirJsonParser();
            parameters.Parameter.Add(new Parameters.ParameterComponent() { Name = "resource", Resource = parser.Parse<Resource>(Samples.GetJson(filename)) });

            outcome = await _client.ValidateAsync(path, parameters.ToJson());

            Assert.NotEmpty(outcome.Issue.Where(x => x.Severity == OperationOutcome.IssueSeverity.Error));
        }

        [Theory]
        [InlineData(
            "Patient/$validate",
            "{\"resourceType\":\"Patient\",\"name\":\"test, one\"}")]
        public async void GivenAValidateRequest_WhenTheResourceIsInvalid_ThenADetailedErrorIsReturned(string path, string payload)
        {
            OperationOutcome outcome = await _client.ValidateAsync(path, payload);
            Exception exception = null;
            try
            {
                new FhirJsonParser().Parse<Resource>(payload);
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            Assert.NotNull(exception);
            Assert.Single(outcome.Issue);
            CheckOperationOutcomeIssue(
                    outcome.Issue[0],
                    OperationOutcome.IssueSeverity.Error,
                    OperationOutcome.IssueType.Invalid,
                    string.Format(Api.Resources.ParsingError, exception.Message));
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
            var fhirSource = Samples.GetJson("Profile-Patient");
            var parser = new FhirJsonParser();
            var patient = parser.Parse<Resource>(fhirSource).ToTypedElement().ToResourceElement();
            Patient createdResource = await _client.CreateAsync(patient.ToPoco<Patient>());
            OperationOutcome outcome = await _client.ValidateByIdAsync(ResourceType.Patient, createdResource.Id, "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient");

            Assert.Empty(outcome.Issue.Where(x => x.Severity == OperationOutcome.IssueSeverity.Error));
        }

        [Fact]
        public async void GivenUnpresentIdRequest_WhenValidateIt_ThenAnErrorShouldBeReturned()
        {
            var exception = await Assert.ThrowsAsync<FhirException>(async () =>
            await _client.ValidateByIdAsync(ResourceType.Patient, "-1", "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient"));

            Assert.Equal(HttpStatusCode.NotFound, exception.Response.StatusCode);
        }

        [Fact]
        public async void GivenAValidateByIdRequestWithStricterProfile_WhenRunningValidate_ThenAnErrorShouldBeReturned()
        {
            Patient createdResource = await _client.CreateAsync(Samples.GetDefaultPatient().ToPoco<Patient>());
            OperationOutcome outcome = await _client.ValidateByIdAsync(ResourceType.Patient, createdResource.Id, "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient");

            Assert.NotEmpty(outcome.Issue.Where(x => x.Severity == OperationOutcome.IssueSeverity.Error));
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
    }
}
