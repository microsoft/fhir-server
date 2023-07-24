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
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.ConditionalOperations)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class ConditionalUpdateTests : IClassFixture<HttpIntegrationTestFixture<Startup>>
    {
        private readonly TestFhirClient _client;

        public ConditionalUpdateTests(HttpIntegrationTestFixture<Startup> fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenABundledResourceTypeWithMissingId_WhenCreatingConditionally_TheServerRespondsWithCorrectMessage()
        {
            Bundle bundle = Samples.GetJsonSample("Bundle-MissingIdentifier").ToPoco<Bundle>();
            FhirClientException exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.ConditionalUpdateAsync(bundle, string.Empty));
            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
            Assert.Equal(exception.Response.Resource.Issue[0].Diagnostics, string.Format(Core.Resources.ConditionalOperationNotSelectiveEnough, bundle.TypeName));
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResource_WhenUpsertingConditionallyWithNoIdAndNoExisting_TheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = null;

            using FhirResponse<Observation> updateResponse = await _client.ConditionalUpdateAsync(
                observation,
                $"identifier={Guid.NewGuid().ToString()}");

            Assert.Equal(HttpStatusCode.Created, updateResponse.StatusCode);

            Observation updatedResource = updateResponse.Resource;

            Assert.NotNull(updatedResource);
            Assert.NotNull(updatedResource.Id);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResource_WhenUpsertingConditionallyWithNoSearchCriteria_ThenAnErrorShouldBeReturned()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();

            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.ConditionalUpdateAsync(
                observation,
                string.Empty));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResourceAndProvenanceHeader_WhenUpsertingConditionallyWithNoIdAndNoExisting_TheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = null;

            using FhirResponse<Observation> updateResponse = await _client.ConditionalUpdateAsync(
                observation,
                $"identifier={Guid.NewGuid().ToString()}",
                provenanceHeader: Samples.GetProvenanceHeader());

            Assert.Equal(HttpStatusCode.Created, updateResponse.StatusCode);

            Observation updatedResource = updateResponse.Resource;

            Assert.NotNull(updatedResource);
            Assert.NotNull(updatedResource.Id);

            using var provenanceResponse = await _client.SearchAsync(ResourceType.Provenance, $"target={observation.Id}");
            Assert.Equal(HttpStatusCode.OK, provenanceResponse.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResourceAndMalformedProvenanceHeader_WhenUpsertingConditionallyWithNoSearchCriteria_ThenAnErrorShouldBeReturned()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();

            var weakETag = "W/\"Jibberish\"";
            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.ConditionalUpdateAsync(
                observation,
                null,
                weakETag));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResource_WhenUpsertingConditionallyWithAnIdAndNoExisting_TheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = Guid.NewGuid().ToString();

            using FhirResponse<Observation> updateResponse = await _client.ConditionalUpdateAsync(
                observation,
                $"identifier={Guid.NewGuid().ToString()}");

            Assert.Equal(HttpStatusCode.Created, updateResponse.StatusCode);

            Observation updatedResource = updateResponse.Resource;

            Assert.NotNull(updatedResource);
            Assert.Equal(observation.Id, updatedResource.Id);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResourceWithNoId_WhenUpsertingConditionallyWithOneMatch_TheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            var identifier = Guid.NewGuid().ToString();

            observation.Identifier.Add(new Identifier("http://e2etests", identifier));
            using FhirResponse<Observation> response = await _client.CreateAsync(observation);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var observation2 = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation2.Id = null;
            observation2.Identifier.Add(new Identifier("http://e2etests", identifier));
            observation2.Text.Div = "<div>Updated!</div>";
            using FhirResponse<Observation> updateResponse = await _client.ConditionalUpdateAsync(
                observation2,
                $"identifier={identifier}");

            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

            Observation updatedResource = updateResponse.Resource;

            Assert.NotNull(updatedResource);
            Assert.Equal(response.Resource.Id, updatedResource.Id);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResourceWithCorrectId_WhenUpsertingConditionallyWithOneMatch_TheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            var identifier = Guid.NewGuid().ToString();
            string updatedDiv = "<div xmlns=\"http://www.w3.org/1999/xhtml\">Updated!</div>";

            observation.Identifier.Add(new Identifier("http://e2etests", identifier));
            using FhirResponse<Observation> response = await _client.CreateAsync(observation);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var observation2 = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation2.Id = response.Resource.Id;
            observation2.Identifier.Add(new Identifier("http://e2etests", identifier));
            observation2.Text.Div = updatedDiv;
            using FhirResponse<Observation> updateResponse = await _client.ConditionalUpdateAsync(
                observation2,
                $"identifier={identifier}");

            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

            Observation updatedResource = updateResponse.Resource;

            Assert.NotNull(updatedResource);
            Assert.Equal(response.Resource.Id, updatedResource.Id);
            Assert.Equal(observation2.Text.Div, updatedResource.Text.Div);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResourceWithIncorrectId_WhenUpsertingConditionallyWithOneMatch_TheServerShouldFail()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            var identifier = Guid.NewGuid().ToString();

            observation.Identifier.Add(new Identifier("http://e2etests", identifier));
            using FhirResponse<Observation> response = await _client.CreateAsync(observation);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var observation2 = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation2.Id = Guid.NewGuid().ToString();

            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.ConditionalUpdateAsync(
                observation2,
                $"identifier={identifier}"));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResource_WhenUpsertingConditionallyWithMultipleMatches_TheServerShouldFail()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            var identifier = Guid.NewGuid().ToString();

            observation.Identifier.Add(new Identifier("http://e2etests", identifier));

            using FhirResponse<Observation> response = await _client.CreateAsync(observation);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            using FhirResponse<Observation> response2 = await _client.CreateAsync(observation);
            Assert.Equal(HttpStatusCode.Created, response2.StatusCode);

            var observation2 = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation2.Id = null;

            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.ConditionalUpdateAsync(
                observation2,
                $"identifier={identifier}"));

            Assert.Equal(HttpStatusCode.PreconditionFailed, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResource_WhenUpsertingConditionallyANewDuplicatedSearchParameterResource_TheServerShouldFail()
        {
            var id = Guid.NewGuid();
            var resourceToCreate = Samples.GetJsonSample<SearchParameter>("SearchParameterDuplicated");
            resourceToCreate.Id = id.ToString();

            using FhirClientException ex = await Assert.ThrowsAsync<FhirClientException>(() => _client.ConditionalUpdateAsync(
                resourceToCreate,
                $"SearchParameter/id={id}"));

            var operationOutcome = ex.OperationOutcome;
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, ex.StatusCode);
            Assert.NotNull(operationOutcome.Id);
            Assert.NotEmpty(operationOutcome.Issue);
            Assert.Contains("A search parameter with the same code value 'code' already exists for base type 'Observation'", operationOutcome.Issue[1].Diagnostics);
        }
    }
}
