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
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.All, FhirVersion.All)]
    [CollectionDefinition("History", DisableParallelization = true)]
    [Collection("History")]
    public class HistoryTests : IClassFixture<HttpIntegrationTestFixture>, IDisposable
    {
        private FhirResponse<ResourceElement> _createdResource;

        public HistoryTests(HttpIntegrationTestFixture fixture)
        {
            Client = fixture.FhirClient;

            _createdResource = Client.CreateAsync(Client.GetDefaultObservation()).GetAwaiter().GetResult();
        }

        protected ICustomFhirClient Client { get; set; }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenGettingResourceHistory_GivenAType_TheServerShouldReturnTheAppropriateBundleSuccessfully()
        {
            FhirResponse<ResourceElement> readResponse = await Client.SearchAsync("Observation/_history");

            Assert.NotEmpty(readResponse.Resource.Select("Resource.entry.resource"));
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenGettingResourceHistory_GivenATypeAndId_TheServerShouldReturnTheAppropriateBundleSuccessfully()
        {
            FhirResponse<ResourceElement> readResponse = await Client.SearchAsync($"Observation/{_createdResource.Resource.Id}/_history");

            Assert.NotEmpty(readResponse.Resource.Select("Resource.entry.resource"));
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenGettingSystemHistory_GivenNoType_TheServerShouldReturnTheAppropriateBundleSuccessfully()
        {
            FhirResponse<ResourceElement> readResponse = await Client.SearchAsync("_history");

            Assert.NotEmpty(readResponse.Resource.Select("Resource.entry.resource"));
        }

        [Fact]
        public async Task WhenGettingSystemHistory_GivenAValueForSince_TheServerShouldReturnOnlyRecordsModifiedAfterSinceValue()
        {
            DateTimeOffset since = await GetStartTimeForHistoryTest();
            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));

            Thread.Sleep(500);  // put a small gap between since and the first edits

            var updatedResourceElement = Client.UpdateText(_createdResource.Resource, "Changed by E2E test");

            var updatedResponse = await Client.UpdateAsync(updatedResourceElement);

            FhirResponse<ResourceElement> readResponse = await Client.SearchAsync("_history?_since=" + sinceUriString);

            var entries = readResponse.Resource.Select("Resource.entry.resource").ToList();
            ValidateEntry(entries);

            FhirResponse<ResourceElement> selfLinkResponse = Client.SearchAsync(readResponse.Resource.Scalar<string>("Resource.link.where(relation = 'self').url")).Result;

            var selfLinkEntries = selfLinkResponse.Resource.Select("Resource.entry.resource").ToList();
            ValidateEntry(selfLinkEntries);

            void ValidateEntry(List<ITypedElement> collection)
            {
                Assert.Single(collection);
                var obsHistory = collection.First().ToResourceElement();

                Assert.NotNull(collection);
                Assert.Contains("Changed by E2E test", obsHistory.Scalar<string>("text.div"));
            }
        }

        [Fact(Skip = "History tests are unstable at the moment due to Cosmos DB issue with continuation tokens")]
        public async Task WhenGettingSystemHistory_GivenAValueForSinceAndBeforeWithModifications_TheServerShouldOnlyCorrectResources()
        {
            var since = await GetStartTimeForHistoryTest();

            Thread.Sleep(500);  // put a small gap between since and the first edits

            var updatedResourceElement = Client.UpdateText(_createdResource.Resource, "Changed by E2E test");
            await Client.UpdateAsync<ResourceElement>(updatedResourceElement);

            ResourceElement newPatient = (await Client.CreateAsync<ResourceElement>(Client.GetDefaultPatient())).Resource;

            var before = newPatient.LastUpdated.Value.AddMilliseconds(100);
            Thread.Sleep(500);  // make sure that the before time is not in the future

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
            var beforeUriString = HttpUtility.UrlEncode(before.ToString("o"));

            FhirResponse<ResourceElement> readResponse = await Client.SearchAsync("_history?_since=" + sinceUriString + "&_before=" + beforeUriString);

            var entries = readResponse.Resource.Select("Resource.entry.resource").ToList();
            Assert.Equal(2, entries.Count);

            ResourceElement patientHistory = entries.First(e => e.InstanceType == "Patient").ToResourceElement();
            ResourceElement obsHistory = entries.First(e => e.InstanceType == "Observation").ToResourceElement();

            Assert.NotNull(obsHistory);
            Assert.NotNull(patientHistory);
            Assert.Contains("Changed by E2E test", obsHistory.Scalar<string>("text.div"));
            Assert.Equal(newPatient.Id, patientHistory.Id);

            await Client.DeleteAsync(newPatient);
        }

        [Fact(Skip = "History tests are unstable at the moment due to Cosmos DB issue with continuation tokens")]
        public async Task WhenGettingSystemHistory_GivenAValueForSinceAndBeforeCloseToLastModifiedTime_TheServerShouldNotMissRecords()
        {
            var since = await GetStartTimeForHistoryTest();

            var newResources = new List<ResourceElement>();

            Thread.Sleep(500);  // put a small gap between since and the first edits

            // First make a few edits
            var updatedResource = Client.UpdateText(_createdResource.Resource, "Changed by E2E test");
            await Client.UpdateAsync(updatedResource);
            newResources.Add((await Client.CreateAsync(Client.GetDefaultPatient())).Resource);
            newResources.Add((await Client.CreateAsync(Client.GetDefaultOrganization())).Resource);
            Thread.Sleep(1000);
            newResources.Add((await Client.CreateAsync(Client.GetJsonSample("BloodGlucose"))).Resource);
            newResources.Add((await Client.CreateAsync(Client.GetJsonSample("BloodPressure"))).Resource);
            newResources.Add((await Client.CreateAsync(Client.GetJsonSample("Patient-f001"))).Resource);
            newResources.Add((await Client.CreateAsync(Client.GetJsonSample("Condition-For-Patient-f001"))).Resource);

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));

            // Query all the recent changes
            FhirResponse<ResourceElement> allChanges = await Client.SearchAsync("_history?_since=" + sinceUriString);

            var entries = allChanges.Resource.Select("Resource.entry.resource").ToList();

            Assert.Equal(7, entries.Count);

            // now choose a value of before that is as close as possible to one of the last updated times
            var lastUpdatedTimes = entries.Select(e => e.ToResourceElement().LastUpdated).OrderBy(d => d.Value);

            var before = lastUpdatedTimes.ToList()[4];
            var beforeUriString = HttpUtility.UrlEncode(before.Value.ToString("o"));
            Thread.Sleep(500);
            var firstSet = await Client.SearchAsync("_history?_since=" + sinceUriString + "&_before=" + beforeUriString);
            var firstSetEntries = firstSet.Resource.Select("Resource.entry.resource").ToList();

            Assert.Equal(4, firstSetEntries.Count);

            sinceUriString = beforeUriString;
            before = DateTime.UtcNow;
            beforeUriString = HttpUtility.UrlEncode(before.Value.ToString("o"));
            Thread.Sleep(500); // wait 500 milliseconds to make sure that the value passed to the server for _before is not a time in the future
            var secondSet = await Client.SearchAsync("_history?_since=" + sinceUriString + "&_before=" + beforeUriString);
            var fsecondSetEntries = secondSet.Resource.Select("Resource.entry.resource").ToList();

            Assert.Equal(3, fsecondSetEntries.Count);

            foreach (var r in newResources)
            {
                await Client.DeleteAsync(r);
            }
        }

        [Fact(Skip = "History tests are unstable at the moment due to Cosmos DB issue with continuation tokens")]
        public async Task WhenGettingSystemHistory_GivenAQueryThatReturnsMoreThan10Results_TheServerShouldBatchTheResponse()
        {
            // The batch test does not work reliably on local Cosmos DB Emulator
            // Skip the test if this is local
            // There is no remote FHIR server. Skip test
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TestEnvironmentUrl")))
            {
                return;
            }

            var since = await GetStartTimeForHistoryTest();

            Thread.Sleep(500);  // put a small gap between since and the first edits

            var newResources = new List<ResourceElement>();

            // First make 11 edits
            var updatedResource = Client.UpdateText(_createdResource.Resource, "Changed by E2E test");
            await Client.UpdateAsync(updatedResource);
            newResources.Add((await Client.CreateAsync(Client.GetDefaultPatient())).Resource);
            newResources.Add((await Client.CreateAsync(Client.GetDefaultOrganization())).Resource);
            newResources.Add((await Client.CreateAsync(Client.GetJsonSample("BloodGlucose"))).Resource);
            newResources.Add((await Client.CreateAsync(Client.GetJsonSample("BloodPressure"))).Resource);
            newResources.Add((await Client.CreateAsync(Client.GetJsonSample("Patient-f001"))).Resource);
            newResources.Add((await Client.CreateAsync(Client.GetJsonSample("Condition-For-Patient-f001"))).Resource);
            newResources.Add((await Client.CreateAsync(Client.GetJsonSample("Encounter-For-Patient-f001"))).Resource);
            newResources.Add((await Client.CreateAsync(Client.GetJsonSample("Observation-For-Patient-f001"))).Resource);
            newResources.Add((await Client.CreateAsync(Client.GetJsonSample("ObservationWith1MinuteApgarScore"))).Resource);
            newResources.Add((await Client.CreateAsync(Client.GetJsonSample("ObservationWith20MinuteApgarScore"))).Resource);

            var lastUpdatedTimes = newResources.Select(e => e.LastUpdated).OrderBy(d => d.Value);
            var before = lastUpdatedTimes.Last().Value.AddMilliseconds(100);

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
            var beforeUriString = HttpUtility.UrlEncode(before.ToString("o"));

            FhirResponse<ResourceElement> readResponse = await Client.SearchAsync("_history?_since=" + sinceUriString);
            var readEntries = readResponse.Resource.Select("Resource.entry.resource");

            Assert.Equal(10, readEntries.Count());

            Thread.Sleep(500);

            newResources.Add((await Client.CreateAsync(Client.GetJsonSample("ObservationWithBloodPressure"))).Resource);
            newResources.Add((await Client.CreateAsync(Client.GetJsonSample("ObservationWithEyeColor"))).Resource);

            Thread.Sleep(500);

            FhirResponse<ResourceElement> firstBatch = await Client.SearchAsync("_history?_since=" + sinceUriString + "&_before=" + beforeUriString);
            var firstBatchEntries = firstBatch.Resource.Select("Resource.entry.resource");

            Assert.Equal(10, firstBatchEntries.Count());

            var secondBatch = await Client.SearchAsync(firstBatch.Resource.Scalar<string>("Resource.link.where(relation = 'self').url"));

            Assert.Single(secondBatch.Resource.Select("Resource.entry.resource"));

            foreach (var r in newResources)
            {
                Client.DeleteAsync(r).GetAwaiter().GetResult();
            }
        }

        [Fact]
        public async Task WhenGettingSystemHistory_GivenAValueForSinceAfterAllModifications_TheServerShouldReturnAnEmptyResult()
        {
            var updatedResourceElement = Client.UpdateText(_createdResource.Resource, "Changed by E2E test");

            var updatedResource = await Client.UpdateAsync(updatedResourceElement);

            // ensure that the server has fully processed the PUT
            var since = await GetStartTimeForHistoryTest();

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));

            FhirResponse<ResourceElement> readResponse = await Client.SearchAsync("_history?_since=" + sinceUriString);

            Assert.Empty(readResponse.Resource.Select("Resource.entry.resource"));
        }

        [Fact]
        public async Task WhenGettingSystemHistory_GivenAValueForSinceAndBeforeWithNoModifications_TheServerShouldReturnAnEmptyResult()
        {
            var updatedResourceElement = Client.UpdateText(_createdResource.Resource, "Changed by E2E test");
            var updatedResource = await Client.UpdateAsync(updatedResourceElement);

            // ensure that the server has fully processed the PUT
            var since = await GetStartTimeForHistoryTest();
            var before = updatedResource.Resource.LastUpdated.Value.AddMilliseconds(100);

            Thread.Sleep(500);

            var newPatient = await Client.CreateAsync(Client.GetDefaultPatient());

            Assert.True(before < newPatient.Resource.LastUpdated.Value);

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
            var beforeUriString = HttpUtility.UrlEncode(before.ToString("o"));

            FhirResponse<ResourceElement> readResponse = await Client.SearchAsync("_history?_since=" + sinceUriString + "&_before=" + beforeUriString);

            Assert.Empty(readResponse.Resource.Select("Resource.entry.resource"));

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

            Assert.Contains("Parameter _before cannot be a value in the future", ex.Message);
        }

        /// <summary>
        /// Find a time to use _since where there have been no results in history
        /// so we can start from clean start point
        /// </summary>
        /// <returns>DateIimeOffset set to a good value for _since</returns>
        private async Task<DateTimeOffset> GetStartTimeForHistoryTest()
        {
            Thread.Sleep(500);
            var since = DateTime.UtcNow;

            for (int i = 0; i < 10; i++)
            {
                var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
                FhirResponse<ResourceElement> readResponse = await Client.SearchAsync("_history?_since=" + sinceUriString);

                if (!readResponse.Resource.Select("Resource.entry.resource").Any())
                {
                    break;
                }

                Thread.Sleep(1000);
                since = DateTime.UtcNow;
            }

            return since;
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
