// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All, FhirVersion.All)]
    public class VReadTests : IClassFixture<HttpIntegrationTestFixture>
    {
        public VReadTests(HttpIntegrationTestFixture fixture)
        {
            Client = fixture.FhirClient;
        }

        protected ICustomFhirClient Client { get; set; }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenGettingAResource_GivenAnIdAndVersionId_TheServerShouldReturnTheAppropriateResourceSuccessfully()
        {
            FhirResponse<ResourceElement> createdResponse = await Client.CreateAsync(Client.GetDefaultObservation());

            var createdResource = createdResponse.Resource;

            FhirResponse<ResourceElement> vReadResponse = await Client.VReadAsync(
                createdResource.InstanceType,
                createdResource.Id,
                createdResource.VersionId);

            ResourceElement vReadResource = vReadResponse.Resource;

            Assert.Equal(createdResource.Id, vReadResource.Id);
            Assert.Equal(createdResource.VersionId, vReadResource.VersionId);
            Assert.Equal(createdResource.LastUpdated, vReadResource.LastUpdated);

            Assert.Contains(createdResource.VersionId, vReadResponse.Headers.ETag.Tag);

            TestHelper.AssertLastUpdatedAndLastModifiedAreEqual(createdResource.LastUpdated, vReadResponse.Content.Headers.LastModified);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenGettingAResource_GivenANonExistingIdAndVersionId_TheServerShouldReturnANotFoundStatus()
        {
            FhirException ex = await Assert.ThrowsAsync<FhirException>(
                () => Client.VReadAsync(KnownResourceTypes.Observation, Guid.NewGuid().ToString(), Guid.NewGuid().ToString()));

            Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenGettingAResource_GivenAIdAndNonExistingVersionId_TheServerShouldReturnANotFoundStatus()
        {
            FhirResponse<ResourceElement> createdResponse = await Client.CreateAsync(Client.GetDefaultObservation());
            var createdResource = createdResponse.Resource;

            FhirException ex = await Assert.ThrowsAsync<FhirException>(
                () => Client.VReadAsync(KnownResourceTypes.Observation, createdResource.Id, Guid.NewGuid().ToString()));

            Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenGettingAResource_GivenADeletedIdAndVersionId_TheServerShouldReturnAGoneStatus()
        {
            FhirResponse<ResourceElement> createdResponse = await Client.CreateAsync(Client.GetDefaultObservation());
            var createdResource = createdResponse.Resource;

            FhirResponse deleteResponse = await Client.DeleteAsync(createdResource);

            var deletedVersion = WeakETag.FromWeakETag(deleteResponse.Headers.ETag.ToString()).VersionId;

            FhirException ex = await Assert.ThrowsAsync<FhirException>(
                () => Client.VReadAsync(KnownResourceTypes.Observation, createdResource.Id, deletedVersion));

            Assert.Equal(System.Net.HttpStatusCode.Gone, ex.StatusCode);
        }
    }
}
