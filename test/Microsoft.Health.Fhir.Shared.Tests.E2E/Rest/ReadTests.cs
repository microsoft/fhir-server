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
    public class ReadTests : IClassFixture<HttpIntegrationTestFixture>
    {
        public ReadTests(HttpIntegrationTestFixture fixture)
        {
            Client = fixture.FhirClient;
        }

        protected FhirClient Client { get; set; }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenGettingAResource_GivenAnId_TheServerShouldReturnTheAppropriateResourceSuccessfully()
        {
            Observation createdResource = await Client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            FhirResponse<Observation> readResponse = await Client.ReadAsync<Observation>(ResourceType.Observation, createdResource.Id);

            Observation readResource = readResponse.Resource;

            Assert.Equal(createdResource.Id, readResource.Id);
            Assert.Equal(createdResource.Meta.VersionId, readResource.Meta.VersionId);
            Assert.Equal(createdResource.Meta.LastUpdated, readResource.Meta.LastUpdated);

            Assert.Contains(createdResource.Meta.VersionId, readResponse.Headers.ETag.Tag);

            TestHelper.AssertLastUpdatedAndLastModifiedAreEqual(createdResource.Meta.LastUpdated, readResponse.Content.Headers.LastModified);
            TestHelper.AssertSecurityHeaders(readResponse.Headers);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenGettingAResource_GivenANonExistantId_TheServerShouldReturnANotFoundStatus()
        {
            FhirException ex = await Assert.ThrowsAsync<FhirException>(
                () => Client.ReadAsync<Observation>(ResourceType.Observation, Guid.NewGuid().ToString()));

            Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenGettingAResource_GivenADeletedId_TheServerShouldReturnAGoneStatus()
        {
            Observation createdResource = await Client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            await Client.DeleteAsync(createdResource);

            FhirException ex = await Assert.ThrowsAsync<FhirException>(
                () => Client.ReadAsync<Observation>(ResourceType.Observation, createdResource.Id));

            Assert.Equal(System.Net.HttpStatusCode.Gone, ex.StatusCode);
        }
    }
}
