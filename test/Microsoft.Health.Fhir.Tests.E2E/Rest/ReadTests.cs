// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All, FhirVersion.All)]
    public class ReadTests : IClassFixture<HttpIntegrationTestFixture>
    {
        public ReadTests(HttpIntegrationTestFixture fixture)
        {
            Client = fixture.FhirClient;
        }

        protected ICustomFhirClient Client { get; set; }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenGettingAResource_GivenAnId_TheServerShouldReturnTheAppropriateResourceSuccessfully()
        {
            FhirResponse<ResourceElement> createdResponse = await Client.CreateAsync(Client.GetDefaultObservation());
            var createdResource = createdResponse.Resource;

            FhirResponse<ResourceElement> readResponse = await Client.ReadAsync(createdResource.InstanceType, createdResource.Id);

            var readResource = readResponse.Resource;

            Assert.Equal(createdResource.Id, readResource.Id);
            Assert.Equal(createdResource.VersionId, readResource.VersionId);
            Assert.Equal(createdResource.LastUpdated, readResource.LastUpdated);

            Assert.Contains(createdResource.VersionId, readResponse.Headers.ETag.Tag);

            TestHelper.AssertLastUpdatedAndLastModifiedAreEqual(createdResource.LastUpdated, readResponse.Content.Headers.LastModified);
            TestHelper.AssertSecurityHeaders(readResponse.Headers);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenGettingAResource_GivenANonExistantId_TheServerShouldReturnANotFoundStatus()
        {
            FhirException ex = await Assert.ThrowsAsync<FhirException>(
                () => Client.ReadAsync(KnownResourceTypes.Observation, Guid.NewGuid().ToString()));

            Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenGettingAResource_GivenADeletedId_TheServerShouldReturnAGoneStatus()
        {
            FhirResponse<ResourceElement> createdResponse = await Client.CreateAsync(Client.GetDefaultObservation());
            var createdResource = createdResponse.Resource;

            await Client.DeleteAsync(createdResource);

            FhirException ex = await Assert.ThrowsAsync<FhirException>(
                () => Client.ReadAsync(KnownResourceTypes.Observation, createdResource.Id));

            Assert.Equal(System.Net.HttpStatusCode.Gone, ex.StatusCode);
        }
    }
}
