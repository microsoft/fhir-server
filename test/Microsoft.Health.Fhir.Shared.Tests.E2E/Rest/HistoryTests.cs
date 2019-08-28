// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
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
    [CollectionDefinition("History", DisableParallelization = true)]
    [Collection("History")]
    public class HistoryTests : IClassFixture<HttpIntegrationTestFixture>, IDisposable
    {
        private FhirResponse<Observation> _createdResource;

        public HistoryTests(HttpIntegrationTestFixture fixture)
        {
            Client = fixture.FhirClient;

            _createdResource = Client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>()).GetAwaiter().GetResult();
        }

        protected FhirClient Client { get; set; }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenGettingResourceHistory_GivenAType_TheServerShouldReturnTheAppropriateBundleSuccessfully()
        {
            FhirResponse<Bundle> readResponse = await Client.SearchAsync("Observation/_history");
            Assert.NotEmpty(readResponse.Resource.Entry);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenGettingResourceHistory_GivenATypeAndId_TheServerShouldReturnTheAppropriateBundleSuccessfully()
        {
            FhirResponse<Bundle> readResponse = await Client.SearchAsync($"Observation/{_createdResource.Resource.Id}/_history");

            Assert.NotEmpty(readResponse.Resource.Entry);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenGettingSystemHistory_GivenNoType_TheServerShouldReturnTheAppropriateBundleSuccessfully()
        {
            FhirResponse<Bundle> readResponse = await Client.SearchAsync("_history");

            Assert.NotEmpty(readResponse.Resource.Entry);
        }

        [Fact]
        public async Task WhenGettingSystemHistory_GivenAValueForSince_TheServerShouldReturnOnlyRecordsModifiedAfterSinceValue()
        {
            var since = await GetStartTimeForHistoryTest();
            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));

            Thread.Sleep(500);  // put a small gap between since and the first edits
            _createdResource.Resource.Text = new Narrative { Div = "<div>Changed by E2E test</div>" };

            var updatedResource = await Client.UpdateAsync<Observation>(_createdResource);

            FhirResponse<Bundle> readResponse = await Client.SearchAsync("_history?_since=" + sinceUriString);

            Assert.Single(readResponse.Resource.Entry);

            var obsHistory = readResponse.Resource.Entry[0].Resource as Observation;

            Assert.NotNull(obsHistory);
            Assert.Contains("Changed by E2E test", obsHistory.Text.Div);

            FhirResponse<Bundle> selfLinkResponse = await Client.SearchAsync(readResponse.Resource.SelfLink.ToString());

            Assert.Single(selfLinkResponse.Resource.Entry);

            obsHistory = selfLinkResponse.Resource.Entry[0].Resource as Observation;

            Assert.NotNull(obsHistory);
            Assert.Contains("Changed by E2E test", obsHistory.Text.Div);
        }

        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)] // History tests are unstable at the moment due to Cosmos DB issue with continuation tokens
        [Fact]
        public async Task WhenGettingSystemHistory_GivenAValueForSinceAndBeforeWithModifications_TheServerShouldOnlyCorrectResources()
        {
            var since = await GetStartTimeForHistoryTest();

            Thread.Sleep(500);  // put a small gap between since and the first edits

            _createdResource.Resource.Text = new Narrative { Div = "<div>Changed by E2E test</div>" };
            await Client.UpdateAsync<Observation>(_createdResource);
            FhirResponse<Resource> newPatient = await Client.CreateAsync(Samples.GetDefaultPatient().ToPoco());

            var before = newPatient.Resource.Meta.LastUpdated.Value.AddMilliseconds(100);
            Thread.Sleep(500);  // make sure that the before time is not in the future

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
            var beforeUriString = HttpUtility.UrlEncode(before.ToString("o"));

            FhirResponse<Bundle> readResponse = await Client.SearchAsync("_history?_since=" + sinceUriString + "&_before=" + beforeUriString);

            Assert.Equal(2, readResponse.Resource.Entry.Count);

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
            Assert.Equal(newPatient.Resource.Id, patientHistory.Id);

            if (newPatient?.Resource != null)
            {
                await Client.DeleteAsync(newPatient.Resource);
            }
        }

        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)] // History tests are unstable at the moment due to Cosmos DB issue with continuation tokens
        [Fact]
        public async Task WhenGettingSystemHistory_GivenAValueForSinceAndBeforeCloseToLastModifiedTime_TheServerShouldNotMissRecords()
        {
            var since = await GetStartTimeForHistoryTest();

            var newResources = new List<Resource>();

            // First make a few edits
            _createdResource.Resource.Text = new Narrative { Div = "<div>Changed by E2E test</div>" };
            await Client.UpdateAsync<Observation>(_createdResource);
            newResources.Add(await Client.CreateAsync(Samples.GetDefaultPatient().ToPoco()));
            newResources.Add(await Client.CreateAsync(Samples.GetDefaultOrganization().ToPoco()));
            Thread.Sleep(1000);
            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("BloodGlucose").ToPoco()));
            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("BloodPressure").ToPoco()));
            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("Patient-f001").ToPoco()));
            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("Condition-For-Patient-f001").ToPoco()));

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));

            // Query all the recent changes
            FhirResponse<Bundle> allChanges = await Client.SearchAsync("_history?_since=" + sinceUriString);

            Assert.Equal(7, allChanges.Resource.Entry.Count);

            // now choose a value of before that is as close as possible to one of the last updated times
            var lastUpdatedTimes = allChanges.Resource.Entry.Select(e => e.Resource.Meta.LastUpdated).OrderBy(d => d.Value);

            var before = lastUpdatedTimes.ToList()[4];
            var beforeUriString = HttpUtility.UrlEncode(before.Value.ToString("o"));
            Thread.Sleep(500);
            var firstSet = await Client.SearchAsync("_history?_since=" + sinceUriString + "&_before=" + beforeUriString);

            Assert.Equal(4, firstSet.Resource.Entry.Count);

            sinceUriString = beforeUriString;
            before = DateTime.UtcNow;
            beforeUriString = HttpUtility.UrlEncode(before.Value.ToString("o"));
            Thread.Sleep(500); // wait 500 milliseconds to make sure that the value passed to the server for _before is not a time in the future
            var secondSet = await Client.SearchAsync("_history?_since=" + sinceUriString + "&_before=" + beforeUriString);

            Assert.Equal(3, secondSet.Resource.Entry.Count);

            foreach (var r in newResources)
            {
                await Client.DeleteAsync(r);
            }
        }

        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)] // History tests are unstable at the moment due to Cosmos DB issue with continuation tokens
        [Fact]
        public async Task WhenGettingSystemHistory_GivenAQueryThatReturnsMoreThan10Results_TheServerShouldBatchTheResponse()
        {
            var since = await GetStartTimeForHistoryTest();

            var newResources = new List<Resource>();

            // First make 11 edits
            _createdResource.Resource.Text = new Narrative { Div = "<div>Changed by E2E test</div>" };
            await Client.UpdateAsync<Observation>(_createdResource);
            newResources.Add(await Client.CreateAsync(Samples.GetDefaultPatient().ToPoco()));
            newResources.Add(await Client.CreateAsync(Samples.GetDefaultOrganization().ToPoco()));
            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("BloodGlucose").ToPoco()));
            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("BloodPressure").ToPoco()));
            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("Patient-f001").ToPoco()));
            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("Condition-For-Patient-f001").ToPoco()));
            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("Encounter-For-Patient-f001").ToPoco()));
            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("Observation-For-Patient-f001").ToPoco()));
            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("ObservationWith1MinuteApgarScore").ToPoco()));
            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("ObservationWith20MinuteApgarScore").ToPoco()));

            var lastUpdatedTimes = newResources.Select(e => e.Meta.LastUpdated).OrderBy(d => d.Value);
            var before = lastUpdatedTimes.Last().Value.AddMilliseconds(100);

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
            var beforeUriString = HttpUtility.UrlEncode(before.ToString("o"));

            FhirResponse<Bundle> readResponse = await Client.SearchAsync("_history?_since=" + sinceUriString);

            Assert.Equal(10, readResponse.Resource.Entry.Count);

            Thread.Sleep(500);

            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("ObservationWithBloodPressure").ToPoco()));
            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("ObservationWithEyeColor").ToPoco()));

            Thread.Sleep(500);

            FhirResponse<Bundle> firstBatch = await Client.SearchAsync("_history?_since=" + sinceUriString + "&_before=" + beforeUriString);

            Assert.Equal(10, firstBatch.Resource.Entry.Count);

            var secondBatch = await Client.SearchAsync(firstBatch.Resource.NextLink.ToString());

            Assert.Single(secondBatch.Resource.Entry);

            foreach (var r in newResources)
            {
                await Client.DeleteAsync(r);
            }
        }

        [Fact]
        public async Task WhenGettingSystemHistory_GivenAValueForSinceAfterAllModificatons_TheServerShouldReturnAnEmptyResult()
        {
            _createdResource.Resource.Text = new Narrative { Div = "<div>Changed by E2E test</div>" };
            var updatedResource = await Client.UpdateAsync<Observation>(_createdResource);

            // ensure that the server has fully processed the PUT
            var since = await GetStartTimeForHistoryTest();

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));

            FhirResponse<Bundle> readResponse = await Client.SearchAsync("_history?_since=" + sinceUriString);

            Assert.Empty(readResponse.Resource.Entry);
        }

        [Fact]
        public async Task WhenGettingSystemHistory_GivenAValueForSinceAndBeforeWithNoModifications_TheServerShouldReturnAnEmptyResult()
        {
            _createdResource.Resource.Text = new Narrative { Div = "<div>Changed by E2E test</div>" };
            var updatedResource = await Client.UpdateAsync<Observation>(_createdResource);

            // ensure that the server has fully processed the PUT
            var since = await GetStartTimeForHistoryTest();
            var before = updatedResource.Resource.Meta.LastUpdated.Value.AddMilliseconds(100);

            Thread.Sleep(500);

            var newPatient = await Client.CreateAsync(Samples.GetDefaultPatient().ToPoco());

            Assert.True(before < newPatient.Resource.Meta.LastUpdated.Value);

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
            var beforeUriString = HttpUtility.UrlEncode(before.ToString("o"));

            FhirResponse<Bundle> readResponse = await Client.SearchAsync("_history?_since=" + sinceUriString + "&_before=" + beforeUriString);

            Assert.Empty(readResponse.Resource.Entry);

            if (newPatient?.Resource != null)
            {
                await Client.DeleteAsync(newPatient.Resource);
            }
        }

        [Fact]
        public async Task WhenGettingSystemHistory_GivenAValueForBeforeInTheFuture_AnErrorIsReturned()
        {
            var before = DateTime.UtcNow.AddSeconds(300);
            var beforeUriString = HttpUtility.UrlEncode(before.ToString("o"));
            var ex = await Assert.ThrowsAsync<FhirException>(() => Client.SearchAsync("_history?_before=" + beforeUriString));

            Assert.Contains("Parameter _before cannot a be a value in the future", ex.Message);
        }

        /// <summary>
        /// Find a time to use _since where there have been no results in history
        /// so we can start from clean start point
        /// </summary>
        /// <returns>DateIimeOffset set to a good value for _since</returns>
        private async Task<DateTimeOffset> GetStartTimeForHistoryTest()
        {
            FhirResponse<Resource> response = await Client.CreateAsync(Samples.GetDefaultPatient().ToPoco());
            await Task.Delay(10);
            return response.Resource.Meta.LastUpdated.Value.AddMilliseconds(1);
        }

        public void Dispose()
        {
            if (_createdResource?.Resource != null)
            {
                Client.DeleteAsync(_createdResource.Resource).GetAwaiter().GetResult();
            }
        }
    }
}
