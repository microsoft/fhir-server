// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class SmartPatientScopeTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly HttpIntegrationTestFixture _fixture;

        public SmartPatientScopeTests(HttpIntegrationTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Theory]
        [InlineData("patient/*.read", "Patient")]
        [InlineData("patient/*.rs", "Observation")]
        [InlineData("patient/*.rs", "MedicationRequest")]
        [InlineData("patient/*.read", "Patient/other-patient")]
        [InlineData("patient/*.rs", "Observation/other-observation")]
        public async Task GivenPractitionerIdentityWithoutPatientContext_WhenUsingPatientScope_ThenRequestIsRejected(string scope, string path)
        {
            Assert.SkipWhen(!_fixture.IsUsingInProcTestServer, "Requires in-proc development identity provider to issue SMART scopes.");
            using HttpClient client = await CreateSmartClientAsync(TestApplications.SmartPractitionerA, scope);

            using HttpResponseMessage response = await client.GetAsync(path);
            var outcome = new FhirJsonParser().Parse<OperationOutcome>(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var issue = Assert.Single(outcome.Issue);
            Assert.Equal(OperationOutcome.IssueType.Invalid, issue.Code);
            Assert.Contains("Patient-level SMART scopes require", issue.Diagnostics);
        }

        [Theory]
        [InlineData("patient/Patient.read", true)]
        [InlineData("patient/Patient.rs", true)]
        [InlineData("user/Patient.read", false)]
        public async Task GivenExistingSmartIdentity_WhenSearching_ThenCompatibleScopesRemainSupported(string scope, bool patientIdentity)
        {
            Assert.SkipWhen(!_fixture.IsUsingInProcTestServer, "Requires in-proc development identity provider to issue SMART scopes.");
            using HttpClient client = await CreateSmartClientAsync(
                patientIdentity ? TestApplications.SmartPatientA : TestApplications.SmartPractitionerA, scope);

            using HttpResponseMessage response = await client.GetAsync("Patient");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private async Task<HttpClient> CreateSmartClientAsync(TestApplication application, string scope)
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", application.GrantType },
                { "client_id", application.ClientId },
                { "client_secret", application.ClientSecret },
                { "scope", scope },
                { "resource", AuthenticationSettings.Resource },
            });
            using var authClient = new HttpClient(_fixture.TestFhirServer.CreateMessageHandler());
            using HttpResponseMessage tokenResponse = await authClient.PostAsync(_fixture.TestFhirServer.TokenUri, content);
            tokenResponse.EnsureSuccessStatusCode();
            using JsonDocument token = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());

            var client = new HttpClient(_fixture.TestFhirServer.CreateMessageHandler())
            {
                BaseAddress = _fixture.TestFhirServer.BaseAddress,
            };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.RootElement.GetProperty("access_token").GetString());
            return client;
        }
    }
}
