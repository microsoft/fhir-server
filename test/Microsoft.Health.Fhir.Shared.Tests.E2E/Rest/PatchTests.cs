// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
        public async Task GivenAServerThatSupportsIt_WhenSubmittingALevel1PropertyPatch_ThenServerShouldPatchCorrectly()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);
            Assert.Equal(AdministrativeGender.Male, response.Resource.Gender);
            Assert.NotNull(response.Resource.Address);

            string patchDocument =
                "[{\"op\":\"replace\",\"path\":\"/gender\",\"value\":\"female\"}, {\"op\":\"remove\",\"path\":\"/address\"}]";

            using FhirResponse<Patient> patch = await _client.PatchAsync(response.Resource, patchDocument);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
            Assert.Equal(AdministrativeGender.Female, patch.Resource.Gender);
            Assert.Empty(patch.Resource.Address);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingALevel2PropertyPatch_ThenServerShouldPatchCorrectly()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            poco.Address.Clear();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);
            Assert.Empty(response.Resource.Address);

            string patchDocument =
                "[{\"op\":\"add\",\"path\":\"/address\",\"value\":[]},{\"op\":\"add\",\"path\":\"/address/0\",\"value\":{\"use\":\"home\",\"line\":[\"23 thule st\",\"avon\"],\"city\":\"Big Smoke\",\"country\":\"erewhon\",\"text\":\"23 thule st\"}}]";
            using FhirResponse<Patient> patch = await _client.PatchAsync(response.Resource, patchDocument);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
            Assert.Single(patch.Resource.Address);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingAnInvalidPropertyPatch_ThenAnErrorShouldBeReturned()
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
        [InlineData("/id", "abc")]
        [InlineData("/versionId", "100")]
        [InlineData("/resourceType", "DummyResource")]
        [InlineData("/text/div", "<div>dummy narrative</div>")]
        [InlineData("/text/status", "extensions")]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingAForbiddenPropertyPatch_ThenAnErrorShouldBeReturned(
            string propertyName, string value)
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            string patchDocument =
                "[{\"op\":\"replace\",\"path\":\"" + propertyName + "\",\"value\":\"" + value + "\"}]";

            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.PatchAsync(
                response.Resource,
                patchDocument));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
        }

        [Theory]
        [InlineData("/gender", "dummyGender")]
        [InlineData("/birthDate", "abc")]
        [InlineData("/address/0/use", "dummyAddress")]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingAInvalidValuePatch_ThenAnErrorShouldBeReturned(string propertyName, string value)
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            string patchDocument =
                "[{\"op\":\"replace\",\"path\":\"" + propertyName + "\",\"value\":\"" + value + "\"}]";

            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.PatchAsync(
                response.Resource,
                patchDocument));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnMissingResource_WhenPatching_ThenAnErrorShouldBeReturned()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            poco.Id = Guid.NewGuid().ToString();

            string patchDocument =
                "[{\"op\":\"add\",\"path\":\"/deceasedDateTime\",\"value\":\"2015-02-14T13:42:00+10:00\"}]";

            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.PatchAsync(
                poco,
                patchDocument));

            Assert.Equal(HttpStatusCode.NotFound, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnInvalidEtag_WhenPatching_ThenAnErrorShouldBeReturned()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();

            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            string patchDocument =
                "[{\"op\":\"add\",\"path\":\"/deceasedDateTime\",\"value\":\"2015-02-14T13:42:00+10:00\"}]";

            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.PatchAsync(
                response.Resource,
                patchDocument,
                "5"));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAValidEtag_WhenPatching_ThenAnResourceShouldBeUpdated()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();

            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            string patchDocument =
                "[{\"op\":\"add\",\"path\":\"/deceasedDateTime\",\"value\":\"2015-02-14T13:42:00+10:00\"}]";

            var patch = await _client.PatchAsync(response.Resource, patchDocument, ifMatchVersion: "1");

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnExistingResource_WhenPatchingWithAChangedChoiceType_ThenAnErrorShouldBeReturned()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            poco.Deceased = new FhirBoolean(false);

            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            // Given that the deceasedBoolean property already exists, attempt to add a deceasedDateTime
            string patchDocument =
                "[{\"op\":\"add\",\"path\":\"/deceasedDateTime\",\"value\":\"2015-02-14T13:42:00+10:00\"}]";

            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.PatchAsync(
                response.Resource,
                patchDocument));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnExistingResource_WhenPatchingToChangeAChoiceType_ThenAnErrorShouldBeReturned()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            poco.Deceased = new FhirBoolean(false);

            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            // Given that the deceasedBoolean property already exists, attempt to add a deceasedDateTime
            string patchDocument =
                "[{\"op\":\"add\",\"path\":\"/deceasedDateTime\",\"value\":\"2015-02-14T13:42:00+10:00\"}, {\"op\":\"remove\",\"path\":\"/deceasedBoolean\"}]";

            var patch = await _client.PatchAsync(response.Resource, patchDocument);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
        }
    }
}
