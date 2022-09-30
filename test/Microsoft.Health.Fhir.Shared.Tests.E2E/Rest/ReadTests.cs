// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [Trait(Traits.Category, Categories.DomainLogicValidation)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class ReadTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;

        public ReadTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnId_WhenGettingAResource_TheServerShouldReturnTheAppropriateResourceSuccessfully()
        {
            Observation createdResource = await _client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            using FhirResponse<Observation> readResponse = await _client.ReadAsync<Observation>(ResourceType.Observation, createdResource.Id);

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
        public async Task GivenANonExistantId_WhenGettingAResource_TheServerShouldReturnANotFoundStatus()
        {
            using FhirException ex = await Assert.ThrowsAsync<FhirException>(
                () => _client.ReadAsync<Observation>(ResourceType.Observation, Guid.NewGuid().ToString()));

            Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenADeletedId_WhenGettingAResource_TheServerShouldReturnAGoneStatus()
        {
            Observation createdResource = await _client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            await _client.DeleteAsync(createdResource);

            using FhirException ex = await Assert.ThrowsAsync<FhirException>(
                () => _client.ReadAsync<Observation>(ResourceType.Observation, createdResource.Id));

            Assert.Equal(System.Net.HttpStatusCode.Gone, ex.StatusCode);
        }
    }
}
