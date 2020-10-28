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
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    [CollectionDefinition("History", DisableParallelization = true)]
    [Collection("History")]
    public class HistoryTests : IClassFixture<HttpIntegrationTestFixture>, IAsyncLifetime
    {
        private FhirResponse<Observation> _createdResource;
        private readonly TestFhirClient _client;

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

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenATypeAndId_WhenGettingResourceHistory_TheServerShouldReturnTheAppropriateBundleSuccessfully()
        {
            using FhirResponse<Bundle> readResponse = await _client.SearchAsync($"Observation/{_createdResource.Resource.Id}/_history");

            Assert.NotEmpty(readResponse.Resource.Entry);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenNoType_WhenGettingSystemHistory_TheServerShouldReturnTheAppropriateBundleSuccessfully()
        {
            using FhirResponse<Bundle> readResponse = await _client.SearchAsync("_history");

            Assert.NotEmpty(readResponse.Resource.Entry);
        }

        [Fact]
        public async Task GivenAValueForSince_WhenGettingSystemHistory_TheServerShouldReturnOnlyRecordsModifiedAfterSinceValue()
        {
            var tag = Guid.NewGuid().ToString();

            var since = await GetStartTimeForHistoryTest(tag);
            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));

            Thread.Sleep(500);  // put a small gap between since and the first edits
            _createdResource.Resource.Text = new Narrative { Div = "<div>Changed by E2E test</div>" };

            var updatedResource = await CreateResourceWithTag(_createdResource.Resource, tag);

            using FhirResponse<Bundle> readResponse = await _client.SearchAsync($"_history?_tag={tag}&_since={sinceUriString}");

            AssertCount(1, readResponse.Resource.Entry);

            var obsHistory = readResponse.Resource.Entry[0].Resource as Observation;

            Assert.NotNull(obsHistory);
            Assert.Contains("Changed by E2E test", obsHistory.Text.Div);

            using FhirResponse<Bundle> selfLinkResponse = await _client.SearchAsync(readResponse.Resource.SelfLink.ToString());

            AssertCount(1, selfLinkResponse.Resource.Entry);

            obsHistory = selfLinkResponse.Resource.Entry[0].Resource as Observation;

            Assert.NotNull(obsHistory);
            Assert.Contains("Changed by E2E test", obsHistory.Text.Div);
        }

        [Fact]
        public async Task GivenAValueForSinceAndBeforeWithModifications_WhenGettingSystemHistory_TheServerShouldOnlyCorrectResources()
        {
            var tag = Guid.NewGuid().ToString();

            var since = await GetStartTimeForHistoryTest(tag);

            Thread.Sleep(500);  // put a small gap between since and the first edits

            _createdResource.Resource.Text = new Narrative { Div = "<div>Changed by E2E test</div>" };

            await CreateResourceWithTag(_createdResource.Resource, tag);
            var newPatient = await CreateResourceWithTag(Samples.GetDefaultPatient().ToPoco(), tag);

            var before = newPatient.Meta.LastUpdated.Value.AddMilliseconds(100);
            Thread.Sleep(500);  // make sure that the before time is not in the future

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
            var beforeUriString = HttpUtility.UrlEncode(before.ToString("o"));

            using FhirResponse<Bundle> readResponse = await _client.SearchAsync($"_history?_tag={tag}&_since={sinceUriString}&_before={beforeUriString}");

            AssertCount(2, readResponse.Resource.Entry);

            Patient patientHistory;
            var obsHistory = readResponse.Resource.Entry[0].Resource as Observation;

            if (obsHistory == null)
            {
                patientHistory = readResponse.Resource.Entry[0].Resource as Patient;
                obsHistory = readResponse.Resource.Entry[1].Resource as Observation;
            }
            else
            {
                patientHistory = readResponse.Resource.Entry[1].Resource as Patient;
            }

            Assert.NotNull(obsHistory);
            Assert.NotNull(patientHistory);
            Assert.Contains("Changed by E2E test", obsHistory.Text.Div);
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

            var since = await GetStartTimeForHistoryTest(tag);

            var newResources = new List<Resource>();

            // First make a few edits
            _createdResource.Resource.Text = new Narrative { Div = "<div>Changed by E2E test</div>" };
            await CreateResourceWithTag(_createdResource.Resource, tag);

            newResources.Add(await CreateResourceWithTag(Samples.GetDefaultPatient().ToPoco(), tag));
            newResources.Add(await CreateResourceWithTag(Samples.GetDefaultOrganization().ToPoco(), tag));
            Thread.Sleep(1000);
            newResources.Add(await CreateResourceWithTag(Samples.GetJsonSample("BloodGlucose").ToPoco(), tag));
            newResources.Add(await CreateResourceWithTag(Samples.GetJsonSample("BloodPressure").ToPoco(), tag));
            newResources.Add(await CreateResourceWithTag(Samples.GetJsonSample("Patient-f001").ToPoco(), tag));
            newResources.Add(await CreateResourceWithTag(Samples.GetJsonSample("Condition-For-Patient-f001").ToPoco(), tag));

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));

            // Query all the recent changes
            using FhirResponse<Bundle> allChanges = await _client.SearchAsync($"_history?_tag={tag}&_since=" + sinceUriString);

            AssertCount(7, allChanges.Resource.Entry);

            // now choose a value of before that is as close as possible to one of the last updated times
            var lastUpdatedTimes = allChanges.Resource.Entry.Select(e => e.Resource.Meta.LastUpdated).OrderBy(d => d.Value);

            var before = lastUpdatedTimes.ToList()[4];
            var beforeUriString = HttpUtility.UrlEncode(before.Value.ToString("o"));
            Thread.Sleep(500);
            var firstSet = await _client.SearchAsync($"_history?_tag={tag}&_since={sinceUriString}&_before={beforeUriString}");

            AssertCount(4, firstSet.Resource.Entry);

            sinceUriString = beforeUriString;
            before = DateTime.UtcNow;
            beforeUriString = HttpUtility.UrlEncode(before.Value.ToString("o"));
            Thread.Sleep(500); // wait 500 milliseconds to make sure that the value passed to the server for _before is not a time in the future
            var secondSet = await _client.SearchAsync($"_history?_tag={tag}&_since={sinceUriString}&_before={beforeUriString}");

            AssertCount(3, secondSet.Resource.Entry);

            foreach (var r in newResources)
            {
                await _client.DeleteAsync(r);
            }
        }

        [Fact]
        public async Task GivenAQueryThatReturnsMoreThan10Results_WhenGettingSystemHistory_TheServerShouldBatchTheResponse()
        {
            var tag = Guid.NewGuid().ToString();
            var since = await GetStartTimeForHistoryTest(tag);

            var newResources = new List<Resource>();

            // First make 11 edits
            _createdResource.Resource.Text = new Narrative { Div = "<div>Changed by E2E test</div>" };
            await CreateResourceWithTag(_createdResource.Resource, tag);
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
            var before = lastUpdatedTimes.Last().Value.AddMilliseconds(100);

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
            var beforeUriString = HttpUtility.UrlEncode(before.ToString("o"));

            using FhirResponse<Bundle> readResponse = await _client.SearchAsync($"_history?_tag={tag}&_since={sinceUriString}");

            AssertCount(10, readResponse.Resource.Entry);

            Thread.Sleep(500);

            newResources.Add(await _client.CreateByUpdateAsync(Samples.GetJsonSample("ObservationWithBloodPressure").ToPoco()));
            newResources.Add(await _client.CreateByUpdateAsync(Samples.GetJsonSample("ObservationWithEyeColor").ToPoco()));

            Thread.Sleep(500);

            using FhirResponse<Bundle> firstBatch = await _client.SearchAsync($"_history?_tag={tag}&_since={sinceUriString}&_before={beforeUriString}");

            AssertCount(10, firstBatch.Resource.Entry);

            var secondBatch = await _client.SearchAsync(firstBatch.Resource.NextLink.ToString());

            AssertCount(1, secondBatch.Resource.Entry);

            foreach (var r in newResources)
            {
                await _client.DeleteAsync(r);
            }
        }

        [Fact]
        public async Task GivenAValueForSinceAfterAllModificatons_WhenGettingSystemHistory_TheServerShouldReturnAnEmptyResult()
        {
            _createdResource.Resource.Text = new Narrative { Div = "<div>Changed by E2E test</div>" };
            var tag = Guid.NewGuid().ToString();
            var updatedResource = await CreateResourceWithTag(_createdResource.Resource, tag);

            // ensure that the server has fully processed the PUT
            var since = await GetStartTimeForHistoryTest(tag);

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));

            using FhirResponse<Bundle> readResponse = await _client.SearchAsync($"_history?_tag={tag}&_since={sinceUriString}");

            Assert.Empty(readResponse.Resource.Entry);
        }

        [Fact]
        public async Task GivenAValueForSinceAndBeforeWithNoModifications_WhenGettingSystemHistory_TheServerShouldReturnAnEmptyResult()
        {
            _createdResource.Resource.Text = new Narrative { Div = "<div>Changed by E2E test</div>" };
            var tag = Guid.NewGuid().ToString();
            var updatedResource = await CreateResourceWithTag(_createdResource.Resource, tag);

            // ensure that the server has fully processed the PUT
            var since = await GetStartTimeForHistoryTest(tag);
            var before = updatedResource.Meta.LastUpdated.Value.AddMilliseconds(100);

            Thread.Sleep(500);

            var newPatient = await _client.CreateByUpdateAsync(Samples.GetDefaultPatient().ToPoco());

            Assert.True(before < newPatient.Resource.Meta.LastUpdated.Value);

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
            var beforeUriString = HttpUtility.UrlEncode(before.ToString("o"));

            using FhirResponse<Bundle> readResponse = await _client.SearchAsync($"_history?_tag={tag}&_since={sinceUriString}&_before={beforeUriString}");

            Assert.Empty(readResponse.Resource.Entry);

            if (newPatient?.Resource != null)
            {
                await _client.DeleteAsync(newPatient.Resource);
            }
        }

        [Fact]
        public async Task GivenAValueForBeforeInTheFuture_WhenGettingSystemHistory_AnErrorIsReturned()
        {
            var before = DateTime.UtcNow.AddSeconds(300);
            var beforeUriString = HttpUtility.UrlEncode(before.ToString("o"));
            var ex = await Assert.ThrowsAsync<FhirException>(() => _client.SearchAsync("_history?_before=" + beforeUriString));

            Assert.Contains("Parameter _before cannot a be a value in the future", ex.Message);
        }

        [Fact]
        public async Task GivenAValueForUnSupportedAt_WhenGettingSystemHistory_TheAtIsDroppedFromUrl()
        {
            var tag = Guid.NewGuid().ToString();
            var at = await GetStartTimeForHistoryTest(tag);
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
        /// <returns>DateIimeOffset set to a good value for _since</returns>
        private async Task<DateTimeOffset> GetStartTimeForHistoryTest(string tag)
        {
            Resource resource = Samples.GetDefaultPatient().ToPoco();
            resource.Meta = new Meta();
            resource.Meta.Tag.Add(new Coding(string.Empty, tag));
            resource.Meta.Tag.Add(new Coding(string.Empty, "startTimeResource"));

            using FhirResponse<Resource> response = await _client.CreateByUpdateAsync(resource);
            await Task.Delay(10);
            return response.Resource.Meta.LastUpdated.Value.AddMilliseconds(1);
        }

        private async Task<T> CreateResourceWithTag<T>(T resource, string tag)
            where T : Resource
        {
            resource.Meta = new Meta();
            resource.Meta.Tag.Add(new Coding(string.Empty, tag));
            return await _client.CreateByUpdateAsync(resource);
        }

        private void AssertCount<TBase>(int expected, ICollection<TBase> collection)
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

            throw new XunitException(sb.ToString());
        }
    }
}
