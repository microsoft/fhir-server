// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
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
    [Trait(Traits.Category, Categories.Patch)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class JsonPatchTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;

        public JsonPatchTests(HttpIntegrationTestFixture fixture)
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

            using FhirResponse<Patient> patch = await _client.JsonPatchAsync(response.Resource, patchDocument);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
            Assert.Equal(AdministrativeGender.Female, patch.Resource.Gender);
            Assert.Empty(patch.Resource.Address);
        }

        [Theory]
        [InlineData("[{\"op\":\"replace\",\"value\":\"female\"}, {\"op\":\"remove\",\"path\":\"/address\"}]")]
        [InlineData("[{\"op\":\"coo\",\"path\":\"/gender\",\"value\":\"female\"}, {\"op\":\"remove\",\"path\":\"/address\"}]")]
        [InlineData("[{\"op\":\"replace\",\"path\":\"\",\"value\":\"\"}]")]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingIncorrectJsonPropertyPatch_ThenServerShouldBadRequest(string patch)
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);
            Assert.Equal(AdministrativeGender.Male, response.Resource.Gender);
            Assert.NotNull(response.Resource.Address);
            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.JsonPatchAsync(response.Resource, patch));
            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
        }

        // ToDo: Refactor this test as it contains a bundle with duplicated resources.
        /*
        [SkippableFact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatchDocument_WhenSubmittingABundleWithBinaryPatch_ThenServerShouldPatchCorrectly()
        {
            Skip.If(ModelInfoProvider.Version == FhirSpecification.Stu3, "Patch isn't supported in Bundles by STU3");

            var bundleWithPatch = Samples.GetJsonSample("Bundle-BinaryPatch").ToPoco<Bundle>();

            using FhirResponse<Bundle> patched = await _client.PostBundleAsync(bundleWithPatch);

            Assert.Equal(HttpStatusCode.OK, patched.Response.StatusCode);

            Assert.Equal(2, patched.Resource?.Entry?.Count);
            Assert.IsType<Patient>(patched.Resource.Entry[1].Resource);
            Assert.Equal(AdministrativeGender.Female, ((Patient)patched.Resource.Entry[1].Resource).Gender);
        }
        */

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
            using FhirResponse<Patient> patch = await _client.JsonPatchAsync(response.Resource, patchDocument);

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

            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.JsonPatchAsync(
                response.Resource,
                patchDocument));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
        }

        [Theory]
        [InlineData("/id", "abc")]
        [InlineData("/versionId", "100")]
        [InlineData("/meta/versionId", "100")]
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

            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.JsonPatchAsync(
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

            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.JsonPatchAsync(
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

            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.JsonPatchAsync(
                poco,
                patchDocument));

            Assert.Equal(HttpStatusCode.NotFound, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnPatchWhichWouldMakeResourceInvalid_WhenPatching_ThenAnErrorShouldBeReturned()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();

            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            string patchDocument =
                "[{\"op\":\"add\",\"path\":\"/deceasedDateTime\",\"value\":\"2015-02-14T13:42:00+10:00\"}]";

            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.JsonPatchAsync(
                response.Resource,
                patchDocument));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAWrongVersionInETag_WhenPatching_ThenAnErrorShouldBeReturned()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();

            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            string patchDocument =
                "[{\"op\":\"add\",\"path\":\"/deceasedDateTime\",\"value\":\"2015-02-14T13:42:00+10:00\"}]";

            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.JsonPatchAsync(
                response.Resource,
                patchDocument,
                "5"));

            Assert.Equal(HttpStatusCode.PreconditionFailed, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAValidEtag_WhenPatching_ThenAnResourceShouldBeUpdated()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();

            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            string patchDocument =
                "[{\"op\":\"replace\",\"path\":\"/gender\",\"value\":\"female\"}]";

            var patch = await _client.JsonPatchAsync(response.Resource, patchDocument, ifMatchVersion: "1");

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

            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.JsonPatchAsync(
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

            var patch = await _client.JsonPatchAsync(response.Resource, patchDocument);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnExistingResourceWithCorrectData_WhenPatchingWithPatchTest_ThenResourceShouldBePatched()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            poco.Deceased = new FhirBoolean(false);

            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            string patchDocument =
                "[{\"op\":\"test\",\"path\":\"/deceasedBoolean\",\"value\": false}, {\"op\":\"replace\",\"path\":\"/deceasedBoolean\",\"value\":\"true\"}]";

            var patch = await _client.JsonPatchAsync(response.Resource, patchDocument);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
            Assert.Equal(new FhirBoolean(true).ToString(), patch.Resource.Deceased.ToString());
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnExistingResourceWithMismatchingData_WhenPatchingWithPatchTest_ThenTheServerShouldError()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            poco.Deceased = new FhirBoolean(false);

            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            string patchDocument =
                "[{\"op\":\"test\",\"path\":\"/deceasedBoolean\",\"value\": true}, {\"op\":\"replace\",\"path\":\"/deceasedBoolean\",\"value\":\"false\"}]";

            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.JsonPatchAsync(
                response.Resource,
                patchDocument));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatchDocumentWithDateTime_WhenDateTimeHasOffset_ThenOffsetShouldBePreserved()
        {
            var poco = Samples.GetJsonSample("PatientWithMinimalData").ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            string dateTimeOffsetString = "2022-05-02T14:00:00+02:00";
            string patchDocument =
                "[{\"op\":\"add\",\"path\":\"/deceasedDateTime\",\"value\":\"" + dateTimeOffsetString + "\"}]";

            FhirResponse<Patient> patchResponse = await _client.JsonPatchAsync(response.Resource, patchDocument);
            Patient p = patchResponse.Resource;

            DateTimeOffset expectedDTO = DateTimeOffset.Parse(dateTimeOffsetString);
            DateTimeOffset receivedDTO = ((FhirDateTime)p.Deceased).ToDateTimeOffset(expectedDTO.Offset);

            // check that FhirDateTime conversions match
            Assert.Equal(new FhirDateTime(dateTimeOffsetString), p.Deceased);

            // check that DateTimeOffset conversions match
            Assert.Equal(expectedDTO, receivedDTO);

            // explicitly check hour and offset
            Assert.Equal(expectedDTO.Hour, receivedDTO.Hour);
            Assert.Equal(expectedDTO.Offset, receivedDTO.Offset);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatchDocument_WhenContainsDate_ThenShouldParseWithoutTimeAndOffset()
        {
            var parser = new Hl7.Fhir.Serialization.FhirJsonParser();

            string adJson = "{\"resourceType\":\"ActivityDefinition\",\"status\":\"active\"}";
            var poco = parser.
                Parse<Resource>(adJson).
                ToTypedElement().
                ToResourceElement().
                ToPoco<ActivityDefinition>();

            FhirResponse<ActivityDefinition> response = await _client.CreateAsync(poco);

            string dateString = "2022-05-02";
            string patchDocument =
                "[{\"op\":\"add\",\"path\":\"/approvalDate\",\"value\":\"" + dateString + "\"}]";

            FhirResponse<ActivityDefinition> patchResponse = await _client.JsonPatchAsync(response.Resource, patchDocument);
            ActivityDefinition ad = patchResponse.Resource;

            Assert.Equal(new Date(dateString), ad.ApprovalDateElement);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatchDocumentWithDate_WhenPassingDateTime_ThenExceptionShouldBeThrown()
        {
            // FHIR Date type can NOT contain any time information, with or without offset
            // Ensure that a RequestNotValidException is thrown if time is included, so that model does not enter invalid state

            var parser = new Hl7.Fhir.Serialization.FhirJsonParser();

            string adJson = "{\"resourceType\":\"ActivityDefinition\",\"status\":\"active\"}";
            var poco = parser.
                Parse<Resource>(adJson).
                ToTypedElement().
                ToResourceElement().
                ToPoco<ActivityDefinition>();

            FhirResponse<ActivityDefinition> response = await _client.CreateAsync(poco);

            string dateTimeString = "2022-05-02T14:00:00";
            string patchDocument =
                "[{\"op\":\"add\",\"path\":\"/approvalDate\",\"value\":\"" + dateTimeString + "\"}]";

            // DateTime without offset
            await Assert.ThrowsAsync<FhirClientException>(() => _client.JsonPatchAsync(response.Resource, patchDocument));

            string dateTimeOffsetString = "2022-05-02T14:00:00+02:00";
            patchDocument =
                "[{\"op\":\"add\",\"path\":\"/approvalDate\",\"value\":\"" + dateTimeOffsetString + "\"}]";

            // DateTime with offset
            await Assert.ThrowsAsync<FhirClientException>(() => _client.JsonPatchAsync(response.Resource, patchDocument));
        }
    }
}
