// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Models;
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

        [Theory]
        [InlineData(false, false, false, false)]
        [InlineData(false, true, false, false)]
        [InlineData(true, false, false, false)]
        [InlineData(true, true, false, false)]
        [InlineData(false, false, true, false)]
        [InlineData(false, true, true, false)]
        [InlineData(true, false, true, false)]
        [InlineData(true, true, true, false)]
        [InlineData(false, false, false, true)]
        [InlineData(false, true, false, true)]
        [InlineData(true, false, false, true)]
        [InlineData(true, true, false, true)]
        [InlineData(false, false, true, true)]
        [InlineData(false, true, true, true)]
        [InlineData(true, false, true, true)]
        [InlineData(true, true, true, true)]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
        public async Task GivenReferenceFilteredSystemScopes_WhenIncludingResources_ThenEachCandidateMustSatisfyItsOwnScope(
            bool typedObservationScope,
            bool typedPatientScope,
            bool reversed,
            bool wildcard)
        {
            // Arrange
            Assert.SkipWhen(!_fixture.IsUsingInProcTestServer, "Requires in-proc development identity provider to issue SMART scopes.");
            Assert.SkipWhen(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "This test is only valid for R4 and R4B.");
            var adminClient = _fixture.TestFhirClient.CreateClientForClientApplication(TestApplications.AdminUser);
            var resources = new List<Resource>();
            async Task<T> CreateResourceAsync<T>(T resource)
                where T : Resource
            {
                using var response = await adminClient.CreateAsync(resource);
                resources.Add(response.Resource);
                return response.Resource;
            }

            try
            {
                Practitioner practitionerA = await CreateResourceAsync(new Practitioner());
                Practitioner practitionerB = await CreateResourceAsync(new Practitioner());
                Patient patientA = await CreateResourceAsync(new Patient
                {
                    GeneralPractitioner = new List<ResourceReference> { new ResourceReference($"Practitioner/{practitionerA.Id}") },
                });
                Patient patientB = await CreateResourceAsync(new Patient
                {
                    GeneralPractitioner = new List<ResourceReference> { new ResourceReference($"Practitioner/{practitionerB.Id}") },
                    Name = new List<HumanName> { new HumanName { Family = "SYNTHETIC_RESTRICTED_PATIENT" } },
                });
                Observation allowedObservation = await CreateResourceAsync(new Observation
                {
                    Status = ObservationStatus.Final,
                    Code = new CodeableConcept("http://loinc.org", "1234-5"),
                    Subject = new ResourceReference($"Patient/{patientA.Id}"),
                    Performer = new List<ResourceReference> { new ResourceReference($"Practitioner/{practitionerA.Id}") },
                });
                Observation outsidePatientObservation = await CreateResourceAsync(new Observation
                {
                    Status = ObservationStatus.Final,
                    Code = new CodeableConcept("http://loinc.org", "1234-5"),
                    Subject = new ResourceReference($"Patient/{patientB.Id}"),
                    Performer = new List<ResourceReference> { new ResourceReference($"Practitioner/{practitionerA.Id}") },
                });
                Observation restrictedObservation = await CreateResourceAsync(new Observation
                {
                    Status = ObservationStatus.Final,
                    Code = new CodeableConcept("http://loinc.org", "1234-5"),
                    Subject = new ResourceReference($"Patient/{patientA.Id}"),
                    Performer = new List<ResourceReference> { new ResourceReference($"Practitioner/{practitionerB.Id}") },
                });
                string observationScopeValue = typedObservationScope ? $"Practitioner/{practitionerA.Id}" : practitionerA.Id;
                string patientScopeValue = typedPatientScope ? $"Practitioner/{practitionerA.Id}" : practitionerA.Id;
                using HttpClient client = await CreateSmartClientAsync(
                    TestApplications.SmartUserClient,
                    $"system/Observation.rs?performer={observationScopeValue} system/Patient.rs?general-practitioner={patientScopeValue}");
                string resourceType = reversed ? "Patient" : "Observation";
                string parameter = reversed ? "_revinclude" : "_include";
                string relationship = wildcard ? "*:*" : "Observation:subject";

                // Act
                using HttpResponseMessage response = await client.GetAsync($"{resourceType}?{parameter}={relationship}&_count=100");

                // Assert
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var parser = new FhirJsonParser();
                var bundle = parser.Parse<Bundle>(await response.Content.ReadAsStringAsync());
                Assert.Null(bundle.NextLink);
                Assert.DoesNotContain(bundle.Link, link => link.Relation == "related");
                Assert.DoesNotContain(bundle.Entry, entry => entry.Resource is Patient patient && patient.Id == patientB.Id);
                Assert.DoesNotContain(bundle.Entry, entry => entry.Resource is Observation observation && observation.Id == restrictedObservation.Id);
                Assert.Equal(reversed ? 2 : 3, bundle.Entry.Count);
                Assert.Contains(bundle.Entry, entry =>
                    entry.Resource is Patient patient && patient.Id == patientA.Id &&
                    entry.Search.Mode == (reversed ? Bundle.SearchEntryMode.Match : Bundle.SearchEntryMode.Include));
                Assert.Contains(bundle.Entry, entry =>
                    entry.Resource is Observation observation && observation.Id == allowedObservation.Id &&
                    entry.Search.Mode == (reversed ? Bundle.SearchEntryMode.Include : Bundle.SearchEntryMode.Match));
                if (!reversed)
                {
                    Assert.Contains(bundle.Entry, entry =>
                        entry.Resource is Observation observation && observation.Id == outsidePatientObservation.Id &&
                        entry.Search.Mode == Bundle.SearchEntryMode.Match);
                }

                using HttpResponseMessage patientRead = await client.GetAsync($"Patient/{patientB.Id}");
                Assert.Equal(HttpStatusCode.NotFound, patientRead.StatusCode);
                using HttpResponseMessage patientSearch = await client.GetAsync($"Patient?_id={patientB.Id}");
                Assert.Equal(HttpStatusCode.OK, patientSearch.StatusCode);
                Assert.Empty(parser.Parse<Bundle>(await patientSearch.Content.ReadAsStringAsync()).Entry);
                using HttpResponseMessage observationRead = await client.GetAsync($"Observation/{outsidePatientObservation.Id}");
                Assert.Equal(HttpStatusCode.OK, observationRead.StatusCode);
                Assert.Equal(outsidePatientObservation.Id, parser.Parse<Observation>(await observationRead.Content.ReadAsStringAsync()).Id);
            }
            finally
            {
                foreach (Resource resource in resources.AsEnumerable().Reverse())
                {
                    await adminClient.HardDeleteAsync(resource);
                }
            }
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
