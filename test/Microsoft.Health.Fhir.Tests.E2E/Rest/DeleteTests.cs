// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Web;
using Xunit;
using FhirClient = Microsoft.Health.Fhir.Tests.E2E.Common.FhirClient;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public class DeleteTests : IClassFixture<HttpIntegrationTestFixture<Startup>>
    {
        public DeleteTests(HttpIntegrationTestFixture<Startup> fixture)
        {
            Client = fixture.FhirClient;
        }

        protected FhirClient Client { get; set; }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResource_WhenDeleting_ThenServerShouldDeleteSuccessfully()
        {
            FhirResponse<Observation> response = await Client.CreateAsync(Samples.GetDefaultObservation());

            string resourceId = response.Resource.Id;
            string versionId = response.Resource.Meta.VersionId;

            // Delete the resource.
            await Client.DeleteAsync(response.Resource);

            // Subsequent read on the resource should return Gone.
            FhirException ex = await ExecuteAndValidateGoneStatus(() => Client.ReadAsync<Observation>(ResourceType.Observation, resourceId));

            string eTag = ex.Headers.ETag.ToString();

            Assert.NotNull(eTag);

            string deleteVersionId = WeakETag.FromWeakETag(eTag).VersionId;

            // Subsequent read on the specific version should still work.
            await Client.VReadAsync<Observation>(ResourceType.Observation, resourceId, versionId);

            // Subsequent read on the deleted version should return Gone.
            await ExecuteAndValidateGoneStatus(() => Client.VReadAsync<Observation>(ResourceType.Observation, resourceId, deleteVersionId));

            async Task<FhirException> ExecuteAndValidateGoneStatus(Func<Task> action)
            {
                FhirException exception = await Assert.ThrowsAsync<FhirException>(action);

                Assert.Equal(HttpStatusCode.Gone, exception.StatusCode);

                return exception;
            }
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAResource_WhenHardDeleting_ThenServerShouldDeleteAllRelatedResourcesSuccessfully()
        {
            List<string> versionIds = new List<string>();

            FhirResponse<Observation> response = await Client.CreateAsync(Samples.GetDefaultObservation());

            Observation observation = response.Resource;
            string resourceId = observation.Id;

            versionIds.Add(observation.Meta.VersionId);

            // Update the observation.
            observation.Status = ObservationStatus.EnteredInError;

            response = await Client.UpdateAsync(observation);

            versionIds.Add(response.Resource.Meta.VersionId);

            // Delete the observation
            await Client.DeleteAsync(observation);

            // Update the observation to resurrect the resource.
            observation.Status = ObservationStatus.Final;
            observation.Subject = new ResourceReference("Patient/123");

            response = await Client.UpdateAsync(observation);

            versionIds.Add(response.Resource.Meta.VersionId);

            // Hard-delete the resource.
            await Client.HardDeleteAsync(observation);

            // Getting the resource should result in NotFound.
            await ExecuteAndValidateNotFoundStatus(() => Client.ReadAsync<Observation>(ResourceType.Observation, resourceId));

            // Each version read should also result in NotFound.
            foreach (string versionId in versionIds)
            {
                await ExecuteAndValidateNotFoundStatus(() => Client.VReadAsync<Observation>(ResourceType.Observation, resourceId, versionId));
            }

            // History API should return NotFound.
            await ExecuteAndValidateNotFoundStatus(() => Client.SearchAsync($"Observation/{resourceId}/_history"));

            async Task<FhirException> ExecuteAndValidateNotFoundStatus(Func<Task> action)
            {
                FhirException exception = await Assert.ThrowsAsync<FhirException>(action);

                Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);

                return exception;
            }
        }

        [Fact]
        public async Task GivenAResourceWithLargeNumberOfHistory_WhenHardDeleting_ThenServerShouldDeleteAllRelatedResourcesSuccessfully()
        {
            List<string> versionIds = new List<string>();

            FhirResponse<Observation> response = await Client.CreateAsync(Samples.GetDefaultObservation());

            Observation observation = response.Resource;
            string resourceId = observation.Id;

            versionIds.Add(observation.Meta.VersionId);

            // Update the observation.
            for (int i = 0; i < 50; i++)
            {
                response = await Client.UpdateAsync(observation);

                versionIds.Add(response.Resource.Meta.VersionId);
            }

            // Hard-delete the resource.
            await Client.HardDeleteAsync(observation);

            // Getting the resource should result in NotFound.
            await ExecuteAndValidateNotFoundStatus(() => Client.ReadAsync<Observation>(ResourceType.Observation, resourceId));

            // Each version read should also result in NotFound.
            foreach (string versionId in versionIds)
            {
                await ExecuteAndValidateNotFoundStatus(() => Client.VReadAsync<Observation>(ResourceType.Observation, resourceId, versionId));
            }

            // History API should return NotFound.
            await ExecuteAndValidateNotFoundStatus(() => Client.SearchAsync($"Observation/{resourceId}/_history"));

            async Task<FhirException> ExecuteAndValidateNotFoundStatus(Func<Task> action)
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

                return exception;
            }
        }
    }
}
