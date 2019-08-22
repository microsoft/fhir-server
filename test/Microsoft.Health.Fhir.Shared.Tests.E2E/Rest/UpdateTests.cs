// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;
using FhirClient = Microsoft.Health.Fhir.Tests.E2E.Common.FhirClient;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class UpdateTests : IClassFixture<HttpIntegrationTestFixture>
    {
        public UpdateTests(HttpIntegrationTestFixture fixture)
        {
            Client = fixture.FhirClient;
        }

        protected FhirClient Client { get; set; }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenUpdatingAnExistingResource_GivenTheResource_TheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            Observation createdResource = await Client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            FhirResponse<Observation> updateResponse = await Client.UpdateAsync(createdResource);

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
        public async Task WhenUpdatingANewResource_GivenTheResource_TheServerShouldReturnTheNewResourceSuccessfully()
        {
            var resourceToCreate = Samples.GetDefaultObservation().ToPoco<Observation>();
            resourceToCreate.Id = Guid.NewGuid().ToString();

            FhirResponse<Observation> createResponse = await Client.UpdateAsync(resourceToCreate);

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
            TestHelper.AssertLocationHeaderIsCorrect(Client, createdResource, createResponse.Headers.Location);
            TestHelper.AssertLastUpdatedAndLastModifiedAreEqual(createdResource.Meta.LastUpdated, createResponse.Content.Headers.LastModified);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenUpdatingANewResource_GivenTheResourceWithMetaSet_TheServerShouldReturnTheNewResourceSuccessfully()
        {
            var resourceToCreate = Samples.GetDefaultObservation().ToPoco<Observation>();
            resourceToCreate.Id = Guid.NewGuid().ToString();
            resourceToCreate.Meta = new Meta
            {
                VersionId = Guid.NewGuid().ToString(),
                LastUpdated = DateTimeOffset.UtcNow.AddMilliseconds(-1),
            };

            FhirResponse<Observation> createResponse = await Client.UpdateAsync(resourceToCreate);

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
            TestHelper.AssertLocationHeaderIsCorrect(Client, createdResource, createResponse.Headers.Location);
            TestHelper.AssertLastUpdatedAndLastModifiedAreEqual(createdResource.Meta.LastUpdated, createResponse.Content.Headers.LastModified);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenUpdatingAResource_GivenAMismatchedId_TheServerShouldReturnABadRequestResponse()
        {
            Observation createdResource = await Client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            FhirException ex = await Assert.ThrowsAsync<FhirException>(
                () => Client.UpdateAsync($"Observation/{Guid.NewGuid()}", createdResource));

            Assert.Equal(System.Net.HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenUpdatingAResource_GivenAResourceWithNoId_TheServerShouldReturnABadRequestResponse()
        {
            var resourceToCreate = Samples.GetDefaultObservation().ToPoco<Observation>();

            FhirException ex = await Assert.ThrowsAsync<FhirException>(
                () => Client.UpdateAsync($"Observation/{Guid.NewGuid()}", resourceToCreate));

            Assert.Equal(System.Net.HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenUpdatingAResource_GivenAnETagHeader_TheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            Observation createdResource = await Client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            FhirResponse<Observation> updateResponse = await Client.UpdateAsync(createdResource);

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
        public async Task WhenUpdatingAResource_GivenAnIncorrectETagHeader_TheServerShouldReturnAConflictResponse()
        {
            Observation createdResource = await Client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.UpdateAsync(createdResource, Guid.NewGuid().ToString()));

            Assert.Equal(System.Net.HttpStatusCode.Conflict, ex.StatusCode);
        }
    }
}
