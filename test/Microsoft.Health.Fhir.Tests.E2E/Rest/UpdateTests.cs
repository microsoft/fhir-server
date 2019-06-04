// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All, FhirVersion.All)]
    public class UpdateTests : IClassFixture<HttpIntegrationTestFixture>
    {
        public UpdateTests(HttpIntegrationTestFixture fixture)
        {
            Client = fixture.FhirClient;
        }

        protected IVersionSpecificFhirClient Client { get; set; }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenUpdatingAnExistingResource_GivenTheResource_TheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            ResourceElement createdResource = await Client.CreateAsync(Client.GetDefaultObservation());

            FhirResponse<ResourceElement> updateResponse = await Client.UpdateAsync(createdResource);

            Assert.Equal(System.Net.HttpStatusCode.OK, updateResponse.StatusCode);

            ResourceElement updatedResource = updateResponse.Resource;

            Assert.NotNull(updatedResource);
            Assert.Equal(createdResource.Id, updatedResource.Id);
            Assert.NotEqual(createdResource.VersionId, updatedResource.VersionId);
            Assert.NotEqual(createdResource.LastUpdated, updatedResource.LastUpdated);

            Assert.Contains(updatedResource.VersionId, updateResponse.Headers.ETag.Tag);
            TestHelper.AssertLastUpdatedAndLastModifiedAreEqual(updatedResource.LastUpdated, updateResponse.Content.Headers.LastModified);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenUpdatingANewResource_GivenTheResource_TheServerShouldReturnTheNewResourceSuccessfully()
        {
            var resourceToCreate = Client.GetDefaultObservation();
            resourceToCreate = Client.UpdateId(resourceToCreate, Guid.NewGuid().ToString());

            FhirResponse<ResourceElement> createResponse = await Client.UpdateAsync(resourceToCreate);

            Assert.Equal(System.Net.HttpStatusCode.Created, createResponse.StatusCode);

            ResourceElement createdResource = createResponse.Resource;

            Assert.NotNull(createdResource);
            Assert.Equal(resourceToCreate.Id, createdResource.Id);
            Assert.NotNull(createdResource.VersionId);
            Assert.NotNull(createdResource.LastUpdated);
            Assert.NotNull(createResponse.Headers.ETag);
            Assert.NotNull(createResponse.Headers.Location);
            Assert.NotNull(createResponse.Content.Headers.LastModified);

            Assert.Contains(createdResource.VersionId, createResponse.Headers.ETag.Tag);
            TestHelper.AssertLocationHeaderIsCorrect(Client, createdResource, createResponse.Headers.Location);
            TestHelper.AssertLastUpdatedAndLastModifiedAreEqual(createdResource.LastUpdated, createResponse.Content.Headers.LastModified);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenUpdatingANewResource_GivenTheResourceWithMetaSet_TheServerShouldReturnTheNewResourceSuccessfully()
        {
            var resourceToCreate = Client.GetDefaultObservation();

            resourceToCreate = Client.UpdateId(resourceToCreate, Guid.NewGuid().ToString());
            resourceToCreate = Client.UpdateVersion(resourceToCreate, Guid.NewGuid().ToString());
            resourceToCreate = Client.UpdateLastUpdated(resourceToCreate, DateTimeOffset.UtcNow.AddMilliseconds(-1));

            FhirResponse<ResourceElement> createResponse = await Client.UpdateAsync(resourceToCreate);

            Assert.Equal(System.Net.HttpStatusCode.Created, createResponse.StatusCode);

            ResourceElement createdResource = createResponse.Resource;

            Assert.NotNull(createdResource);
            Assert.Equal(resourceToCreate.Id, createdResource.Id);
            Assert.NotEqual(resourceToCreate.VersionId, createdResource.VersionId);
            Assert.NotEqual(resourceToCreate.LastUpdated, createdResource.LastUpdated);
            Assert.NotNull(createResponse.Headers.ETag);
            Assert.NotNull(createResponse.Headers.Location);
            Assert.NotNull(createResponse.Content.Headers.LastModified);

            Assert.Contains(createdResource.VersionId, createResponse.Headers.ETag.Tag);
            TestHelper.AssertLocationHeaderIsCorrect(Client, createdResource, createResponse.Headers.Location);
            TestHelper.AssertLastUpdatedAndLastModifiedAreEqual(createdResource.LastUpdated, createResponse.Content.Headers.LastModified);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenUpdatingAResource_GivenAMismatchedId_TheServerShouldReturnABadRequestResponse()
        {
            ResourceElement createdResource = await Client.CreateAsync(Client.GetDefaultObservation());

            FhirException ex = await Assert.ThrowsAsync<FhirException>(
                () => Client.UpdateAsync($"Observation/{Guid.NewGuid()}", createdResource));

            Assert.Equal(System.Net.HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenUpdatingAResource_GivenAResourceWithNoId_TheServerShouldReturnABadRequestResponse()
        {
            var resourceToCreate = Client.GetDefaultObservation();

            FhirException ex = await Assert.ThrowsAsync<FhirException>(
                () => Client.UpdateAsync($"Observation/{Guid.NewGuid()}", resourceToCreate));

            Assert.Equal(System.Net.HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenUpdatingAResource_GivenAnETagHeader_TheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            ResourceElement createdResource = await Client.CreateAsync(Client.GetDefaultObservation());

            FhirResponse<ResourceElement> updateResponse = await Client.UpdateAsync(createdResource);

            Assert.Equal(System.Net.HttpStatusCode.OK, updateResponse.StatusCode);

            ResourceElement updatedResource = updateResponse.Resource;

            Assert.NotNull(updatedResource);
            Assert.Equal(createdResource.Id, updatedResource.Id);
            Assert.NotEqual(createdResource.VersionId, updatedResource.VersionId);
            Assert.NotEqual(createdResource.LastUpdated, updatedResource.LastUpdated);

            Assert.Contains(updatedResource.VersionId, updateResponse.Headers.ETag.Tag);
            TestHelper.AssertLastUpdatedAndLastModifiedAreEqual(updatedResource.LastUpdated, updateResponse.Content.Headers.LastModified);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenUpdatingAResource_GivenAnIncorrectETagHeader_TheServerShouldReturnAConflictResponse()
        {
            ResourceElement createdResource = await Client.CreateAsync(Client.GetDefaultObservation());

            FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.UpdateAsync(createdResource, Guid.NewGuid().ToString()));

            Assert.Equal(System.Net.HttpStatusCode.Conflict, ex.StatusCode);
        }
    }
}
