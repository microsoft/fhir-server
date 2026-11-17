// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.ForwardedHeaders
{
    /// <summary>
    /// Validates how X-Forwarded-* headers affect SMART reference-scope checks when trusted forwarders are configured.
    /// Currently covers callers outside the trusted networks, whose X-Forwarded-Prefix must be ignored so it cannot
    /// change how references are classified.
    /// The counterpart tests in <see cref="SmartPatientScopeTests"/> document the exploit when every caller is trusted.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class SmartScopeForwardedHeadersTests : IClassFixture<TrustedForwardersTestFixture>
    {
        private readonly TrustedForwardersTestFixture _fixture;

        public SmartScopeForwardedHeadersTests(TrustedForwardersTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task GivenReferenceScopedObservation_WhenReadingWithForwardedPrefixFromUntrustedCaller_ThenPrivateLocalObservationIsNotDisclosed()
        {
            Assert.SkipWhen(!_fixture.IsUsingInProcTestServer, "Requires in-proc development identity provider to issue SMART scopes.");
            var writer = _fixture.TestFhirClient.CreateClientForClientApplication(TestApplications.AdminUser);
            var resources = new List<Resource>();

            try
            {
                (Observation allowed, Observation localPrivate, Observation localOther, string absoluteSubject) = await SeedAsync(writer, resources);

                using HttpClient reader = await CreateSmartClientAsync(
                    TestApplications.SmartUserClient,
                    $"system/Observation.rs?subject={Uri.EscapeDataString(absoluteSubject)}");

                reader.DefaultRequestHeaders.Add("X-Forwarded-Prefix", "/injected");
                using HttpResponseMessage allowedRead = await reader.GetAsync($"Observation/{allowed.Id}");
                using HttpResponseMessage privateRead = await reader.GetAsync($"Observation/{localPrivate.Id}");
                using HttpResponseMessage otherRead = await reader.GetAsync($"Observation/{localOther.Id}");

                Assert.Equal(HttpStatusCode.OK, allowedRead.StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, privateRead.StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, otherRead.StatusCode);
                Assert.DoesNotContain("SYNTH_PRIVATE_FWREF_19", await privateRead.Content.ReadAsStringAsync());
            }
            finally
            {
                await CleanupAsync(writer, resources);
            }
        }

        [Fact]
        public async Task GivenReferenceScopedObservation_WhenSearchingWithForwardedPrefixFromUntrustedCaller_ThenPrivateObservationIsNotDisclosed()
        {
            Assert.SkipWhen(!_fixture.IsUsingInProcTestServer, "Requires in-proc development identity provider to issue SMART scopes.");
            var writer = _fixture.TestFhirClient.CreateClientForClientApplication(TestApplications.AdminUser);
            var resources = new List<Resource>();

            try
            {
                (_, _, _, string absoluteSubject) = await SeedAsync(writer, resources);

                using HttpClient reader = await CreateSmartClientAsync(
                    TestApplications.SmartUserClient,
                    $"system/Observation.rs?subject={Uri.EscapeDataString(absoluteSubject)}");

                reader.DefaultRequestHeaders.Add("X-Forwarded-Prefix", "/injected");
                using HttpResponseMessage search = await reader.GetAsync("Observation?_count=10");

                Assert.Equal(HttpStatusCode.OK, search.StatusCode);
                string body = await search.Content.ReadAsStringAsync();
                Assert.Contains("SYNTH_ALLOWED_FWREF_19", body);
                Assert.DoesNotContain("SYNTH_PRIVATE_FWREF_19", body);
                Assert.DoesNotContain("SYNTH_OTHER_FWPRE_19", body);
            }
            finally
            {
                await CleanupAsync(writer, resources);
            }
        }

        private async Task<(Observation Allowed, Observation LocalPrivate, Observation LocalOther, string AbsoluteSubject)> SeedAsync(TestFhirClient writer, List<Resource> resources)
        {
            string absoluteSubject = new Uri(_fixture.TestFhirServer.BaseAddress, "injected/Patient/fwpatient19").AbsoluteUri;

            Observation allowed = await CreateObservationAsync(writer, resources, absoluteSubject, "SYNTH_ALLOWED_FWREF_19");
            Observation localPrivate = await CreateObservationAsync(writer, resources, "Patient/fwpatient19", "SYNTH_PRIVATE_FWREF_19");
            Observation localOther = await CreateObservationAsync(writer, resources, "Patient/fwother19", "SYNTH_OTHER_FWPRE_19");

            return (allowed, localPrivate, localOther, absoluteSubject);
        }

        private static async Task<Observation> CreateObservationAsync(TestFhirClient writer, List<Resource> resources, string subject, string marker)
        {
            using var response = await writer.CreateAsync(new Observation
            {
                Status = ObservationStatus.Final,
                Code = new CodeableConcept("http://loinc.org", "1234-5"),
                Subject = new ResourceReference(subject),
                Value = new FhirString(marker),
            });
            resources.Add(response.Resource);
            return response.Resource;
        }

        private static async Task CleanupAsync(TestFhirClient writer, List<Resource> resources)
        {
            foreach (Resource resource in resources.AsEnumerable().Reverse())
            {
                await writer.HardDeleteAsync(resource);
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
