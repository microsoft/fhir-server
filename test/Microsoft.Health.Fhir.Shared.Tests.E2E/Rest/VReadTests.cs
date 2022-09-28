// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class VReadTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;

        public VReadTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnIdAndVersionId_WhenGettingAResource_TheServerShouldReturnTheAppropriateResourceSuccessfully()
        {
            Observation createdResource = await _client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            using FhirResponse<Observation> vReadResponse = await _client.VReadAsync<Observation>(
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
        public async Task GivenANonExistingIdAndVersionId_WhenGettingAResource_TheServerShouldReturnANotFoundStatus()
        {
            using FhirException ex = await Assert.ThrowsAsync<FhirException>(
                () => _client.VReadAsync<Observation>(ResourceType.Observation, Guid.NewGuid().ToString(), Guid.NewGuid().ToString()));

            Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAIdAndNonExistingVersionId_WhenGettingAResource_TheServerShouldReturnANotFoundStatus()
        {
            Observation createdResource = await _client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            using FhirException ex = await Assert.ThrowsAsync<FhirException>(
                () => _client.VReadAsync<Observation>(ResourceType.Observation, createdResource.Id, Guid.NewGuid().ToString()));

            Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenADeletedIdAndVersionId_WhenGettingAResource_TheServerShouldReturnAGoneStatus()
        {
            Observation createdResource = await _client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            using FhirResponse deleteResponse = await _client.DeleteAsync(createdResource);

            var deletedVersion = WeakETag.FromWeakETag(deleteResponse.Headers.ETag.ToString()).VersionId;

            using FhirException ex = await Assert.ThrowsAsync<FhirException>(
                () => _client.VReadAsync<Observation>(ResourceType.Observation, createdResource.Id, deletedVersion));

            Assert.Equal(System.Net.HttpStatusCode.Gone, ex.StatusCode);
        }
    }
}
