// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Web;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.ConditionalOperations)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class ConditionalDeleteTests : IClassFixture<HttpIntegrationTestFixture<Startup>>
    {
        private const string _resourceType = KnownResourceTypes.Encounter;
        private readonly TestFhirClient _client;
        private static SemaphoreSlim _createSemaphore = new(5, 5);

        public ConditionalDeleteTests(HttpIntegrationTestFixture<Startup> fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnIncompleteSearchParam_WhenDeletingConditionally_TheServerRespondsWithCorrectMessage()
        {
            FhirClientException fhirException = await Assert.ThrowsAsync<FhirClientException>(() => _client.DeleteAsync($"{_resourceType}?_tag=", CancellationToken.None));
            Assert.Equal(HttpStatusCode.PreconditionFailed, fhirException.StatusCode);
            Assert.Equal(fhirException.Response.Resource.Issue[0].Diagnostics, string.Format(Core.Resources.ConditionalOperationNotSelectiveEnough, _resourceType));
        }

        [InlineData(1)]
        [InlineData(100)]
        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenNoExistingResources_WhenDeletingConditionally_TheServerShouldReturnAccepted(int deleteCount)
        {
            var tag = Guid.NewGuid().ToString();
            await ValidateResults(tag, 0);

            FhirResponse response = await _client.DeleteAsync($"{_resourceType}?_tag={tag}&_count={deleteCount}", CancellationToken.None);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [InlineData(KnownQueryParameterNames.BulkHardDelete, true, 1)]
        [InlineData(KnownQueryParameterNames.HardDelete, true, 100)]
        [InlineData(KnownQueryParameterNames.HardDelete, false, 1)]
        [InlineData(KnownQueryParameterNames.BulkHardDelete, false, 100)]
        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenOneMatchingResource_WhenDeletingConditionally_TheServerShouldDeleteSuccessfully(string hardDeleteKey, bool hardDeleteValue, int deleteCount)
        {
            var tag = Guid.NewGuid().ToString();
            await CreateWithTag(tag);
            await ValidateResults(tag, 1);

            FhirResponse response = await _client.DeleteAsync($"{_resourceType}?_tag={tag}&{hardDeleteKey}={hardDeleteValue}&_count={deleteCount}", CancellationToken.None);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            await ValidateResults(tag, 0);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenMultipleMatchingResources_WhenDeletingConditionallyInSingleMode_TheServerShouldReturnError()
        {
            var tag = Guid.NewGuid().ToString();
            await CreateWithTag(tag);
            await CreateWithTag(tag);
            await ValidateResults(tag, 2);

            await Assert.ThrowsAsync<FhirClientException>(() => _client.DeleteAsync($"{_resourceType}?_tag={tag}", CancellationToken.None));
        }

        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(int.MaxValue)]
        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenMultipleMatchingResources_WhenDeletingConditionallyWithOutOfRangeCount_TheServerShouldReturnError(int deleteCount)
        {
            var tag = Guid.NewGuid().ToString();
            await Assert.ThrowsAsync<FhirClientException>(() => _client.DeleteAsync($"{_resourceType}?_tag={tag}&_count={deleteCount}", CancellationToken.None));
        }

        [InlineData(true)]
        [InlineData(false)]
        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenMultipleMatchingResources_WhenDeletingConditionallyWithMultipleFlag_TheServerShouldDeleteSuccessfully(bool hardDelete)
        {
            var tag = Guid.NewGuid().ToString();
            await CreateWithTag(tag);
            await CreateWithTag(tag);
            await CreateWithTag(tag);
            await ValidateResults(tag, 3);

            FhirResponse response = await _client.DeleteAsync($"{_resourceType}?_tag={tag}&hardDelete={hardDelete}&_count=100", CancellationToken.None);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(3, int.Parse(response.Headers.GetValues(KnownHeaders.ItemsDeleted).First()));

            await ValidateResults(tag, 0);
        }

        [InlineData(true, 50, 50, 0)]
        [InlineData(false, 50, 50, 0)]
        [InlineData(true, 10, 5, 5)]
        [InlineData(false, 10, 5, 5)]
        [Theory]
        public async Task GivenMatchingResources_WhenDeletingConditionallyWithMultipleFlag_TheServerShouldDeleteSuccessfully(bool hardDelete, int create, int delete, int expected)
        {
            var tag = Guid.NewGuid().ToString();

            await Task.WhenAll(Enumerable.Range(1, create).Select(_ => CreateWithTag(tag)));
            await ValidateResults(tag, create);

            FhirResponse response = await _client.DeleteAsync($"{_resourceType}?_tag={tag}&hardDelete={hardDelete}&_count={delete}", CancellationToken.None);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(delete, int.Parse(response.Headers.GetValues(KnownHeaders.ItemsDeleted).First()));

            await ValidateResults(tag, expected);
        }

        [Fact]
        public async Task GivenMatchingResources_WhenDeletingConditionallyWithInclude_ThenTheIncludedResourcesAreDeleted()
        {
            var tag = Guid.NewGuid().ToString();
            await CreateGroupWithPatients(tag, 5);
            await ValidateResults(tag, 6);
            FhirResponse response = await _client.DeleteAsync($"Group?_tag={tag}&_count=1&_include=Group:member", CancellationToken.None);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(6, int.Parse(response.Headers.GetValues(KnownHeaders.ItemsDeleted).First())); // currently returning 1 for some reason
            await ValidateResults(tag, 0);
        }

        [Fact]
        public async Task GivenMatchingResources_WhenHardDeletingConditionallyWithInclude_ThenTheIncludedResourcesAreDeleted()
        {
            var tag = Guid.NewGuid().ToString();
            await CreateGroupWithPatients(tag, 5);
            await ValidateResults(tag, 6);
            FhirResponse response = await _client.DeleteAsync($"Group?hardDelete=true&_tag={tag}&_count=1&_include=Group:member", CancellationToken.None);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(6, int.Parse(response.Headers.GetValues(KnownHeaders.ItemsDeleted).First())); // currently returning 1 for some reason
            await ValidateResults(tag, 0);
        }

        [Fact]
        public async Task GivenMultipleMatchingResources_WhenDeletingConditionallyWithInclude_ThenTheIncludedResourcesAreDeleted()
        {
            var tag = Guid.NewGuid().ToString();
            await CreateGroupWithPatients(tag, 5);
            await CreateGroupWithPatients(tag, 5);
            await ValidateResults(tag, 12);
            FhirResponse response = await _client.DeleteAsync($"Group?_tag={tag}&_count=2&_include=Group:member", CancellationToken.None);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(12, int.Parse(response.Headers.GetValues(KnownHeaders.ItemsDeleted).First())); // currently returning 1 for some reason
            await ValidateResults(tag, 0);
        }

        [Fact]
        public async Task GivenMatchingResources_WhenDeletingConditionallyWithMoreIncludedResourcesThanTheLimit_ThenBadRequestIsRetured()
        {
            var tag = Guid.NewGuid().ToString();
            await CreateGroupWithPatients(tag, 101);
            await ValidateResults(tag, 102);
            await Assert.ThrowsAsync<FhirClientException>(() => _client.DeleteAsync($"Group?_tag={tag}&_count=100&_include=Group:member&_includesCount=100", CancellationToken.None));
        }

        private async Task CreateWithTag(string tag)
        {
            await _createSemaphore.WaitAsync(TimeSpan.FromMinutes(1));

            try
            {
                Encounter encounter = Samples.GetJsonSample("Encounter-For-Patient-f001").ToPoco<Encounter>();

                encounter.Meta = new Meta();
                encounter.Meta.Tag = new System.Collections.Generic.List<Coding> { new Coding("http://e2etests", tag) };
                using FhirResponse<Encounter> response = await _client.CreateAsync(encounter);

                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            }
            finally
            {
                _createSemaphore.Release();
            }
        }

        private async Task CreateGroupWithPatients(string tag, int count)
        {
            await _createSemaphore.WaitAsync(TimeSpan.FromMinutes(1));
            try
            {
                Bundle createBundle = new Bundle();
                createBundle.Type = Bundle.BundleType.Batch;
                createBundle.Entry = new System.Collections.Generic.List<Bundle.EntryComponent>();

                Group group = new Group();
                group.Member = new System.Collections.Generic.List<Group.MemberComponent>();
#if !R5
                group.Actual = true;
#else
                group.Membership = Group.GroupMembershipBasis.Enumerated;
#endif
                group.Type = Group.GroupType.Person;

                group.Meta = new Meta();
                group.Meta.Tag = new System.Collections.Generic.List<Coding> { new Coding("http://e2etests", tag) };

                for (int i = 0; i < count; i++)
                {
                    var id = Guid.NewGuid();
                    var patient = new Patient();
                    patient.Meta = new Meta();
                    patient.Meta.Tag = new System.Collections.Generic.List<Coding> { new Coding("http://e2etests", tag) };
                    patient.Id = id.ToString();

                    createBundle.Entry.Add(new Bundle.EntryComponent { Resource = patient, Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.PUT, Url = $"Patient/{id}" } });

                    group.Member.Add(new Group.MemberComponent { Entity = new ResourceReference($"Patient/{id}") });
                }

                createBundle.Entry.Add(new Bundle.EntryComponent { Resource = group, Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.POST, Url = "Group" } });

                using FhirResponse<Bundle> response = await _client.PostBundleAsync(createBundle);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            finally
            {
                _createSemaphore.Release();
            }
        }

        private async Task ValidateResults(string tag, int expected)
        {
            var result = await GetResourceCount(tag);
            Assert.Equal(expected, result);
        }

        private async Task<int?> GetResourceCount(string tag)
        {
            try
            {
                FhirResponse<Bundle> result = await _client.SearchAsync($"?_tag=http://e2etests|{tag}&_summary=count");

                return result.Resource.Total;
            }
            catch (FhirClientException fce)
            {
                Assert.Fail($"A non-expected '{nameof(FhirClientException)}' was raised. Url: {_client.HttpClient.BaseAddress}. Activity Id: {fce.Response.GetRequestId()}. Error: {fce.Message}");
            }
            catch (Exception ex)
            {
                Assert.Fail($"A non-expected '{ex.GetType()}' was raised. Url: {_client.HttpClient.BaseAddress}. No Activity Id present. Error: {ex.Message}");
            }

            throw new NotImplementedException("Unreachable part of the code. The method should fail before reaching this part of the code.");
        }
    }
}
