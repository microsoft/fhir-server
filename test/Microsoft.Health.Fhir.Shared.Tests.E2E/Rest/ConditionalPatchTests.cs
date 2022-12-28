// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
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
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.ConditionalOperations)]
    [Trait(Traits.Category, Categories.Patch)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class ConditionalPatchTests : IClassFixture<HttpIntegrationTestFixture<Startup>>
    {
        private readonly TestFhirClient _client;

        private const string _patchDocumentJson =
            "[{\"op\":\"replace\",\"path\":\"/gender\",\"value\":\"female\"}, {\"op\":\"remove\",\"path\":\"/address\"}]";

        private static Parameters _fhirPatchRequest = new Parameters()
            .AddReplacePatchParameter("Patient.gender", new Code("female"))
            .AddDeletePatchParameter("Patient.address");

        public ConditionalPatchTests(HttpIntegrationTestFixture<Startup> fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnObservation_WhenPatchingConditionally_TheServerRespondsWithCorrectMessage()
        {
            Observation observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = Guid.NewGuid().ToString();
            using FhirResponse<Observation> response = await _client.CreateAsync(observation);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            Parameters observationFhirPatchRequest = new Parameters().AddReplacePatchParameter("subject.reference", new FhirString("Patient/"));

            FhirException exceptionFhir = await Assert.ThrowsAsync<FhirException>(() => _client.ConditionalFhirPatchAsync<Observation>(
                "Observation",
                $"id={response.Resource.Id}",
                observationFhirPatchRequest));

            Assert.Equal(HttpStatusCode.PreconditionFailed, exceptionFhir.StatusCode);
            Assert.True(exceptionFhir.Response.Resource.Issue[0].Diagnostics.Equals(string.Format(Core.Resources.ConditionalOperationNotSelectiveEnough, observation.TypeName)));
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenConditionWithNoExistingResources_WhenPatching_TheServerShouldReturnNoFound()
        {
            var exceptionJson = await Assert.ThrowsAsync<FhirException>(() =>
                _client.ConditionalJsonPatchAsync<Patient>("Patient", $"identifier={Guid.NewGuid()}", _patchDocumentJson));
            var exceptionFhir = await Assert.ThrowsAsync<FhirException>(() =>
                _client.ConditionalFhirPatchAsync<Patient>("Patient", $"identifier={Guid.NewGuid()}", _fhirPatchRequest));

            Assert.Equal(HttpStatusCode.NotFound, exceptionJson.Response.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, exceptionFhir.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenNoCondition_WhenPatching_ThenAnErrorShouldBeReturned()
        {
            var exceptionJson = await Assert.ThrowsAsync<FhirException>(() =>
                _client.ConditionalJsonPatchAsync<Patient>("Patient", string.Empty, _patchDocumentJson));
            var exceptionFhir = await Assert.ThrowsAsync<FhirException>(() =>
                _client.ConditionalFhirPatchAsync<Patient>("Patient", string.Empty, _fhirPatchRequest));

            Assert.Equal(HttpStatusCode.PreconditionFailed, exceptionJson.Response.StatusCode);
            Assert.Equal(HttpStatusCode.PreconditionFailed, exceptionFhir.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResource_WhenJsonPatchingConditionallyWithOneMatch_TheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            var patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            var identifier = Guid.NewGuid().ToString();

            patient.Identifier.Add(new Identifier("http://e2etests", identifier));
            using FhirResponse<Patient> response = await _client.CreateAsync(patient);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            using FhirResponse<Patient> jsonPatchResponse = await _client.ConditionalJsonPatchAsync<Patient>(
                "Patient",
                $"identifier={identifier}",
                _patchDocumentJson);

            Assert.Equal(AdministrativeGender.Female, jsonPatchResponse.Resource.Gender);
            Assert.Empty(jsonPatchResponse.Resource.Address);
            Assert.Equal(HttpStatusCode.OK, jsonPatchResponse.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResource_WhenFhirPatchingConditionallyWithOneMatch_TheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            var patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            var identifier = Guid.NewGuid().ToString();

            patient.Identifier.Add(new Identifier("http://e2etests", identifier));
            using FhirResponse<Patient> response = await _client.CreateAsync(patient);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            using FhirResponse<Patient> fhirPatchResponse = await _client.ConditionalFhirPatchAsync<Patient>(
                "Patient",
                $"identifier={identifier}",
                _fhirPatchRequest);

            Assert.Equal(AdministrativeGender.Female, fhirPatchResponse.Resource.Gender);
            Assert.Empty(fhirPatchResponse.Resource.Address);
            Assert.Equal(HttpStatusCode.OK, fhirPatchResponse.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResource_WhenJsonPatchingConditionallyWithMultipleMatches_TheServerShouldFail()
        {
            var patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            var identifier = Guid.NewGuid().ToString();
            patient.Identifier.Add(new Identifier("http://e2etests", identifier));

            using FhirResponse<Patient> response = await _client.CreateAsync(patient);
            using FhirResponse<Patient> response2 = await _client.CreateAsync(patient);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(HttpStatusCode.Created, response2.StatusCode);

            var observation2 = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation2.Id = null;

            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.ConditionalJsonPatchAsync<Patient>(
                "Patient",
                $"identifier={identifier}",
                _patchDocumentJson));

            Assert.Equal(HttpStatusCode.PreconditionFailed, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResource_WhenFhirPatchingConditionallyWithMultipleMatches_TheServerShouldFail()
        {
            var patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            var identifier = Guid.NewGuid().ToString();
            patient.Identifier.Add(new Identifier("http://e2etests", identifier));

            using FhirResponse<Patient> response = await _client.CreateAsync(patient);
            using FhirResponse<Patient> response2 = await _client.CreateAsync(patient);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(HttpStatusCode.Created, response2.StatusCode);

            var observation2 = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation2.Id = null;

            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.ConditionalFhirPatchAsync<Patient>(
                "Patient",
                $"identifier={identifier}",
                _fhirPatchRequest));

            Assert.Equal(HttpStatusCode.PreconditionFailed, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenSequentialVersionOfResource_WhenJsonPatchingConditionallyWithOneMatchAndExactVersion_TheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            var patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            var identifier = Guid.NewGuid().ToString();

            patient.Identifier.Add(new Identifier("http://e2etests", identifier));
            using FhirResponse<Patient> response = await _client.CreateAsync(patient);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            response.Resource.BirthDate = "2020-01-01";
            using FhirResponse<Patient> secondVersion = await _client.UpdateAsync(response.Resource);

            using FhirResponse<Patient> jsonPatchResponse = await _client.ConditionalJsonPatchAsync<Patient>(
                "Patient",
                $"identifier={identifier}",
                _patchDocumentJson,
                "2");

            Assert.Equal(AdministrativeGender.Female, jsonPatchResponse.Resource.Gender);
            Assert.Empty(jsonPatchResponse.Resource.Address);
            Assert.Equal(HttpStatusCode.OK, jsonPatchResponse.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenSequentialVersionOfResource_WhenFhirPatchingConditionallyWithOneMatchAndExactVersion_TheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            var patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            var identifier = Guid.NewGuid().ToString();

            patient.Identifier.Add(new Identifier("http://e2etests", identifier));
            using FhirResponse<Patient> response = await _client.CreateAsync(patient);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            response.Resource.BirthDate = "2020-01-01";
            using FhirResponse<Patient> secondVersion = await _client.UpdateAsync(response.Resource);

            using FhirResponse<Patient> fhirPatchResponse = await _client.ConditionalFhirPatchAsync<Patient>(
                "Patient",
                $"identifier={identifier}",
                _fhirPatchRequest,
                "2");

            Assert.Equal(AdministrativeGender.Female, fhirPatchResponse.Resource.Gender);
            Assert.Empty(fhirPatchResponse.Resource.Address);
            Assert.Equal(HttpStatusCode.OK, fhirPatchResponse.StatusCode);
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

            var exceptionJson = await Assert.ThrowsAsync<FhirException>(() => _client.ConditionalJsonPatchAsync<Patient>(
                "Patient",
                $"identifier={identifier}",
                _patchDocumentJson,
                "3"));
            var exceptionFhir = await Assert.ThrowsAsync<FhirException>(() => _client.ConditionalFhirPatchAsync<Patient>(
                "Patient",
                $"identifier={identifier}",
                _fhirPatchRequest,
                "3"));

            Assert.Equal(HttpStatusCode.PreconditionFailed, exceptionJson.Response.StatusCode);
            Assert.Equal(HttpStatusCode.PreconditionFailed, exceptionFhir.Response.StatusCode);
        }
    }
}
