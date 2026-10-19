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
using Microsoft.Health.Extensions.Xunit;
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

        [Theory]
        [InlineData("_include", 1, false)]
        [InlineData("_include", 2, false)]
        [InlineData("_include", 1, true)]
        [InlineData("_include", 2, true)]
        [InlineData("_include:iterate", 1, false)]
        [InlineData("_include:recurse", 2, true)]
        [InlineData("_revinclude", 1, false)]
        [InlineData("_revinclude", 2, false)]
        [InlineData("_revinclude", 1, true)]
        [InlineData("_revinclude", 2, true)]
        [InlineData("_revinclude:iterate", 2, false)]
        [InlineData("_revinclude:recurse", 1, true)]
        public async Task GivenMatchingResources_WhenDeletingConditionallyWithInclusions_ThenBadRequestIsReturnedAndNoResourcesAreDeleted(
            string parameter,
            int deleteCount,
            bool hardDelete)
        {
            // Arrange
            var tag = Guid.NewGuid().ToString();
            await CreateGroupWithPatients(tag, 5);
            var resourceType = parameter.StartsWith("_revinclude", StringComparison.Ordinal) ? "Patient" : "Group";

            // Act
            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.DeleteAsync(
                $"{resourceType}?_tag={tag}&_count={deleteCount}&hardDelete={hardDelete}&{parameter}=Group:member",
                CancellationToken.None));

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
            Assert.Contains(parameter, Assert.Single(exception.Response.Resource.Issue).Diagnostics);
            await ValidateResults(tag, 6);
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(null, true)]
        [InlineData(1, true)]
        [InlineData(2, true)]
        public async Task GivenObservationReferencingPatient_WhenDeletingWithInclude_ThenBadRequestIsReturnedAndBothResourcesRemain(int? deleteCount, bool hardDelete)
        {
            // Arrange
            using var patient = await _client.CreateAsync(new Patient());
            using var observation = await _client.CreateAsync(new Observation
            {
                Status = ObservationStatus.Final,
                Code = new CodeableConcept("http://loinc.org", "85354-9"),
                Subject = new ResourceReference($"Patient/{patient.Resource.Id}"),
            });
            var countParameter = deleteCount.HasValue ? $"&_count={deleteCount}" : string.Empty;

            // Act
            var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.DeleteAsync(
                $"Observation?_id={observation.Resource.Id}&_include=Observation:subject&hardDelete={hardDelete}{countParameter}",
                CancellationToken.None));

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
            using var remainingPatient = await _client.ReadAsync<Patient>($"Patient/{patient.Resource.Id}");
            using var remainingObservation = await _client.ReadAsync<Observation>($"Observation/{observation.Resource.Id}");
            Assert.Equal(patient.Resource.Meta.VersionId, remainingPatient.Resource.Meta.VersionId);
            Assert.Equal(observation.Resource.Meta.VersionId, remainingObservation.Resource.Meta.VersionId);
        }

        [Theory]
        [InlineData(Bundle.BundleType.Batch)]
        [InlineData(Bundle.BundleType.Transaction)]
        public async Task GivenConditionalDeleteWithIncludeInBundle_WhenSubmitting_ThenBadRequestIsReturnedAndNoResourcesAreDeleted(Bundle.BundleType bundleType)
        {
            // Arrange
            var tag = Guid.NewGuid().ToString();
            await CreateGroupWithPatients(tag, 1);
            var bundle = new Bundle
            {
                Type = bundleType,
                Entry =
                {
                    new Bundle.EntryComponent
                    {
                        Request = new Bundle.RequestComponent
                        {
                            Method = Bundle.HTTPVerb.DELETE,
                            Url = $"Group?_tag={tag}&_include=Group:member",
                        },
                    },
                },
            };

            // Act / Assert
            if (bundleType == Bundle.BundleType.Batch)
            {
                using var response = await _client.PostBundleAsync(bundle);
                Assert.Equal("400", Assert.Single(response.Resource.Entry).Response.Status);
            }
            else
            {
                var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.PostBundleAsync(bundle));
                Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
            }

            await ValidateResults(tag, 2);
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
