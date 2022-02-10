﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
            yield return new object[] { new Parameters().AddPatchParameter("replace", value: new Code("female")).AddDeletePatchParameter("Patient.address") };
            yield return new object[] { new Parameters().AddPatchParameter("coo", path: "Patient.gender", value: new Code("femaale")).AddDeletePatchParameter("Patient.address") };
            yield return new object[] { new Parameters().AddPatchParameter("replace") };
            yield return new object[] { new Parameters().AddPatchParameter("replace", path: "Patient.gender") };
        }

        [Theory]
        [MemberData(nameof(GetIncorrectRequestTestData))]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingInvalidFhirPatch_ThenServerShouldBadRequest(Parameters patchRequest)
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);
            Assert.Equal(AdministrativeGender.Male, response.Resource.Gender);
            Assert.NotNull(response.Resource.Address);
            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.FhirPatchAsync(response.Resource, patchRequest));
            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
        }

        [SkippableFact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatchDocument_WhenSubmittingABundleWithFhirPatch_ThenServerShouldPatchCorrectly()
        {
            Skip.If(ModelInfoProvider.Version == FhirSpecification.Stu3, "Patch isn't supported in Bundles by STU3");

            var bundleWithPatch = Samples.GetJsonSample("Bundle-FhirPatch").ToPoco<Bundle>();

            using FhirResponse<Bundle> patched = await _client.PostBundleAsync(bundleWithPatch);

            Assert.Equal(HttpStatusCode.OK, patched.Response.StatusCode);

            Assert.Equal(2, patched.Resource?.Entry?.Count);
            Assert.IsType<Patient>(patched.Resource.Entry[1].Resource);
            Assert.Equal(AdministrativeGender.Female, ((Patient)patched.Resource.Entry[1].Resource).Gender);
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

            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.FhirPatchAsync(
                response.Resource,
                patchRequest));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
        }

        public static IEnumerable<object[]> GetForbiddenPropertyTestData()
        {
            yield return new object[] { new Parameters().AddReplacePatchParameter("Patient.id", new FhirString("abc")) };
            yield return new object[] { new Parameters().AddReplacePatchParameter("Patient.versionId", new FhirString("100")) };
            yield return new object[] { new Parameters().AddReplacePatchParameter("Patient.meta.versionId", new FhirString("100")) };
            yield return new object[] { new Parameters().AddReplacePatchParameter("Patient.resourceType", new FhirString("DummyResource")) };
            yield return new object[] { new Parameters().AddReplacePatchParameter("Patient.text.div", new FhirString("<div>dummy narrative</div>")) };
            yield return new object[] { new Parameters().AddReplacePatchParameter("Patient.text.status", new FhirString("extensions")) };
        }

        [Theory]
        [MemberData(nameof(GetForbiddenPropertyTestData))]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingPatchOnForbiddenProperty_ThenAnErrorShouldBeReturned(Parameters patchRequest)
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.FhirPatchAsync(
                response.Resource,
                patchRequest));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
        }

        public static IEnumerable<object[]> GetInvalidDataValueTestData()
        {
            yield return new object[] { new Parameters().AddReplacePatchParameter("Patient.gender", new FhirString("dummyGender")) };
            yield return new object[] { new Parameters().AddReplacePatchParameter("Patient.birthDate", new FhirString("abc")) };
            yield return new object[] { new Parameters().AddReplacePatchParameter("Patient.address[0]use", new FhirString("dummyAddress")) };
        }

        [Theory]
        [MemberData(nameof(GetInvalidDataValueTestData))]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAServerThatSupportsIt_WhenSubmittingPatchWithInvalidValue_ThenAnErrorShouldBeReturned(Parameters patchRequest)
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            FhirResponse<Patient> response = await _client.CreateAsync(poco);

            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.FhirPatchAsync(
                response.Resource,
                patchRequest));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnMissingResource_WhenPatching_ThenAnErrorShouldBeReturned()
        {
            var poco = Samples.GetDefaultPatient().ToPoco<Patient>();
            poco.Id = Guid.NewGuid().ToString();

            var patchRequest = new Parameters().AddAddPatchParameter("Patient", "deceased", new FhirDateTime("2015-02-14T13:42:00+10:00"));

            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.FhirPatchAsync(
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

            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.FhirPatchAsync(
                response.Resource,
                patchRequest));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
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

            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.FhirPatchAsync(
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
    }
}
