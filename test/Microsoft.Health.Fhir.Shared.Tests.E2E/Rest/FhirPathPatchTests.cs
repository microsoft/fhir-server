// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Helpers;
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
    public class FhirPathPatchTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;

        public FhirPathPatchTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingASimplePropertyPatch_ThenServerShouldPatchCorrectly()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);
            Assert.Equal(AdministrativeGender.Male, response.Resource.Gender);
            Assert.NotNull(response.Resource.Address);

            var patchRequest = new Parameters()
                .AddReplacePatchParameter("Patient.gender", new Code("female"))
                .AddDeletePatchParameter("Patient.address");

            FhirResponse<Patient> patch = await _client.FhirPatchAsync(response.Resource, patchRequest);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
            Assert.Equal(AdministrativeGender.Female, patch.Resource.Gender);
            Assert.Empty(patch.Resource.Address);
        }

        public static IEnumerable<object[]> GetIncorrectRequestTestData()
        {
            yield return new object[] { new Parameters().AddPatchParameter("replace", value: new Code("female")), "Patch replace operations must have the 'path' part." };
            yield return new object[] { new Parameters().AddPatchParameter("coo", path: "Patient.gender", value: new Code("femaale")), "Invalid patch operation type: 'coo'. Only 'add', 'insert', 'delete', 'replace', and 'move' are allowed." };
            yield return new object[] { new Parameters().AddPatchParameter("replace"), "Patch replace operations must have the 'path' part." };
            yield return new object[] { new Parameters().AddPatchParameter("replace", path: "Patient.gender"), "Patch replace operations must have the 'value' part." };
        }

        [Theory]
        [MemberData(nameof(GetIncorrectRequestTestData))]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingInvalidFhirPatch_ThenServerShouldBadRequest(Parameters patchRequest, string expectedError)
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);
            Assert.Equal(AdministrativeGender.Male, response.Resource.Gender);
            Assert.NotNull(response.Resource.Address);

            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.FhirPatchAsync(response.Resource, patchRequest));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
            var responseObject = exception.Response.ToT();
            Assert.Equal(expectedError, responseObject.Issue[0].Diagnostics);
            Assert.Equal(OperationOutcome.IssueType.Invalid, responseObject.Issue[0].Code);
        }

        // [SkippableFact]
        // This test used to require sequential processing. I'm removing the skippable tag from it.
        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatchDocument_WhenSubmittingAParallelBundleWithDuplicatedPatch_ThenServerShouldReturnAnError()
        {
            Skip.If(ModelInfoProvider.Version == FhirSpecification.Stu3, "Patch isn't supported in Bundles by STU3");

            var bundleWithPatch = Samples.GetJsonSample("Bundle-FhirPatch").ToPoco<Bundle>();

            using FhirResponse<Bundle> fhirResponse = await _client.PostBundleAsync(bundleWithPatch, new FhirBundleOptions() { BundleProcessingLogic = FhirBundleProcessingLogic.Parallel });

            Assert.Equal(HttpStatusCode.OK, fhirResponse.Response.StatusCode);

            Bundle resource = fhirResponse.Resource;

            // Duplicated records. Only one should successed. As the requests are processed in parallel,
            // it's not possible to pick the one that will be processed.
            if (resource.Entry[1].Response.Status == "200")
            {
                Assert.Equal("200", resource.Entry[1].Response.Status); // PATCH
                Assert.Equal("400", resource.Entry[2].Response.Status); // PATCH (Duplicate)
            }
            else
            {
                Assert.Equal("400", resource.Entry[1].Response.Status); // PATCH (Duplicate)
                Assert.Equal("200", resource.Entry[2].Response.Status); // PATCH
            }
        }

        [SkippableFact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatchDocument_WhenSubmittingABundleWithFhirPatch_ThenServerShouldPatchCorrectly()
        {
            Skip.If(ModelInfoProvider.Version == FhirSpecification.Stu3, "Patch isn't supported in Bundles by STU3");

            var bundleWithPatch = Samples.GetJsonSample("Bundle-FhirPatch").ToPoco<Bundle>();

            // This test required sequential bundle processing.
            using FhirResponse<Bundle> patched = await _client.PostBundleAsync(bundleWithPatch, new FhirBundleOptions() { BundleProcessingLogic = FhirBundleProcessingLogic.Sequential });

            Assert.Equal(HttpStatusCode.OK, patched.Response.StatusCode);

            // Get the final result of our patch
            Assert.IsType<Patient>(patched.Resource.Entry.Last().Resource);
            var patchedPatient = patched.Resource.Entry.Last().Resource as Patient;

            // Check first patch
            Assert.Equal(AdministrativeGender.Female, patchedPatient.Gender);

            // Check second patch
            Coding patchedValue = patchedPatient
                                    .Extension.Single(x => x.Url == "http://hl7.org/fhir/us/core/StructureDefinition/us-core-race")
                                    .Extension.Single(x => x.Url == "ombCategory")
                                    .Value as Coding;
            Coding expectedValue = new Coding(system: "urn:oid:2.16.840.1.113883.6.238", code: "2054-5", display: "Black or African American");

            // The base objects aren't equal so we have to compare the members we care about
            Assert.Equal(expectedValue.System, patchedValue.System);
            Assert.Equal(expectedValue.Code, patchedValue.Code);
            Assert.Equal(expectedValue.Display, patchedValue.Display);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingObjectPropertyPatch_ThenServerShouldPatchCorrectly()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            poco.Address.Clear();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);
            Assert.Empty(response.Resource.Address);

            var patchRequest = new Parameters().AddPatchParameter("add", "Patient", "address", new Address
            {
                Use = Address.AddressUse.Home,
                Line = new[] { "23 thule st", "avon" },
                City = "Big Smoke",
                Country = "erewhon",
                Text = "23 thule st",
            });
            using FhirResponse<Patient> patch = await _client.FhirPatchAsync(response.Resource, patchRequest);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
            Assert.Single(patch.Resource.Address);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingPatchUsingWhere_ThenServerShouldPatchCorrectly()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            var patchRequest = new Parameters().AddPatchParameter("replace", "Patient.address.where(use = 'home').city", value: new FhirString("Portland"));
            using FhirResponse<Patient> patch = await _client.FhirPatchAsync(response.Resource, patchRequest);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
            Assert.Single(patch.Resource.Address);
            Assert.Equal("Portland", patch.Resource.Address.First().City);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingPatchOnInvalidProperty_ThenAnErrorShouldBeReturned()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            var patchRequest = new Parameters().AddAddPatchParameter("Patient", "dummyProperty", new FhirString("dummy"));

            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.FhirPatchAsync(
                response.Resource,
                patchRequest));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
            var responseObject = exception.Response.ToT();
            Assert.Equal("Element dummyProperty not found at Patient when processing patch add operation.", responseObject.Issue[0].Diagnostics);
            Assert.Equal(OperationOutcome.IssueType.Invalid, responseObject.Issue[0].Code);
        }

        public static IEnumerable<object[]> GetForbiddenPropertyTestData()
        {
            yield return new object[] { new Parameters().AddReplacePatchParameter("Patient.id", new Id("abc")) };
            yield return new object[] { new Parameters().AddReplacePatchParameter("Patient.meta.versionId", new Id("100")) };
            yield return new object[] { new Parameters().AddReplacePatchParameter("Patient.text.div", new XHtml("<div>dummy narrative</div>")) };
            yield return new object[] { new Parameters().AddReplacePatchParameter("Patient.text.status", new Code("extensions")) };
        }

        [Theory]
        [MemberData(nameof(GetForbiddenPropertyTestData))]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingPatchOnForbiddenProperty_ThenAnErrorShouldBeReturned(Parameters patchRequest)
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.FhirPatchAsync(
                response.Resource,
                patchRequest));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
            var responseObject = exception.Response.ToT();
            Assert.Contains("immutable", responseObject.Issue[0].Diagnostics);
            Assert.Equal(OperationOutcome.IssueType.Invalid, responseObject.Issue[0].Code);
        }

        public static IEnumerable<object[]> GetInvalidDataValueTestData()
        {
            yield return new object[] { "Patient.gender", new FhirString("dummyGender"), "Invalid input for gender at Patient.gender when processing patch replace operation." };
            yield return new object[] { "Patient.birthDate", new FhirString("abc"), "Invalid input for birthDate at Patient.birthDate when processing patch replace operation." };
            yield return new object[] { "Patient.address[0].use", new FhirString("dummyAddress"), "Invalid input for use at Patient.address[0].use when processing patch replace operation." };
        }

        [Theory]
        [MemberData(nameof(GetInvalidDataValueTestData))]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingPatchWithInvalidValue_ThenAnErrorShouldBeReturned(string patchPath, DataType patchValue, string expectedError)
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            var patchRequest = new Parameters().AddReplacePatchParameter(patchPath, patchValue)
;
            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.FhirPatchAsync(
                response.Resource,
                patchRequest));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
            var responseObject = exception.Response.ToT();

            Assert.Equal(expectedError, responseObject.Issue[0].Diagnostics);
            Assert.Equal(OperationOutcome.IssueType.Invalid, responseObject.Issue[0].Code);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnMissingResource_WhenPatching_ThenAnErrorShouldBeReturned()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            poco.Id = Guid.NewGuid().ToString();

            var patchRequest = new Parameters().AddAddPatchParameter("Patient", "deceased", new FhirDateTime("2015-02-14T13:42:00+10:00"));

            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.FhirPatchAsync(
                poco,
                patchRequest));

            Assert.Equal(HttpStatusCode.NotFound, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnPatchWhichWouldAddOnExistingProperty_WhenPatching_ThenAnErrorShouldBeReturned()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();

            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            var patchRequest = new Parameters().AddAddPatchParameter("Patient", "deceased", new FhirDateTime("2015-02-14T13:42:00+10:00"));

            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.FhirPatchAsync(
                response.Resource,
                patchRequest));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
            var responseObject = exception.Response.ToT();
            Assert.Contains("Existing element deceased found", responseObject.Issue[0].Diagnostics);
            Assert.Equal(OperationOutcome.IssueType.Invalid, responseObject.Issue[0].Code);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnPatchWhichWouldReplaceExistingProperty_WhenPatching_ThenServerShouldPatchCorrectly()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();

            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            // Given that the deceasedBoolean property already exists, attempt to replace with deceasedDateTime
            var patchRequest = new Parameters().AddReplacePatchParameter("Patient.deceased", new FhirDateTime("2015-02-14T13:42:00+10:00"));

            using FhirResponse<Patient> patch = await _client.FhirPatchAsync(response.Resource, patchRequest);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
            Assert.Equal(new FhirDateTime("2015-02-14T13:42:00+10:00"), patch.Resource.Deceased);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAWrongVersionInETag_WhenPatching_ThenAnErrorShouldBeReturned()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();

            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            var patchRequest = new Parameters().AddAddPatchParameter("Patient", "deceased", new FhirDateTime("2015-02-14T13:42:00+10:00"));

            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.FhirPatchAsync(
                response.Resource,
                patchRequest,
                "5"));

            Assert.Equal(HttpStatusCode.PreconditionFailed, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAValidEtag_WhenPatching_ThenAnResourceShouldBeUpdated()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();

            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            var patchRequest = new Parameters().AddReplacePatchParameter("Patient.gender", new Code("female"));

            var patch = await _client.FhirPatchAsync(response.Resource, patchRequest, ifMatchVersion: "1");

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnInvalidPartDefinition_WhenPatching_ThenAnErrorShouldBeReturned()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();

            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            var versionSpecificResourceReference = new ResourceReference() { Reference = "Patient/123", Display = "Test Patient" };
            if (ModelInfoProvider.Version != FhirSpecification.Stu3)
            {
                versionSpecificResourceReference.Type = "Patient";
            }

            var patchRequest = new Parameters().AddPatchParameter("add", "Patient", "link", new List<Parameters.ParameterComponent>()
            {
                new Parameters.ParameterComponent { Name = "value", Value = new Code("replaced-by") },
                new Parameters.ParameterComponent { Name = "value", Value = versionSpecificResourceReference },
            });

            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.FhirPatchAsync(
                response.Resource,
                patchRequest));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
            var responseObject = exception.Response.ToT();
            Assert.Contains("not found at Patient", responseObject.Issue[0].Diagnostics);
            Assert.Equal(OperationOutcome.IssueType.Invalid, responseObject.Issue[0].Code);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnPatchWithAnonymousObjectContainingResource_WhenPatching_ThenAnResourceShouldBeUpdated()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();

            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            var versionSpecificResourceReference = new ResourceReference() { Reference = "Patient/123", Display = "Test Patient" };
            if (ModelInfoProvider.Version != FhirSpecification.Stu3)
            {
                versionSpecificResourceReference.Type = "Patient";
            }

            var patchRequest = new Parameters().AddPatchParameter("add", "Patient", "link", new List<Parameters.ParameterComponent>()
            {
                new Parameters.ParameterComponent { Name = "type", Value = new Code("replaced-by") },
                new Parameters.ParameterComponent { Name = "other", Value = versionSpecificResourceReference },
            });

            var patch = await _client.FhirPatchAsync(response.Resource, patchRequest);

            Assert.Equal(HttpStatusCode.OK, patch.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatchDocumentWithDateTime_WhenDateTimeHasOffset_ThenOffsetShouldBePreserved()
        {
            var poco = Samples.GetJsonSample("PatientWithMinimalData").ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            string dateTimeOffsetString = "2022-05-02T14:00:00+02:00";
            var patchRequest = new Parameters().AddAddPatchParameter("Patient", "deceased", new FhirDateTime(dateTimeOffsetString));

            using FhirResponse<Patient> patchResponse = await _client.FhirPatchAsync(response.Resource, patchRequest);
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
            var patchRequest = new Parameters().AddAddPatchParameter("ActivityDefinition", "approvalDate", new Date(dateString));
            using FhirResponse<ActivityDefinition> patchResponse = await _client.FhirPatchAsync(response.Resource, patchRequest);
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
            var patchRequest = new Parameters().AddAddPatchParameter("ActivityDefinition", "approvalDate", new FhirDateTime(dateTimeString));

            // DateTime without offset
            await Assert.ThrowsAsync<FhirClientException>(() => _client.FhirPatchAsync(response.Resource, patchRequest));

            string dateTimeOffsetString = "2022-05-02T14:00:00+02:00";
            patchRequest = new Parameters().AddAddPatchParameter("ActivityDefinition", "approvalDate", new FhirDateTime(dateTimeOffsetString));

            // DateTime with offset
            await Assert.ThrowsAsync<FhirClientException>(() => _client.FhirPatchAsync(response.Resource, patchRequest));
        }
    }
}
