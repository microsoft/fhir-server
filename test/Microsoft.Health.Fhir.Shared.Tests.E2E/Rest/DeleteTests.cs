// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
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
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class DeleteTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly HttpIntegrationTestFixture _fixture;
        private readonly TestFhirClient _client;

        public DeleteTests(HttpIntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResource_WhenDeleting_ThenServerShouldDeleteSuccessfully()
        {
            using FhirResponse<Observation> response = await _client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            string resourceId = response.Resource.Id;
            string versionId = response.Resource.Meta.VersionId;

            // Delete the resource.
            await _client.DeleteAsync(response.Resource);

            // Subsequent read on the resource should return Gone.
            using FhirException ex = await ExecuteAndValidateGoneStatus(() => _client.ReadAsync<Observation>(ResourceType.Observation, resourceId));

            string eTag = ex.Headers.ETag.ToString();

            Assert.NotNull(eTag);

            string deleteVersionId = WeakETag.FromWeakETag(eTag).VersionId;

            // Subsequent read on the specific version should still work.
            await _client.VReadAsync<Observation>(ResourceType.Observation, resourceId, versionId);

            // Subsequent read on the deleted version should return Gone.
            await ExecuteAndValidateGoneStatus(() => _client.VReadAsync<Observation>(ResourceType.Observation, resourceId, deleteVersionId));

            async Task<FhirException> ExecuteAndValidateGoneStatus(Func<Task> action)
            {
                using FhirException exception = await Assert.ThrowsAsync<FhirException>(action);

                Assert.Equal(HttpStatusCode.Gone, exception.StatusCode);

                return exception;
            }
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResource_WhenHardDeleting_ThenServerShouldDeleteAllRelatedResourcesSuccessfully()
        {
            List<string> versionIds = new List<string>();

            using FhirResponse<Observation> response1 = await _client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            Observation observation = response1.Resource;
            string resourceId = observation.Id;

            versionIds.Add(observation.Meta.VersionId);

            // Update the observation.
            observation.Status = ObservationStatus.EnteredInError;

            using FhirResponse<Observation> response2 = await _client.UpdateAsync(observation);

            versionIds.Add(response2.Resource.Meta.VersionId);

            // Delete the observation
            await _client.DeleteAsync(observation);

            // Update the observation to resurrect the resource.
            observation.Status = ObservationStatus.Final;
            observation.Subject = new ResourceReference("Patient/123");

            using FhirResponse<Observation> response3 = await _client.UpdateAsync(observation);

            versionIds.Add(response3.Resource.Meta.VersionId);

            // Hard-delete the resource.
            await _client.HardDeleteAsync(observation);

            // Getting the resource should result in NotFound.
            await ExecuteAndValidateNotFoundStatus(() => _client.ReadAsync<Observation>(ResourceType.Observation, resourceId));

            // Each version read should also result in NotFound.
            foreach (string versionId in versionIds)
            {
                await ExecuteAndValidateNotFoundStatus(() => _client.VReadAsync<Observation>(ResourceType.Observation, resourceId, versionId));
            }

            // History API should return NotFound.
            await ExecuteAndValidateNotFoundStatus(() => _client.SearchAsync($"Observation/{resourceId}/_history"));

            async Task ExecuteAndValidateNotFoundStatus(Func<Task> action)
            {
                using FhirException exception = await Assert.ThrowsAsync<FhirException>(action);

                Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
            }
        }

        [SkippableFact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResource_WhenPurging_ThenServerShouldDeleteHistoryAndKeepCurrentVersion()
        {
            Skip.IfNot(_fixture.TestFhirServer.Metadata.Predicate("CapabilityStatement.rest.operation.where(name='purge-history').exists()"), "$purge-history not enabled on this server");

            using FhirResponse<Observation> response = await _client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            string resourceId = response.Resource.Id;
            string historyVersion = response.Resource.Meta.VersionId;

            response.Resource.Meta.Tag.Add(new Coding("tests", Guid.NewGuid().ToString()));

            using FhirResponse<Observation> updateResponse = await _client.UpdateAsync(response.Resource);

            // Delete the resource.
            await _client.DeleteAsync($"{nameof(Observation)}/{resourceId}/$purge-history");

            // Current resource should be returned.
            var currentVersion = await _client.ReadAsync<Observation>(ResourceType.Observation, resourceId);
            Assert.Equal(updateResponse.Resource.Meta.VersionId, currentVersion.Resource.Meta.VersionId);

            // Subsequent read on the specific version should still work.
            await _client.VReadAsync<Observation>(ResourceType.Observation, resourceId, updateResponse.Resource.Meta.VersionId);

            // Subsequent read on the deleted version should return Gone.
            await GivenExecuteAndValidateNotFoundStatus(() => _client.VReadAsync<Observation>(ResourceType.Observation, resourceId, historyVersion));
        }

        [Fact]
        public async Task GivenAResourceWithLargeNumberOfHistory_WhenHardDeleting_ThenServerShouldDeleteAllRelatedResourcesSuccessfully()
        {
            List<string> versionIds = new List<string>();

            using FhirResponse<Observation> response = await _client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            Observation observation = response.Resource;
            string resourceId = observation.Id;

            versionIds.Add(observation.Meta.VersionId);

            // Update the observation.
            for (int i = 0; i < 50; i++)
            {
                using FhirResponse<Observation> loopResponse = await _client.UpdateAsync(observation);

                versionIds.Add(loopResponse.Resource.Meta.VersionId);
            }

            // Hard-delete the resource.
            await _client.HardDeleteAsync(observation);

            // Getting the resource should result in NotFound.
            await GivenExecuteAndValidateNotFoundStatus(() => _client.ReadAsync<Observation>(ResourceType.Observation, resourceId));

            // Each version read should also result in NotFound.
            foreach (string versionId in versionIds)
            {
                await GivenExecuteAndValidateNotFoundStatus(() => _client.VReadAsync<Observation>(ResourceType.Observation, resourceId, versionId));
            }

            // History API should return NotFound.
            await GivenExecuteAndValidateNotFoundStatus(() => _client.SearchAsync($"Observation/{resourceId}/_history"));
        }

        private async Task GivenExecuteAndValidateNotFoundStatus(Func<Task> action)
        {
            FhirException exception = null;

            do
            {
                if (exception?.StatusCode == (HttpStatusCode)429)
                {
                    // Wait for a little bit before retrying if we are geting throttled.
                    await Task.Delay(500);
                }

                exception = await Assert.ThrowsAsync<FhirException>(action);
            }
            while (exception.StatusCode == (HttpStatusCode)429);

            Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);

            exception.Dispose();
        }
    }
}
