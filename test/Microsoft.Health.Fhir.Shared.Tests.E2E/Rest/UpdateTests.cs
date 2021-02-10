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
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public partial class UpdateTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;

        public UpdateTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenTheResource_WhenUpdatingAnExistingResource_TheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            Observation createdResource = await _client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            using FhirResponse<Observation> updateResponse = await _client.UpdateAsync(createdResource);

            Assert.Equal(System.Net.HttpStatusCode.OK, updateResponse.StatusCode);

            Observation updatedResource = updateResponse.Resource;

            Assert.NotNull(updatedResource);
            Assert.Equal(createdResource.Id, updatedResource.Id);
            Assert.NotEqual(createdResource.Meta.VersionId, updatedResource.Meta.VersionId);
            Assert.NotEqual(createdResource.Meta.LastUpdated, updatedResource.Meta.LastUpdated);

            Assert.Contains(updatedResource.Meta.VersionId, updateResponse.Headers.ETag.Tag);
            TestHelper.AssertLastUpdatedAndLastModifiedAreEqual(updatedResource.Meta.LastUpdated, updateResponse.Content.Headers.LastModified);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenTheResourceAndMalformedHeader_WhenUpdatingAnExistingResource_ThenAnErrorShouldBeReturned()
        {
            Observation createdResource = await _client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.UpdateAsync(
                createdResource,
                null,
                "Jibberish"));

            Assert.Equal(HttpStatusCode.BadRequest, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResourceAndMalformedProvenanceHeader_WhenPostingToHttp_TheServerShouldRespondSuccessfully()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = null;
            var exception = await Assert.ThrowsAsync<FhirException>(() => _client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>(), $"identifier={Guid.NewGuid().ToString()}", "Jibberish"));
            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenTheResource_WhenUpdatingANewResource_TheServerShouldReturnTheNewResourceSuccessfully()
        {
            var resourceToCreate = Samples.GetDefaultObservation().ToPoco<Observation>();
            resourceToCreate.Id = Guid.NewGuid().ToString();

            using FhirResponse<Observation> createResponse = await _client.UpdateAsync(resourceToCreate);

            Assert.Equal(System.Net.HttpStatusCode.Created, createResponse.StatusCode);

            Observation createdResource = createResponse.Resource;

            Assert.NotNull(createdResource);
            Assert.Equal(resourceToCreate.Id, createdResource.Id);
            Assert.NotNull(createdResource.Meta.VersionId);
            Assert.NotNull(createdResource.Meta.LastUpdated);
            Assert.NotNull(createResponse.Headers.ETag);
            Assert.NotNull(createResponse.Headers.Location);
            Assert.NotNull(createResponse.Content.Headers.LastModified);

            Assert.Contains(createdResource.Meta.VersionId, createResponse.Headers.ETag.Tag);
            TestHelper.AssertLocationHeaderIsCorrect(_client, createdResource, createResponse.Headers.Location);
            TestHelper.AssertLastUpdatedAndLastModifiedAreEqual(createdResource.Meta.LastUpdated, createResponse.Content.Headers.LastModified);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenTheResourceWithMetaSet_WhenUpdatingANewResource_TheServerShouldReturnTheNewResourceSuccessfully()
        {
            var resourceToCreate = Samples.GetDefaultObservation().ToPoco<Observation>();
            resourceToCreate.Id = Guid.NewGuid().ToString();
            resourceToCreate.Meta = new Meta
            {
                VersionId = Guid.NewGuid().ToString(),
                LastUpdated = DateTimeOffset.UtcNow.AddMilliseconds(-1),
            };

            using FhirResponse<Observation> createResponse = await _client.UpdateAsync(resourceToCreate);

            Assert.Equal(System.Net.HttpStatusCode.Created, createResponse.StatusCode);

            Observation createdResource = createResponse.Resource;

            Assert.NotNull(createdResource);
            Assert.Equal(resourceToCreate.Id, createdResource.Id);
            Assert.NotEqual(resourceToCreate.Meta.VersionId, createdResource.Meta.VersionId);
            Assert.NotEqual(resourceToCreate.Meta.LastUpdated, createdResource.Meta.LastUpdated);
            Assert.NotNull(createResponse.Headers.ETag);
            Assert.NotNull(createResponse.Headers.Location);
            Assert.NotNull(createResponse.Content.Headers.LastModified);

            Assert.Contains(createdResource.Meta.VersionId, createResponse.Headers.ETag.Tag);
            TestHelper.AssertLocationHeaderIsCorrect(_client, createdResource, createResponse.Headers.Location);
            TestHelper.AssertLastUpdatedAndLastModifiedAreEqual(createdResource.Meta.LastUpdated, createResponse.Content.Headers.LastModified);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAMismatchedId_WhenUpdatingAResource_TheServerShouldReturnABadRequestResponse()
        {
            Observation createdResource = await _client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            using FhirException ex = await Assert.ThrowsAsync<FhirException>(
                () => _client.UpdateAsync($"Observation/{Guid.NewGuid()}", createdResource));

            Assert.Equal(System.Net.HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResourceWithNoId_WhenUpdatingAResource_TheServerShouldReturnABadRequestResponse()
        {
            var resourceToCreate = Samples.GetDefaultObservation().ToPoco<Observation>();

            using FhirException ex = await Assert.ThrowsAsync<FhirException>(
                () => _client.UpdateAsync($"Observation/{Guid.NewGuid()}", resourceToCreate));

            Assert.Equal(System.Net.HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnETagHeader_WhenUpdatingAResource_TheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            Observation createdResource = await _client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            using FhirResponse<Observation> updateResponse = await _client.UpdateAsync(createdResource, createdResource.Meta.VersionId);

            Assert.Equal(System.Net.HttpStatusCode.OK, updateResponse.StatusCode);

            Observation updatedResource = updateResponse.Resource;

            Assert.NotNull(updatedResource);
            Assert.Equal(createdResource.Id, updatedResource.Id);
            Assert.NotEqual(createdResource.Meta.VersionId, updatedResource.Meta.VersionId);
            Assert.NotEqual(createdResource.Meta.LastUpdated, updatedResource.Meta.LastUpdated);

            Assert.Contains(updatedResource.Meta.VersionId, updateResponse.Headers.ETag.Tag);
            TestHelper.AssertLastUpdatedAndLastModifiedAreEqual(updatedResource.Meta.LastUpdated, updateResponse.Content.Headers.LastModified);
        }
    }
}
