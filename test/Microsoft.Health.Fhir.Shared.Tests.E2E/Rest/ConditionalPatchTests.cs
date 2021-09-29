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
using Microsoft.Health.Fhir.Web;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    [Trait(Traits.Category, Categories.ConditionalUpdate)]
    public class ConditionalPatchTests : IClassFixture<HttpIntegrationTestFixture<Startup>>
    {
        private readonly TestFhirClient _client;

        public ConditionalPatchTests(HttpIntegrationTestFixture<Startup> fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenConditionWithNoExistingResources_WhenPatching_TheServerShouldReturnNoFound()
        {
            string patchDocument =
             "[{\"op\":\"replace\",\"path\":\"/gender\",\"value\":\"female\"}, {\"op\":\"remove\",\"path\":\"/address\"}]";

            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.ConditionalPatchAsync<Patient>("Patient", $"identifier={Guid.NewGuid()}", patchDocument));

            Assert.Equal(HttpStatusCode.NotFound, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenNoCondition_WhenPatching_ThenAnErrorShouldBeReturned()
        {
            string patchDocument =
            "[{\"op\":\"replace\",\"path\":\"/gender\",\"value\":\"female\"}, {\"op\":\"remove\",\"path\":\"/address\"}]";
            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.ConditionalPatchAsync<Patient>("Patient", string.Empty, patchDocument));

            Assert.Equal(HttpStatusCode.PreconditionFailed, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResource_WhenPatchingConditionallyWithOneMatch_TheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            var patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            var identifier = Guid.NewGuid().ToString();

            patient.Identifier.Add(new Identifier("http://e2etests", identifier));
            using FhirResponse<Patient> response = await _client.CreateAsync(patient);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            string patchDocument =
           "[{\"op\":\"replace\",\"path\":\"/gender\",\"value\":\"female\"}, {\"op\":\"remove\",\"path\":\"/address\"}]";

            using FhirResponse<Patient> patchResponse = await _client.ConditionalPatchAsync<Patient>(
                "Patient",
                $"identifier={identifier}",
                patchDocument);
            Assert.Equal(AdministrativeGender.Female, patchResponse.Resource.Gender);
            Assert.Empty(patchResponse.Resource.Address);
            Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResource_WhenPatchingConditionallyWithMultipleMatches_TheServerShouldFail()
        {
            var patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            var identifier = Guid.NewGuid().ToString();
            patient.Identifier.Add(new Identifier("http://e2etests", identifier));

            using FhirResponse<Patient> response = await _client.CreateAsync(patient);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            using FhirResponse<Patient> response2 = await _client.CreateAsync(patient);
            Assert.Equal(HttpStatusCode.Created, response2.StatusCode);

            var observation2 = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation2.Id = null;

            string patchDocument =
          "[{\"op\":\"replace\",\"path\":\"/gender\",\"value\":\"female\"}, {\"op\":\"remove\",\"path\":\"/address\"}]";

            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.ConditionalPatchAsync<Patient>(
                "Patient",
                $"identifier={identifier}",
                patchDocument));

            Assert.Equal(HttpStatusCode.PreconditionFailed, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenSecondVersionOfResource_WhenPatchingConditionallyWithOneMatchAndExactVersion_TheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            var patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            var identifier = Guid.NewGuid().ToString();

            patient.Identifier.Add(new Identifier("http://e2etests", identifier));
            using FhirResponse<Patient> response = await _client.CreateAsync(patient);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            response.Resource.BirthDate = "2020-01-01";
            using FhirResponse<Patient> secondVersion = await _client.UpdateAsync(response.Resource);

            string patchDocument =
           "[{\"op\":\"replace\",\"path\":\"/gender\",\"value\":\"female\"}, {\"op\":\"remove\",\"path\":\"/address\"}]";

            using FhirResponse<Patient> patchResponse = await _client.ConditionalPatchAsync<Patient>(
                "Patient",
                $"identifier={identifier}",
                patchDocument,
                "2");
            Assert.Equal(AdministrativeGender.Female, patchResponse.Resource.Gender);
            Assert.Empty(patchResponse.Resource.Address);
            Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenSecondVersionOfResource_WhenPatchingConditionallyWithOneMatchAndWrongVersion_TheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            var patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            var identifier = Guid.NewGuid().ToString();

            patient.Identifier.Add(new Identifier("http://e2etests", identifier));
            using FhirResponse<Patient> response = await _client.CreateAsync(patient);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            response.Resource.BirthDate = "2020-01-01";
            using FhirResponse<Patient> secondVersion = await _client.UpdateAsync(response.Resource);

            string patchDocument =
           "[{\"op\":\"replace\",\"path\":\"/gender\",\"value\":\"female\"}, {\"op\":\"remove\",\"path\":\"/address\"}]";

            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.ConditionalPatchAsync<Patient>(
                "Patient",
                $"identifier={identifier}",
                patchDocument,
                "3"));
            Assert.Equal(HttpStatusCode.PreconditionFailed, exception.Response.StatusCode);
        }
    }
}
