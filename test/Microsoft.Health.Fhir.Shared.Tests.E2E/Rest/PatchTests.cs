// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
    public partial class PatchTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;

        public PatchTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenTheResource_WhenPatchingAnExistingResource_TheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            Observation createdResource = await _client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            using FhirResponse<Observation> updateResponse = await _client.PatchAsync<Observation>($"{createdResource.ResourceType}/{createdResource.Id}", new Parameters());

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
        public async Task GivenAnETagHeader_WhenPatchingAResource_TheServerShouldReturnTheUpdatedResourceSuccessfully()
        {
            Observation createdResource = await _client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            using FhirResponse<Observation> updateResponse = await _client.PatchAsync<Observation>($"{createdResource.ResourceType}/{createdResource.Id}", new Parameters(), createdResource.Meta.VersionId);

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
