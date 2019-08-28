// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;
using FhirClient = Microsoft.Health.Fhir.Tests.E2E.Common.FhirClient;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class VReadTests : IClassFixture<HttpIntegrationTestFixture>
    {
        public VReadTests(HttpIntegrationTestFixture fixture)
        {
            Client = fixture.FhirClient;
        }

        protected FhirClient Client { get; set; }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenGettingAResource_GivenAnIdAndVersionId_TheServerShouldReturnTheAppropriateResourceSuccessfully()
        {
            Observation createdResource = await Client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            FhirResponse<Observation> vReadResponse = await Client.VReadAsync<Observation>(
                ResourceType.Observation,
                createdResource.Id,
                createdResource.Meta.VersionId);

            Observation vReadResource = vReadResponse.Resource;

            Assert.Equal(createdResource.Id, vReadResource.Id);
            Assert.Equal(createdResource.Meta.VersionId, vReadResource.Meta.VersionId);
            Assert.Equal(createdResource.Meta.LastUpdated, vReadResource.Meta.LastUpdated);

            Assert.Contains(createdResource.Meta.VersionId, vReadResponse.Headers.ETag.Tag);

            TestHelper.AssertLastUpdatedAndLastModifiedAreEqual(createdResource.Meta.LastUpdated, vReadResponse.Content.Headers.LastModified);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenGettingAResource_GivenANonExistingIdAndVersionId_TheServerShouldReturnANotFoundStatus()
        {
            FhirException ex = await Assert.ThrowsAsync<FhirException>(
                () => Client.VReadAsync<Observation>(ResourceType.Observation, Guid.NewGuid().ToString(), Guid.NewGuid().ToString()));

            Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenGettingAResource_GivenAIdAndNonExistingVersionId_TheServerShouldReturnANotFoundStatus()
        {
            Observation createdResource = await Client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            FhirException ex = await Assert.ThrowsAsync<FhirException>(
                () => Client.VReadAsync<Observation>(ResourceType.Observation, createdResource.Id, Guid.NewGuid().ToString()));

            Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenGettingAResource_GivenADeletedIdAndVersionId_TheServerShouldReturnAGoneStatus()
        {
            Observation createdResource = await Client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            FhirResponse deleteResponse = await Client.DeleteAsync(createdResource);

            var deletedVersion = WeakETag.FromWeakETag(deleteResponse.Headers.ETag.ToString()).VersionId;

            FhirException ex = await Assert.ThrowsAsync<FhirException>(
                () => Client.VReadAsync<Observation>(ResourceType.Observation, createdResource.Id, deletedVersion));

            Assert.Equal(System.Net.HttpStatusCode.Gone, ex.StatusCode);
        }
    }
}
