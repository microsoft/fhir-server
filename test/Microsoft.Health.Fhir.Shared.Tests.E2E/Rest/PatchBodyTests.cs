// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Helpers;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Patch)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class PatchBodyTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;

        public PatchBodyTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenJsonPatch_WhenBodyIsEmpty_ThenBadRequestShouldBeReturned()
        {
            var request = new HttpRequestMessage(
                HttpMethod.Patch,
                "Patient/123");
            request.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json-patch+json");

            HttpResponseMessage response = await _client.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenFhirPatch_WhenBodyIsEmpty_ThenBadRequestShouldBeReturned()
        {
            var request = new HttpRequestMessage(
                HttpMethod.Patch,
                "Patient/123");
            request.Content = new StringContent(string.Empty, Encoding.UTF8, "application/fhir+json");

            HttpResponseMessage response = await _client.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenJsonPatch_WhenBodyIsPopulated_ThenResultIsSuccessful()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);
            Assert.Equal(AdministrativeGender.Male, response.Resource.Gender);

            string patchDocument =
                "[{\"op\":\"replace\",\"path\":\"/gender\",\"value\":\"female\"}]";

            using FhirResponse<Patient> patch = await _client.JsonPatchAsync(response.Resource, patchDocument);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
            Assert.Equal(AdministrativeGender.Female, patch.Resource.Gender);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenFhirPatch_WhenBodyIsPopulated_ThenResultIsSuccessful()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);
            Assert.Equal(AdministrativeGender.Male, response.Resource.Gender);

            var patchRequest = new Parameters().AddReplacePatchParameter("Patient.gender", new Code("female"));

            using FhirResponse<Patient> patch = await _client.FhirPatchAsync(response.Resource, patchRequest);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
            Assert.Equal(AdministrativeGender.Female, patch.Resource.Gender);
        }
    }
}
