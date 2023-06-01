// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Xunit.Sdk;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// This class covers history tests with different supported query parameters
    /// Other test collections are run parallelly causing some of the tests to fail intermittently as during the same timeframe resources could get created by other tests
    /// We generally use _tag query parameter to filter out the resources but since history search doesn't support _tag, filtering is done explicitly in the test
    /// While returning the search response default maxItem count is set to lower value, due to multiple tests running at the same time this could return entries with different tag code
    /// hence using the NextLink to keep querying for the next set
    /// Some tests have Thread.Sleep to avoid query time to fall in future
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.History)]
    [CollectionDefinition("History", DisableParallelization = true)]
    [Collection("History")]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class HistoryTests : IClassFixture<HttpIntegrationTestFixture>, IAsyncLifetime
    {
        private FhirResponse<Observation> _createdResource;
        private readonly TestFhirClient _client;
        private const string ContentUpdated = "Updated resource content";

        public HistoryTests(HttpIntegrationTestFixture fixture)
        {
            Fixture = fixture;
            _client = fixture.TestFhirClient;
        }

        protected HttpIntegrationTestFixture Fixture { get; }

        public async Task InitializeAsync()
        {
            _createdResource = await _client.CreateByUpdateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());
        }

        public async Task DisposeAsync()
        {
            if (_createdResource?.Resource != null)
            {
                await _client.DeleteAsync(_createdResource.Resource);
            }
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAType_WhenGettingResourceHistory_TheServerShouldReturnTheAppropriateBundleSuccessfully()
        {
            using FhirResponse<Bundle> readResponse = await _client.SearchAsync("Observation/_history");
            Assert.NotEmpty(readResponse.Resource.Entry);
        }

        [Theory]
        [InlineData("")]
        [InlineData("?_sort=-_lastUpdated")]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenATypeAndId_WhenGettingResourceHistory_TheServerShouldReturnTheAppropriateBundleSuccessfully(string queryString)
        {
            UpdateObservation(_createdResource.Resource);
            await _client.UpdateAsync(_createdResource.Resource);

            List<Bundle.EntryComponent> readResponse = await GetAllResultsWithMatchingTagForGivenSearch($"Observation/{_createdResource.Resource.Id}/_history" + queryString, string.Empty);

            AssertCount(2, readResponse);

            // Check most recent item is sorted first
            Assert.True(
                readResponse[0].Resource.Meta.LastUpdated >= readResponse[1].Resource.Meta.LastUpdated,
                userMessage: $"Record 0's latest update ({readResponse[0].Resource.Meta.LastUpdated}) is not greater or equal than Record's 1 latest update ({readResponse[1].Resource.Meta.LastUpdated}).");
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenATypeAndId_WhenGettingResourceHistoryWithAlternateSort_TheServerShouldReturnTheAppropriateBundleSuccessfully()
        {
            UpdateObservation(_createdResource.Resource);
            await _client.UpdateAsync(_createdResource.Resource);

            List<Bundle.EntryComponent> readResponse = await GetAllResultsWithMatchingTagForGivenSearch($"Observation/{_createdResource.Resource.Id}/_history?_sort=_lastUpdated", string.Empty);

            AssertCount(2, readResponse);

            Assert.True(
                readResponse[0].Resource.Meta.LastUpdated <= readResponse[1].Resource.Meta.LastUpdated,
                userMessage: $"Record 0's latest update ({readResponse[0].Resource.Meta.LastUpdated}) is not minor or equal than Record's 1 latest update ({readResponse[1].Resource.Meta.LastUpdated}).");
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenNoType_WhenGettingSystemHistory_TheServerShouldReturnTheAppropriateBundleSuccessfully()
        {
            using FhirResponse<Bundle> readResponse = await _client.SearchAsync("_history");

            Assert.NotEmpty(readResponse.Resource.Entry);
        }

        [Fact]
        public async Task GivenAType_WhenGettingResourceHistory_TheServerShouldReturnTheAppropriateBundleSuccessfullyWithResponseStatus()
        {
            Observation newCreatedResource = await _client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            var weakETag = $"W/\"{int.Parse(newCreatedResource.Meta.VersionId).ToString()}\"";

            UpdateObservation(newCreatedResource);
            await _client.UpdateAsync(newCreatedResource, weakETag);
            await _client.DeleteAsync(newCreatedResource);
            await _client.DeleteAsync(newCreatedResource);

            List<Bundle.EntryComponent> readResponse = await GetAllResultsWithMatchingTagForGivenSearch($"Observation/{newCreatedResource.Id}/_history", string.Empty);

            AssertCount(3, readResponse);
            foreach (var ent in readResponse)
            {
                if (ent.Request.Method == Bundle.HTTPVerb.POST)
                {
                    Assert.True(ent.Response.Status == "201 Created");
                }
                else if (ent.Request.Method == Bundle.HTTPVerb.DELETE)
                {
                    Assert.True(ent.Response.Status == "204 NoContent");
                }
                else
                {
                    Assert.True(ent.Response.Status == "200 OK");
                }
            }
        }

        [Fact]
        public async Task GivenAValueForSince_WhenGettingSystemHistory_TheServerShouldReturnOnlyRecordsModifiedAfterSinceValue()
        {
            var tag = Guid.NewGuid().ToString();

            var since = await CreatePatientAndGetStartTimeForHistoryTest(tag);
            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));

            Thread.Sleep(500);  // put a small gap between since and the first edits
            _createdResource.Resource.Text = new Narrative { Div = $"<div>Changed by E2E test {tag}</div>" };

            var updatedResource = await CreateResourceWithTag(_createdResource.Resource, tag);

            FhirResponse<Bundle> response;
            List<Bundle.EntryComponent> readResponse = new List<Bundle.EntryComponent>();
            string searchString = $"_history?_since={sinceUriString}";
            string selfLink = searchString;
            while (!string.IsNullOrEmpty(searchString))
            {
                response = await _client.SearchAsync(searchString);
                var readResponseWithMatchingTag = response.Resource.Entry.Where(e => e.Resource.Meta.Tag.Any(t => t.Code == tag)).ToList();

                readResponse.AddRange(readResponseWithMatchingTag);
                searchString = response.Resource.NextLink?.ToString();

                if (readResponseWithMatchingTag.Count == 1)
                {
                    selfLink = response.Resource.SelfLink?.ToString();
                }
            }

            AssertCount(1, readResponse);

            var obsHistory = readResponse[0].Resource as Observation;

            Assert.NotNull(obsHistory);
            Assert.Contains($"Changed by E2E test {tag}", obsHistory.Text.Div);

            List<Bundle.EntryComponent> selfLinkResponseWithmatchingTag = await GetAllResultsWithMatchingTagForGivenSearch(selfLink, tag);

            AssertCount(1, selfLinkResponseWithmatchingTag);

            obsHistory = selfLinkResponseWithmatchingTag[0].Resource as Observation;

            Assert.NotNull(obsHistory);
            Assert.Contains($"Changed by E2E test {tag}", obsHistory.Text.Div);
        }

        [Fact]
        public async Task GivenResourceInsideAndOutsideHistoryRange_WhenGettingSystemHistory_ServerShouldReturnOnlyIfInside()
        {
            var tag = Guid.NewGuid().ToString();
            var since = await CreatePatientAndGetStartTimeForHistoryTest(tag);
            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
            var response = await GetAllResultsWithMatchingTagForGivenSearch($"_history?_since={sinceUriString}", tag);
            AssertCount(0, response, since); // nothing
            since = since.AddMilliseconds(-1);
            sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
            response = await GetAllResultsWithMatchingTagForGivenSearch($"_history?_since={sinceUriString}", tag);
            AssertCount(1, response, since);
        }

        [Fact]
        public async Task GivenAValueForSinceAndBeforeWithModifications_WhenGettingSystemHistory_TheServerShouldOnlyCorrectResources()
        {
            var tag = Guid.NewGuid().ToString();

            var since = await CreatePatientAndGetStartTimeForHistoryTest(tag);

            Thread.Sleep(500);  // put a small gap between since and the first edits

            _createdResource.Resource.Text = new Narrative { Div = $"<div>Changed by E2E test {tag}</div>" };

            await CreateResourceWithTag(_createdResource.Resource, tag);
            var newPatient = await CreateResourceWithTag(Samples.GetDefaultPatient().ToPoco(), tag);

            Thread.Sleep(500);  // make sure that the before time is not in the future
            var before = newPatient.Meta.LastUpdated.Value.AddMilliseconds(100);

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
            var beforeUriString = HttpUtility.UrlEncode(before.ToString("o"));

            List<Bundle.EntryComponent> readResponseWithMatchingTag = await GetAllResultsWithMatchingTagForGivenSearch($"_history?_since={sinceUriString}&_before={beforeUriString}", tag);

            AssertCount(2, readResponseWithMatchingTag, since, before);

            Patient patientHistory;
            var obsHistory = readResponseWithMatchingTag[0].Resource as Observation;

            if (obsHistory == null)
            {
                patientHistory = readResponseWithMatchingTag[0].Resource as Patient;
                obsHistory = readResponseWithMatchingTag[1].Resource as Observation;
            }
            else
            {
                patientHistory = readResponseWithMatchingTag[1].Resource as Patient;
            }

            Assert.NotNull(obsHistory);
            Assert.NotNull(patientHistory);
            Assert.Contains($"Changed by E2E test {tag}", obsHistory.Text.Div);
            Assert.Equal(newPatient.Id, patientHistory.Id);

            if (newPatient != null)
            {
                await _client.DeleteAsync(newPatient);
            }
        }

        [Fact]
        public async Task GivenAValueForSinceAndBeforeCloseToLastModifiedTime_WhenGettingSystemHistory_TheServerShouldNotMissRecords()
        {
            var tag = Guid.NewGuid().ToString();

            var since = await CreatePatientAndGetStartTimeForHistoryTest(tag);

            var newResources = new List<Resource>();

            // Wait for some time before making new resources
            // In multiple instances, the server times might vary causing undesirable results
            // We want to make sure below resources are made after the since time
            Thread.Sleep(500);

            // First make a few edits
            _createdResource.Resource.Text = new Narrative { Div = $"<div>Changed by E2E test {tag}</div>" };
            Observation updatedObservationResource = await CreateResourceWithTag(_createdResource.Resource, tag);

            newResources.Add(await CreateResourceWithTag(Samples.GetDefaultPatient().ToPoco(), tag));
            newResources.Add(await CreateResourceWithTag(Samples.GetDefaultOrganization().ToPoco(), tag));
            newResources.Add(await CreateResourceWithTag(Samples.GetJsonSample("BloodGlucose").ToPoco(), tag));
            newResources.Add(await CreateResourceWithTag(Samples.GetJsonSample("BloodPressure").ToPoco(), tag));
            newResources.Add(await CreateResourceWithTag(Samples.GetJsonSample("Patient-f001").ToPoco(), tag));
            newResources.Add(await CreateResourceWithTag(Samples.GetJsonSample("Condition-For-Patient-f001").ToPoco(), tag));

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));

            // For this test to work we want to make sure that 'since' is earlier than the resources created in above edits
            Assert.True(since < updatedObservationResource.Meta.LastUpdated);
            Assert.True(since < newResources.OrderBy(r => r.Meta.LastUpdated).First().Meta.LastUpdated);

            // Query all the recent changes
            List<Bundle.EntryComponent> allChangesWithMatchingTag = await GetAllResultsWithMatchingTagForGivenSearch($"_history?_since={sinceUriString}", tag);

            AssertCount(7, allChangesWithMatchingTag);

            // now choose a value of before that is as close as possible to one of the last updated times
            var lastUpdatedTimes = allChangesWithMatchingTag.Select(e => e.Resource.Meta.LastUpdated).OrderBy(d => d.Value);

            var before = lastUpdatedTimes.ToList()[4];
            var beforeUriString = HttpUtility.UrlEncode(before.Value.ToString("o"));

            var firstSetWithMatchingTag = await GetAllResultsWithMatchingTagForGivenSearch($"_history?_since={sinceUriString}&_before={beforeUriString}", tag);

            AssertCount(4, firstSetWithMatchingTag);

            // Though we try to create an Observation resource first with editted text, the lastUpdated time could be different depending on which server instance receives the request
            // Meaning Request to Patient could have earlier lastUpdated time than the Observation resource
            var obsHistory = firstSetWithMatchingTag.Where(r => r.Resource.TypeName.Equals("Observation", StringComparison.OrdinalIgnoreCase)).OrderBy(d => d.Resource.Meta.LastUpdated).ToList()[0].Resource as Observation;
            Assert.NotNull(obsHistory);
            Assert.Contains($"Changed by E2E test {tag}", obsHistory.Text.Div);

            sinceUriString = beforeUriString;
            before = DateTime.UtcNow;
            beforeUriString = HttpUtility.UrlEncode(before.Value.ToString("o"));
            Thread.Sleep(500); // wait 500 milliseconds to make sure that the value passed to the server for _before is not a time in the future

            var secondSetWithMatchingTag = await GetAllResultsWithMatchingTagForGivenSearch($"_history?_since={sinceUriString}&_before={beforeUriString}", tag);

            AssertCount(3, secondSetWithMatchingTag);

            foreach (var r in newResources)
            {
                await _client.DeleteAsync(r);
            }
        }

        [Fact]
        public async Task GivenAQueryThatReturnsMoreThan10Results_WhenGettingSystemHistory_TheServerShouldBatchTheResponse()
        {
            var tag = Guid.NewGuid().ToString();
            var since = await CreatePatientAndGetStartTimeForHistoryTest(tag);

            var newResources = new List<Resource>();

            // Wait for some time before making new resources
            // In multiple instances, the server times might vary causing undesirable results
            // We want to make sure below 11 resources are made after the since time
            Thread.Sleep(500);

            // First make 11 edits
            _createdResource.Resource.Text = new Narrative { Div = $"<div>Changed by E2E test {tag}</div>" };
            var observationResource = await CreateResourceWithTag(_createdResource.Resource, tag);
            newResources.Add(await CreateResourceWithTag(Samples.GetDefaultPatient().ToPoco(), tag));
            newResources.Add(await CreateResourceWithTag(Samples.GetDefaultOrganization().ToPoco(), tag));
            newResources.Add(await CreateResourceWithTag(Samples.GetJsonSample("BloodGlucose").ToPoco(), tag));
            newResources.Add(await CreateResourceWithTag(Samples.GetJsonSample("BloodPressure").ToPoco(), tag));
            newResources.Add(await CreateResourceWithTag(Samples.GetJsonSample("Patient-f001").ToPoco(), tag));
            newResources.Add(await CreateResourceWithTag(Samples.GetJsonSample("Condition-For-Patient-f001").ToPoco(), tag));
            newResources.Add(await CreateResourceWithTag(Samples.GetJsonSample("Encounter-For-Patient-f001").ToPoco(), tag));
            newResources.Add(await CreateResourceWithTag(Samples.GetJsonSample("Observation-For-Patient-f001").ToPoco(), tag));
            newResources.Add(await CreateResourceWithTag(Samples.GetJsonSample("ObservationWith1MinuteApgarScore").ToPoco(), tag));
            newResources.Add(await CreateResourceWithTag(Samples.GetJsonSample("ObservationWith20MinuteApgarScore").ToPoco(), tag));

            var lastUpdatedTimes = newResources.Select(e => e.Meta.LastUpdated).OrderBy(d => d.Value);

            // For this test to work 'since' value should be earlier than the first resource created in the above 11 edits
            Assert.True(since < observationResource.Meta.LastUpdated);
            Assert.True(since < lastUpdatedTimes.First());

            var before = lastUpdatedTimes.Last().Value.AddMilliseconds(100);

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
            var beforeUriString = HttpUtility.UrlEncode(before.ToString("o"));

            List<Bundle.EntryComponent> readResponse = await GetAllResultsWithMatchingTagForGivenSearch($"_history?_since={sinceUriString}", tag);

            AssertCount(11, readResponse);

            // Making sure the observation was updated properly.
            var obsHistory = readResponse.Where(o => o.Resource.Id == _createdResource.Resource.Id).ToList()[0].Resource as Observation;
            Assert.NotNull(obsHistory);
            Assert.Contains($"Changed by E2E test {tag}", obsHistory.Text.Div);

            newResources.Add(await _client.CreateByUpdateAsync(Samples.GetJsonSample("ObservationWithBloodPressure").ToPoco()));
            newResources.Add(await _client.CreateByUpdateAsync(Samples.GetJsonSample("ObservationWithEyeColor").ToPoco()));

            Thread.Sleep(500);

            readResponse = await GetAllResultsWithMatchingTagForGivenSearch($"_history?_since={sinceUriString}&_before={beforeUriString}", tag);

            AssertCount(11, readResponse);

            foreach (var r in newResources)
            {
                await _client.DeleteAsync(r);
            }
        }

        [Fact]
        public async Task GivenAValueForSinceAfterAllModificatons_WhenGettingSystemHistory_TheServerShouldReturnAnEmptyResult()
        {
            var tag = Guid.NewGuid().ToString();
            var updatedResource = await CreateResourceWithTag(_createdResource.Resource, tag);

            // ensure that the server has fully processed the PUT
            var since = await CreatePatientAndGetStartTimeForHistoryTest(tag);

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));

            List<Bundle.EntryComponent> readResponse = await GetAllResultsWithMatchingTagForGivenSearch($"_history?_since={sinceUriString}", tag);

            Assert.Empty(readResponse);
        }

        [Fact]
        public async Task GivenAValueForSinceAndBeforeWithNoModifications_WhenGettingSystemHistory_TheServerShouldReturnAnEmptyResult()
        {
            var tag = Guid.NewGuid().ToString();

            // Create new patient with tag and get the LastUpdated value = before  (maybe request going to instance 1)
            var updatedResource = await CreateResourceWithTag(_createdResource.Resource, tag);
            var before = updatedResource.Meta.LastUpdated.Value.AddMilliseconds(10);

            // Wait a little bit before creating a Patient as request could go to different instance with different time
            Thread.Sleep(500);

            // Now create another patient and get the LastUpdated value as since, ensure that the server has fully processed the PUT
            var since = await CreatePatientAndGetStartTimeForHistoryTest(tag);

            // We want to make sure before < since for this test to work
            // Below will fail if multiple server instances vary too much in time
            Assert.True(before < since);

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
            var beforeUriString = HttpUtility.UrlEncode(before.ToString("o"));

            List<Bundle.EntryComponent> readResponse = await GetAllResultsWithMatchingTagForGivenSearch($"_history?_since={sinceUriString}&_before={beforeUriString}", tag);

            Assert.Empty(readResponse);
        }

        [Fact]
        public async Task GivenAValueForBeforeInTheFuture_WhenGettingSystemHistory_AnErrorIsReturned()
        {
            var before = DateTime.UtcNow.AddSeconds(300);
            var beforeUriString = HttpUtility.UrlEncode(before.ToString("o"));
            var ex = await Assert.ThrowsAsync<FhirClientException>(() => _client.SearchAsync("_history?_before=" + beforeUriString));

            Assert.Contains("Parameter _before cannot a be a value in the future", ex.Message);
        }

        [Fact]
        public async Task GivenAnInvalidSortValue_WhenGettingSystemHistory_AnErrorIsReturned()
        {
            var ex = await Assert.ThrowsAsync<FhirClientException>(() => _client.SearchAsync("_history?_sort=_id"));

            Assert.Contains("Sorting by the '_id' parameter is not supported.", ex.Message);
        }

        [Fact]
        public async Task GivenAValueForUnSupportedAt_WhenGettingSystemHistory_TheAtIsDroppedFromUrl()
        {
            var tag = Guid.NewGuid().ToString();
            var at = await CreatePatientAndGetStartTimeForHistoryTest(tag);
            var atUriString = HttpUtility.UrlEncode(at.ToString("o"));

            using FhirResponse<Bundle> readResponse = await _client.SearchAsync("_history?_at=" + atUriString);

            Assert.NotNull(readResponse.Resource.Entry);

            var actualSelfLink = WebUtility.UrlDecode("_history");
            Assert.Equal(readResponse.Resource.SelfLink.AbsoluteUri, Fixture.GenerateFullUrl(actualSelfLink));
        }

        /// <summary>
        /// Find a time to use _since where there have been no results in history
        /// so we can start from clean start point
        /// </summary>
        /// <returns>DateTimeOffset set to a good value for _since</returns>
        private async Task<DateTimeOffset> CreatePatientAndGetStartTimeForHistoryTest(string tag)
        {
            Resource resource = Samples.GetDefaultPatient().ToPoco();
            resource.Meta = new Meta();
            resource.Meta.Tag.Add(new Coding(string.Empty, tag));
            resource.Meta.Tag.Add(new Coding(string.Empty, "startTimeResource"));

            using FhirResponse<Resource> response = await _client.CreateByUpdateAsync(resource);
            var lastUpdated = response.Resource.Meta.LastUpdated.Value;
            return lastUpdated.AddMilliseconds(1);
        }

        /// <summary>
        /// Get all the results for given search string matching the tag
        /// </summary>
        /// <returns>List<Bundle.EntryComponent> for the given search string</returns>
        private async Task<List<Bundle.EntryComponent>> GetAllResultsWithMatchingTagForGivenSearch(string searchString, string tag)
        {
            FhirResponse<Bundle> response;
            List<Bundle.EntryComponent> readResponse = new List<Bundle.EntryComponent>();
            while (!string.IsNullOrEmpty(searchString))
            {
                response = await _client.SearchAsync(searchString);
                readResponse.AddRange(response.Resource.Entry);
                searchString = response.Resource.NextLink?.ToString();
            }

            if (!string.IsNullOrEmpty(tag))
            {
                return readResponse.Where(e => e.Resource.Meta.Tag.Any(t => t.Code == tag)).ToList();
            }

            return readResponse;
        }

        private async Task<T> CreateResourceWithTag<T>(T resource, string tag)
            where T : Resource
        {
            resource.Meta = new Meta();
            resource.Meta.Tag.Add(new Coding(string.Empty, tag));
            return await _client.CreateByUpdateAsync(resource);
        }

        private void AssertCount<TBase>(int expected, ICollection<TBase> collection, DateTimeOffset? since = null, DateTimeOffset? before = null)
            where TBase : Base
        {
            if (collection.Count == expected)
            {
                return;
            }

            var sb = new StringBuilder("Expected count to be ").Append(expected).Append(" but was ").Append(collection.Count).AppendLine(" . Contents:");
            var fhirJsonSerializer = new FhirJsonSerializer(new SerializerSettings() { AppendNewLine = false, Pretty = false });
            using var sw = new StringWriter(sb);

            foreach (TBase element in collection)
            {
                sb.AppendLine(fhirJsonSerializer.SerializeToString(element));
            }

            if (since.HasValue)
            {
                sb.AppendLine($"since={since.Value.ToString("o")}");
                sb.AppendLine($"sinceSurr={SqlServer.Features.Storage.ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(since.Value.DateTime)}");
            }

            if (before.HasValue)
            {
                sb.AppendLine($"before={before.Value.ToString("o")}");
                sb.AppendLine($"beforeSurr={SqlServer.Features.Storage.ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(before.Value.DateTime)}");
            }

            throw new XunitException(sb.ToString());
        }

        private static void UpdateObservation(Observation observationResource)
        {
            observationResource.Text = new Narrative
            {
                Status = Narrative.NarrativeStatus.Generated,
                Div = $"<div>{ContentUpdated}</div>",
            };
        }
    }
}
