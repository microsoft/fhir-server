// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
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
        public async Task GivenAnExistingResource_WhenUpsertingConditionallyWithItsIdAndDefaultNoMatchBehavior_TheServerShouldOverwriteTheResource()
        {
            Observation existing = await CreateObservationAsync();

            var observation2 = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation2.Id = existing.Id;
            observation2.Text.Div = "<div xmlns=\"http://www.w3.org/1999/xhtml\">Overwritten</div>";

            // Default (legacy) behavior: without the x-ms-conditional-update-no-match header, a no-match conditional update with an id upserts by that id.
            using FhirResponse<Observation> updateResponse = await _client.ConditionalUpdateAsync(
                observation2,
                $"_id={existing.Id}&_lastUpdated=le2000-01-01");

            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            Assert.Equal("2", updateResponse.Resource.Meta.VersionId);
            Assert.Equal(observation2.Text.Div, updateResponse.Resource.Text.Div);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnExistingResource_WhenUpsertingConditionallyWithItsIdAndNoMatchBehaviorCreate_TheServerShouldReturnConflictAndNotOverwrite()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            using FhirResponse<Observation> createResponse = await _client.CreateAsync(observation);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            Observation existing = createResponse.Resource;

            var observation2 = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation2.Id = existing.Id;
            observation2.Text.Div = "<div xmlns=\"http://www.w3.org/1999/xhtml\">Should not be saved</div>";

            var exception = await Assert.ThrowsAsync<FhirClientException>(() => NoMatchClient("create").ConditionalUpdateAsync(
                observation2,
                $"_id={existing.Id}&_lastUpdated=le2000-01-01"));

            Assert.Equal(HttpStatusCode.Conflict, exception.Response.StatusCode);

            using FhirResponse<Observation> readResponse = await _client.ReadAsync<Observation>(ResourceType.Observation, existing.Id);
            Assert.Equal(existing.Meta.VersionId, readResponse.Resource.Meta.VersionId);
            Assert.Equal(existing.Text.Div, readResponse.Resource.Text.Div);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnExistingResource_WhenUpsertingConditionallyWithAnInvalidNoMatchBehavior_TheServerShouldReturnBadRequestAndNotOverwrite()
        {
            Observation existing = await CreateObservationAsync();

            var observation2 = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation2.Id = existing.Id;
            observation2.Text.Div = "<div xmlns=\"http://www.w3.org/1999/xhtml\">Should not be saved</div>";

            // An unrecognized value must not silently fall back to the overwriting default.
            using FhirClientException exception = await Assert.ThrowsAsync<FhirClientException>(() => NoMatchClient("true").ConditionalUpdateAsync(
                observation2,
                $"_id={existing.Id}&_lastUpdated=le2000-01-01"));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
            await AssertUnchangedAsync(existing);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenADeletedResource_WhenUpsertingConditionallyWithItsIdAndNoMatchBehaviorCreate_TheServerShouldRecreateTheResource()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            using FhirResponse<Observation> createResponse = await _client.CreateAsync(observation);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            await _client.DeleteAsync(createResponse.Resource);

            var observation2 = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation2.Id = createResponse.Resource.Id;
            observation2.Text.Div = "<div xmlns=\"http://www.w3.org/1999/xhtml\">Recreated</div>";

            using FhirResponse<Observation> updateResponse = await NoMatchClient("create").ConditionalUpdateAsync(
                observation2,
                $"identifier={Guid.NewGuid()}");

            // A deleted id may be reused. It's a new version of the existing history (create, delete, recreate), so the outcome is an update.
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            Assert.Equal(observation2.Id, updateResponse.Resource.Id);

            using FhirResponse<Observation> readResponse = await _client.ReadAsync<Observation>(ResourceType.Observation, observation2.Id);
            Assert.Equal(observation2.Text.Div, readResponse.Resource.Text.Div);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnExistingResource_WhenUpsertingConditionallyWithItsIdAndALastUpdatedFilterThatMatches_TheServerShouldUpdateTheResource()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            using FhirResponse<Observation> createResponse = await _client.CreateAsync(observation);
            Observation existing = createResponse.Resource;

            var observation2 = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation2.Id = existing.Id;
            observation2.Text.Div = "<div xmlns=\"http://www.w3.org/1999/xhtml\">Updated!</div>";
            string cutoff = existing.Meta.LastUpdated.Value.AddDays(1).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

            using FhirResponse<Observation> updateResponse = await _client.ConditionalUpdateAsync(
                observation2,
                $"_id={existing.Id}&_lastUpdated=le{cutoff}");

            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            Assert.Equal(existing.Id, updateResponse.Resource.Id);
            Assert.Equal("2", updateResponse.Resource.Meta.VersionId);
            Assert.Equal(observation2.Text.Div, updateResponse.Resource.Text.Div);
        }

        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        [InlineData(FhirBundleProcessingLogic.Parallel)]
        [InlineData(FhirBundleProcessingLogic.Sequential)]
        public async Task GivenABatchWithAConditionalUpdateThatMatchesNothingForAnExistingIdAndNoMatchBehaviorCreate_WhenPosted_ThenOnlyThatEntryConflicts(FhirBundleProcessingLogic processingLogic)
        {
            Observation existing = await CreateObservationAsync();
            string newId = Guid.NewGuid().ToString();
            string conditionalNewId = Guid.NewGuid().ToString();

            var bundle = new Bundle
            {
                Type = Bundle.BundleType.Batch,
                Entry = new List<Bundle.EntryComponent>
                {
                    CreatePutEntry(existing.Id, $"Observation?_id={existing.Id}&_lastUpdated=le2000-01-01", "Should not be saved"),
                    CreatePutEntry(newId, $"Observation/{newId}", "Plain upsert"),
                    CreatePutEntry(conditionalNewId, $"Observation?identifier={Guid.NewGuid()}", "Conditional create"),
                },
            };

            using FhirResponse<Bundle> response = await NoMatchClient("create").PostBundleAsync(bundle, new FhirBundleOptions { BundleProcessingLogic = processingLogic });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("409", response.Resource.Entry[0].Response.Status);
            Assert.Equal("201", response.Resource.Entry[1].Response.Status);
            Assert.Equal("201", response.Resource.Entry[2].Response.Status);

            await AssertUnchangedAsync(existing);
            using FhirResponse<Observation> newRead = await _client.ReadAsync<Observation>(ResourceType.Observation, newId);
            Assert.Equal("1", newRead.Resource.Meta.VersionId);
            using FhirResponse<Observation> conditionalNewRead = await _client.ReadAsync<Observation>(ResourceType.Observation, conditionalNewId);
            Assert.Equal("1", conditionalNewRead.Resource.Meta.VersionId);
        }

        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        [InlineData(FhirBundleProcessingLogic.Parallel)]
        [InlineData(FhirBundleProcessingLogic.Sequential)]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenATransactionWithAConditionalUpdateThatMatchesNothingForAnExistingIdAndNoMatchBehaviorCreate_WhenPosted_ThenTheTransactionFailsAndNothingIsPersisted(FhirBundleProcessingLogic processingLogic)
        {
            Observation existing = await CreateObservationAsync();
            string newId = Guid.NewGuid().ToString();

            var bundle = new Bundle
            {
                Type = Bundle.BundleType.Transaction,
                Entry = new List<Bundle.EntryComponent>
                {
                    CreatePutEntry(newId, $"Observation/{newId}", "Should be rolled back"),
                    CreatePutEntry(existing.Id, $"Observation?_id={existing.Id}&_lastUpdated=le2000-01-01", "Should not be saved"),
                },
            };

            using FhirClientException exception = await Assert.ThrowsAsync<FhirClientException>(
                () => NoMatchClient("create").PostBundleAsync(bundle, new FhirBundleOptions { BundleProcessingLogic = processingLogic }));

            Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
            await AssertUnchangedAsync(existing);
            using FhirClientException readException = await Assert.ThrowsAsync<FhirClientException>(
                () => _client.ReadAsync<Observation>(ResourceType.Observation, newId));
            Assert.Equal(HttpStatusCode.NotFound, readException.StatusCode);
        }

        private TestFhirClient NoMatchClient(string noMatchBehavior)
        {
            // A clone has its own HttpClient, so the header doesn't leak into the shared fixture client.
            TestFhirClient client = _client.Clone();
            client.HttpClient.DefaultRequestHeaders.Add(KnownHeaders.ConditionalUpdateNoMatch, noMatchBehavior);
            return client;
        }

        private async System.Threading.Tasks.Task<Observation> CreateObservationAsync()
        {
            using FhirResponse<Observation> createResponse = await _client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            return createResponse.Resource;
        }

        private async Task AssertUnchangedAsync(Observation expected)
        {
            using FhirResponse<Observation> readResponse = await _client.ReadAsync<Observation>(ResourceType.Observation, expected.Id);
            Assert.Equal(expected.Meta.VersionId, readResponse.Resource.Meta.VersionId);
            Assert.Equal(expected.Text.Div, readResponse.Resource.Text.Div);
        }

        private static Bundle.EntryComponent CreatePutEntry(string id, string url, string narrative)
        {
            Observation observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = id;
            observation.Text.Div = $"<div xmlns=\"http://www.w3.org/1999/xhtml\">{narrative}</div>";

            return new Bundle.EntryComponent
            {
                Resource = observation,
                Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.PUT, Url = url },
            };
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
    }
}
