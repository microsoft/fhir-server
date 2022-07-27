// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.Category, Categories.Validate)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class ValidateTests : IClassFixture<ValidateTestFixture>
    {
        private const string Success = "All OK";
        private readonly TestFhirClient _client;

        public ValidateTests(ValidateTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Theory]
        [InlineData("Patient/$validate", "Profile-Patient-uscore", "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient")]
        [InlineData("Organization/$validate", "Profile-Organization-uscore", "http://hl7.org/fhir/us/core/StructureDefinition/us-core-organization")]
        [InlineData("Organization/$validate", "Profile-Organization-uscore-endpoint", "http://hl7.org/fhir/us/core/StructureDefinition/us-core-organization")]
        [InlineData("CarePlan/$validate", "Profile-CarePlan-uscore", "http://hl7.org/fhir/us/core/StructureDefinition/us-core-careplan")]
        [InlineData("Patient/$validate", "Profile-Patient-uscore", null)]
        [InlineData("Organization/$validate", "Profile-Organization-uscore", null)]
        [InlineData("Organization/$validate", "Profile-Organization-uscore-endpoint", null)]
        [InlineData("CarePlan/$validate", "Profile-CarePlan-uscore", null)]
        public async void GivenAValidateRequest_WhenTheResourceIsValid_ThenAnOkMessageIsReturned(string path, string filename, string profile)
        {
            OperationOutcome outcome = await _client.ValidateAsync(path, Samples.GetJson(filename), profile);

            Assert.Empty(outcome.Issue.Where(x => (x.Severity == OperationOutcome.IssueSeverity.Error && x.Code != OperationOutcome.IssueType.CodeInvalid && x.Code != OperationOutcome.IssueType.Informational)));
            Parameters parameters = new Parameters();
            if (!string.IsNullOrEmpty(profile))
            {
                parameters.Parameter.Add(new Parameters.ParameterComponent() { Name = "profile", Value = new FhirString(profile) });
            }

            var parser = new FhirJsonParser();
            parameters.Parameter.Add(new Parameters.ParameterComponent() { Name = "resource", Resource = parser.Parse<Resource>(Samples.GetJson(filename)) });

            outcome = await _client.ValidateAsync(path, parameters.ToJson());
            Assert.Empty(outcome.Issue.Where(x => (x.Severity == OperationOutcome.IssueSeverity.Error && x.Code != OperationOutcome.IssueType.CodeInvalid && x.Code != OperationOutcome.IssueType.Informational)));
        }

        [Theory]
        [InlineData("Organization/$validate", "Organization", "http://hl7.org/fhir/us/core/StructureDefinition/us-core-organization")]
        [InlineData("Patient/$validate", "Patient", "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient")]
        [InlineData("CarePlan/$validate", "CarePlan", "http://hl7.org/fhir/us/core/StructureDefinition/us-core-careplan")]
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
            "Element with minimum cardinality 1 cannot be null. At Observation.StatusElement.",
            "Observation.StatusElement")]
        [InlineData(
            "Observation/$validate",
            "{\"resourceType\":\"Patient\",\"name\":{\"family\":\"test\",\"given\":\"one\"}}",
            "Resource type in the URL must match resourceType in the resource.",
            "TypeName")]
        public async void GivenAValidateRequest_WhenTheResourceIsInvalid_ThenADetailedErrorWithLocationsIsReturned(string path, string payload, string expectedIssue, string expression)
        {
            OperationOutcome outcome = await _client.ValidateAsync(path, payload);

            Assert.Single(outcome.Issue);
            CheckOperationOutcomeIssue(
                    outcome.Issue[0],
                    OperationOutcome.IssueSeverity.Error,
                    OperationOutcome.IssueType.Invalid,
                    expectedIssue,
                    expression);
        }

        [Fact]
        public async void GivenAValidateByIdRequest_WhenTheResourceIsValid_ThenAnOkMessageIsReturned()
        {
            var fhirSource = Samples.GetJson("Profile-Patient-uscore");
            var parser = new FhirJsonParser();
            var patient = parser.Parse<Resource>(fhirSource).ToTypedElement().ToResourceElement();
            Patient createdResource = await _client.CreateAsync(patient.ToPoco<Patient>());
            OperationOutcome outcome = await _client.ValidateByIdAsync(ResourceType.Patient, createdResource.Id, "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient");
#if !R5
            Assert.Empty(outcome.Issue.Where(x => x.Severity == OperationOutcome.IssueSeverity.Error));
#endif
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
        public async void GivenAValidateResourceToUSCoreProfile_WhenTheResourceProfileIsValid_ThenAnOkMessageIsReturned()
        {
            var fhirSource = Samples.GetJson("Profile-Patient-PassUsCore-Example");

            OperationOutcome outcome = await _client.ValidateAsync("Patient/$validate", fhirSource, "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient");
#if !R5
            Assert.Empty(outcome.Issue.Where(x => (x.Code != OperationOutcome.IssueType.Informational)));
#endif
        }

        [Fact]
        public async void GivenAValidateResourceToUSCoreProfile_WhenTheResourceProfileIsNotValid_ThenAnErrorShouldBeReturned()
        {
            var fhirSource = Samples.GetJson("Profile-Patient-FailUsCore-Example");
            OperationOutcome outcome = await _client.ValidateAsync("Patient/$validate", fhirSource, "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient");
            bool hasSeenCodeInvalid = false;
            foreach (OperationOutcome.IssueComponent issue in outcome.Issue)
            {
                Assert.True(issue.Code == OperationOutcome.IssueType.Informational || issue.Code == OperationOutcome.IssueType.CodeInvalid);
                if (issue.Code == OperationOutcome.IssueType.CodeInvalid)
                {
                    hasSeenCodeInvalid = true;
                }
            }

            Assert.True(hasSeenCodeInvalid, "Resource has invalid code");
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

        [Fact]
        public async void GivenInvalidProfile_WhenValidateCalled_ThenBadRequestReturned()
        {
            var patient = Samples.GetJson("Patient");
            var profile = "abc";

            var exception = await Assert.ThrowsAsync<FhirException>(async () => await _client.ValidateAsync("Patient/$validate", patient, profile));
            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);

            Parameters parameters = new Parameters();
            parameters.Parameter.Add(new Parameters.ParameterComponent() { Name = "profile", Value = new FhirString(profile) });

            var parser = new FhirJsonParser();
            parameters.Parameter.Add(new Parameters.ParameterComponent() { Name = "resource", Resource = Samples.GetDefaultPatient().ToPoco<Patient>() });

            exception = await Assert.ThrowsAsync<FhirException>(async () => await _client.ValidateAsync("Patient/$validate", parameters.ToJson()));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
        }

        [Fact]
        public async void GivenPostedProfiles_WhenCallingForMetadata_ThenMetadataHasSupportedProfiles()
        {
            using FhirResponse<CapabilityStatement> response = await _client.ReadAsync<CapabilityStatement>("metadata");
#if !Stu3
            var supportedProfiles = response.Resource.Rest.Where(r => r.Mode.ToString().Equals("server", StringComparison.OrdinalIgnoreCase)).
                SelectMany(x => x.Resource.Where(x => x.SupportedProfile.Any()).Select(x => x.SupportedProfile)).
                SelectMany(x => x).OrderBy(x => x).ToList();
#else
            var supportedProfiles = response.Resource.Profile.Select(x => x.Url.ToString()).OrderBy(x => x).ToList();
#endif
            var expectedSupportProfiles = new[]
                {
                    "http://hl7.org/fhir/us/core/StructureDefinition/us-core-careplan",
                    "http://hl7.org/fhir/us/core/StructureDefinition/us-core-organization",
                    "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient",
                };

            foreach (var profile in expectedSupportProfiles)
            {
                Assert.Contains(profile, supportedProfiles);
            }
        }

        private void CheckOperationOutcomeIssue(
            OperationOutcome.IssueComponent issue,
            OperationOutcome.IssueSeverity? expectedSeverity,
            OperationOutcome.IssueType? expectedCode,
            string expectedMessage,
            string expectedExpression = null)
        {
            // Check expected outcome
            Assert.Equal(expectedSeverity, issue.Severity);
            Assert.Equal(expectedCode, issue.Code);
            Assert.Equal(expectedMessage, issue.Diagnostics);

            if (expectedExpression != null)
            {
                Assert.Single(issue.ExpressionElement);
                Assert.Equal(expectedExpression, issue.ExpressionElement[0].ToString());
            }
        }
    }
}
