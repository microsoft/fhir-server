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
    [Trait(Traits.Category, Categories.Authorization)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class SmartPatientReferenceTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly HttpIntegrationTestFixture _fixture;

        public SmartPatientReferenceTests(HttpIntegrationTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Theory]
        [InlineData("", true)]
        [InlineData(null, true)]
        [InlineData("https://external.fhir.invalid/", false)]
        [InlineData("other-fhir/", false)]
        public async Task GivenPatientIdCollision_WhenSearchingAndReading_ThenOnlyLocalReferencesAuthorizeAccess(string referenceBase, bool isLocal)
        {
            Assert.SkipWhen(!_fixture.IsUsingInProcTestServer, "Requires the development identity provider to issue a patient-scoped token.");
            var adminClient = _fixture.TestFhirClient.CreateClientForClientApplication(TestApplications.AdminUser);
            string reference = referenceBase switch
            {
                null => new Uri(_fixture.TestFhirServer.BaseAddress, "Patient/smart-patient-A").AbsoluteUri,
                "other-fhir/" => new Uri(_fixture.TestFhirServer.BaseAddress, "other-fhir/Patient/smart-patient-A").AbsoluteUri,
                _ => $"{referenceBase}Patient/smart-patient-A",
            };
            Observation observation = await adminClient.CreateAsync(new Observation
            {
                Status = ObservationStatus.Final,
                Code = new CodeableConcept("http://loinc.org", "1234-5"),
                Subject = new ResourceReference(reference),
                Value = new FhirString("synthetic-compartment-collision"),
            });

            try
            {
                using var client = new HttpClient(_fixture.TestFhirServer.CreateMessageHandler())
                {
                    BaseAddress = _fixture.TestFhirServer.BaseAddress,
                };
                TestApplication application = TestApplications.SmartPatientA;
                using var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "grant_type", application.GrantType },
                    { "client_id", application.ClientId },
                    { "client_secret", application.ClientSecret },
                    { "scope", "patient/Observation.read" },
                });
                using HttpResponseMessage tokenResponse = await client.PostAsync(_fixture.TestFhirServer.TokenUri, tokenRequest);
                tokenResponse.EnsureSuccessStatusCode();
                using JsonDocument token = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.RootElement.GetProperty("access_token").GetString());

                foreach (string query in new[] { "?_count=1000", $"?_id={observation.Id}", "?subject=Patient/smart-patient-A" })
                {
                    bool found = false;
                    string nextPage = $"Observation{query}";
                    do
                    {
                        using HttpResponseMessage response = await client.GetAsync(nextPage);
                        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                        var bundle = new FhirJsonParser().Parse<Bundle>(await response.Content.ReadAsStringAsync());
                        found |= bundle.Entry.Any(entry => entry.Resource is Observation result && result.Id == observation.Id);
                        nextPage = bundle.NextLink?.OriginalString;
                    }
                    while (nextPage != null);

                    Assert.Equal(isLocal, found);
                }

                using HttpResponseMessage read = await client.GetAsync($"Observation/{observation.Id}");
                Assert.Equal(isLocal ? HttpStatusCode.OK : HttpStatusCode.NotFound, read.StatusCode);
                if (isLocal)
                {
                    using JsonDocument resource = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
                    Assert.Equal("synthetic-compartment-collision", resource.RootElement.GetProperty("valueString").GetString());
                }
            }
            finally
            {
                await adminClient.HardDeleteAsync(observation);
            }
        }
    }
}
