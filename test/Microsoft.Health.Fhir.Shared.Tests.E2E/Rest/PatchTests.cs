// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class PatchTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;

        public PatchTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingAPatch_ThenServerShouldPatchCorrectly()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);
            Assert.Equal(AdministrativeGender.Male, response.Resource.Gender);
            Assert.NotNull(response.Resource.Address);

            string patchDocument = "[{\"op\":\"replace\",\"path\":\"/gender\",\"value\":\"female\"}, {\"op\":\"remove\",\"path\":\"/address\"}]";

            using FhirResponse<Patient> patch = await _client.PatchAsync(response.Resource, patchDocument);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
            Assert.Equal(AdministrativeGender.Female, patch.Resource.Gender);
            Assert.Empty(patch.Resource.Address);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingAPatch_ThenServerShouldPatchNewPropertyCorrectly()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            poco.Address.Clear();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);
            Assert.Empty(response.Resource.Address);

            string patchDocument = "[{\"op\":\"add\",\"path\":\"/address\",\"value\":[]},{\"op\":\"add\",\"path\":\"/address/0\",\"value\":{\"use\":\"home\",\"line\":[\"23 thule st\",\"avon\"],\"city\":\"Big Smoke\",\"country\":\"erewhon\",\"text\":\"23 thule st\"}}]";
            using FhirResponse<Patient> patch = await _client.PatchAsync(response.Resource, patchDocument);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
            Assert.Single(patch.Resource.Address);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingAnInvalidPatch_ThenAnErrorShouldBeReturned()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            string patchDocument = "[{\"op\":\"add\",\"path\":\"/dummyProperty\",\"value\":\"dummy\"}]";

            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.PatchAsync(
              response.Resource,
              patchDocument));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
        }

        [Theory]
        [InlineData("/versionId", "100")]
        [InlineData("/resourceType", "DummyResource")]
        [InlineData("/text/div", "<div>dummy narrative</div>")]
        [InlineData("/text/status", "extensions")]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingAForbiddenPropertyPatch_ThenAnErrorShouldBeReturned(string propertyName, string value)
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            string patchDocument = "[{\"op\":\"replace\",\"path\":\"" + propertyName + "\",\"value\":\"" + value + "\"}]";

            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.PatchAsync(
              response.Resource,
              patchDocument));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingAReplacePatch_ThenServerShouldPatchCorrectly()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);
            Assert.Equal(AdministrativeGender.Male, response.Resource.Gender);
            Assert.NotNull(response.Resource.Address);

            string patchDocument =
                "[{\"op\":\"replace\",\"path\":\"/gender\",\"value\":\"female\"}]";

            using FhirResponse<Patient> patch = await _client.PatchAsync(response.Resource, patchDocument);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
            Assert.Equal(AdministrativeGender.Female, patch.Resource.Gender);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingAddBirthdatePatch_ThenServerShouldPatchCorrectly()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);
            string patchDocument =
                "[{\"op\":\"add\",\"path\":\"/birthDate\",\"value\":\"1970-12-25\"}]";

            using FhirResponse<Patient> patch = await _client.PatchAsync(response.Resource, patchDocument);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
            Assert.Equal("1970-12-25", patch.Resource.BirthDate);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task
            GivenAServerThatSupportsIt_WhenSubmittingReplaceBirthdatePatch_ThenServerShouldPatchCorrectly()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);
            Assert.NotNull(response.Resource.Address);

            string patchDocument =
                "[{\"op\":\"replace\",\"path\":\"/birthDate\",\"value\":\"1974-12-25\"}]";

            using FhirResponse<Patient> patch = await _client.PatchAsync(response.Resource, patchDocument);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
            Assert.Equal("1974-12-25", patch.Resource.BirthDate);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task
            GivenAServerThatSupportsIt_WhenSubmittingRemovingBirthdatePatch_ThenServerShouldPatchCorrectly()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);
            string patchDocument =
                "[{\"op\":\"remove\",\"path\":\"/birthDate\"}]";

            using FhirResponse<Patient> patch = await _client.PatchAsync(response.Resource, patchDocument);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
            Assert.Null(patch.Resource.BirthDate);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task
            GivenAServerThatSupportsIt_WhenSubmittingAAddNestedPropertyPatch_ThenServerShouldPatchNewPropertyCorrectly()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            poco.Address.Clear();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            string patchDocument =
                "[{\"op\": \"add\", \"path\": \"/communication\",\"value\": {\"language\": {\"coding\": [{\"system\": \"urn:ietf:bcp:47\",\"code\": \"nl-NL\",\"display\": \"Dutch\"}]},\"preferred\": true}}]";
            using FhirResponse<Patient> patch = await _client.PatchAsync(response.Resource, patchDocument);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
            Assert.Single(patch.Resource.Communication);
            Assert.Equal(true, patch.Resource.Communication[0].Preferred);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingAddPhonePatch_ThenServerShouldPatchNewPropertyCorrectly()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            poco.Address.Clear();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);
            Assert.Empty(response.Resource.Address);

            string patchDocument =
                "[{\"op\":\"add\",\"path\":\"/telecom/0/value\",\"value\":\"31201234569\"}]";
            using FhirResponse<Patient> patch = await _client.PatchAsync(response.Resource, patchDocument);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
            Assert.Equal("31201234569", patch.Resource.Telecom[0].Value);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingReplacePhonePatch_ThenServerShouldPatchNewPropertyCorrectly()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            poco.Address.Clear();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);
            Assert.Empty(response.Resource.Address);

            string patchDocument =
                "[{\"op\":\"add\",\"path\":\"/telecom/0/value\",\"value\":\"31201234569\"},{\"op\":\"replace\",\"path\":\"/telecom/0/value\",\"value\":\"31201234570\"}]";
            using FhirResponse<Patient> patch = await _client.PatchAsync(response.Resource, patchDocument);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
            Assert.Equal("31201234570", patch.Resource.Telecom[0].Value);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingRemovePhonePatch_ThenServerShouldPatchNewPropertyCorrectly()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            poco.Address.Clear();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);
            Assert.Empty(response.Resource.Address);

            string patchDocument =
                "[{\"op\":\"add\",\"path\":\"/telecom/0/value\",\"value\":\"31201234569\"},{\"op\":\"remove\",\"path\":\"/telecom/0/value\"}]";
            using FhirResponse<Patient> patch = await _client.PatchAsync(response.Resource, patchDocument);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
            Assert.Null(patch.Resource.Telecom[0].Value);
        }
    }
}
