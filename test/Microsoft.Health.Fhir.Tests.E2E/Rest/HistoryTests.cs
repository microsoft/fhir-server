﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Web;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Web;
using Xunit;
using FhirClient = Microsoft.Health.Fhir.Tests.E2E.Common.FhirClient;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json | Format.Xml)]
    [CollectionDefinition("History", DisableParallelization=true)]
    [Collection("History")]
    public class HistoryTests : IClassFixture<HttpIntegrationTestFixture<Startup>>, IDisposable
    {
        private FhirResponse<Observation> _createdResource;

        public HistoryTests(HttpIntegrationTestFixture<Startup> fixture)
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
        public void WhenGettingSystemHistory_GivenAValueForSince_TheServerShouldReturnOnlyRecordsModifiedAfterSinceValue()
        {
            var since = GetStartTimeForHistoryTest();
            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
            Thread.Sleep(1000);
            _createdResource.Resource.Comment = "Changed by E2E test";

            var updatedResource = Client.UpdateAsync<Observation>(_createdResource).GetAwaiter().GetResult();

            FhirResponse<Bundle> readResponse = Client.SearchAsync("_history?_since=" + sinceUriString).Result;

            Assert.Single(readResponse.Resource.Entry);

            var obsHistory = readResponse.Resource.Entry[0].Resource as Observation;

            Assert.NotNull(obsHistory);
            Assert.Contains("Changed by E2E test", obsHistory.Comment);

            FhirResponse<Bundle> selfLinkResponse = Client.SearchAsync(readResponse.Resource.SelfLink.ToString()).Result;

            Assert.Single(selfLinkResponse.Resource.Entry);

            obsHistory = selfLinkResponse.Resource.Entry[0].Resource as Observation;

            Assert.NotNull(obsHistory);
            Assert.Contains("Changed by E2E test", obsHistory.Comment);
        }

        [Fact]
        public void WhenGettingSystemHistory_GivenAValueForSinceAndBeforeWithModifications_TheServerShouldOnlyCorrectResources()
        {
            var since = GetStartTimeForHistoryTest();

            _createdResource.Resource.Comment = "Changed by E2E test";
            Client.UpdateAsync<Observation>(_createdResource).GetAwaiter().GetResult();
            FhirResponse<Resource> newPatient = Client.CreateAsync(Samples.GetDefaultPatient().ToPoco()).GetAwaiter().GetResult();

            Thread.Sleep(500);
            var before = DateTime.UtcNow;

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
            var beforeUriString = HttpUtility.UrlEncode(before.ToString("o"));

            Thread.Sleep(1000);
            FhirResponse<Bundle> readResponse = Client.SearchAsync("_history?_since=" + sinceUriString + "&_before=" + beforeUriString).Result;

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
            Assert.Contains("Changed by E2E test", obsHistory.Comment);
            Assert.Equal(newPatient.Resource.Id, patientHistory.Id);

            if (newPatient?.Resource != null)
            {
                Client.DeleteAsync(newPatient.Resource).GetAwaiter().GetResult();
            }
        }

        [Fact]
        public async Task WhenGettingSystemHistory_GivenAValueForSinceAndBeforeCloseToLastModifiedTime_TheServerShouldNotMissRecords()
        {
            var since = GetStartTimeForHistoryTest();

            var newResources = new List<Resource>();

            // First make a few edits
            _createdResource.Resource.Comment = "Changed by E2E test";
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
            Thread.Sleep(500);
            var secondSet = await Client.SearchAsync("_history?_since=" + sinceUriString + "&_before=" + beforeUriString);

            Assert.Equal(3, secondSet.Resource.Entry.Count);

            foreach (var r in newResources)
            {
                Client.DeleteAsync(r).GetAwaiter().GetResult();
            }
        }

        [Fact]
        public async Task WhenGettingSystemHistory_GivenAQueryThatReturnsMoreThan10Results_TheServerShouldBatchTheResponse()
        {
            // The batch test does not work reliably on local Cosmos DB Emulator
            // Skip the test if this is local
            // There is no remote FHIR server. Skip test
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TestEnvironmentUrl")))
            {
                return;
            }

            var since = GetStartTimeForHistoryTest();

            var newResources = new List<Resource>();

            // First make 11 edits
            _createdResource.Resource.Comment = "Changed by E2E test";
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

            Thread.Sleep(1000); // Leave a small gap in the timestamp
            var before = DateTime.UtcNow;

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
            var beforeUriString = HttpUtility.UrlEncode(before.ToString("o"));
            bool historyHas10Entries = false;

            // wait for above values to be represented in history results
            for (int i = 0; i < 5; i++)
            {
                FhirResponse<Bundle> readResponse = Client.SearchAsync("_history?_since=" + sinceUriString).Result;

                if (readResponse.Resource.Entry.Count == 10)
                {
                    historyHas10Entries = true;
                    break;
                }

                Thread.Sleep(2000);
            }

            Assert.True(historyHas10Entries);

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
                Client.DeleteAsync(r).GetAwaiter().GetResult();
            }
        }

        [Fact]
        public void WhenGettingSystemHistory_GivenAValueForSinceAfterAllModificatons_TheServerShouldReturnAnEmptyResult()
        {
            _createdResource.Resource.Comment = "Changed by E2E test";

            var updatedResource = Client.UpdateAsync<Observation>(_createdResource).GetAwaiter().GetResult();

            // ensure that the server has fully processed the PUT
            var since = GetStartTimeForHistoryTest();

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));

            FhirResponse<Bundle> readResponse = Client.SearchAsync("_history?_since=" + sinceUriString).Result;

            Assert.Empty(readResponse.Resource.Entry);
        }

        [Fact]
        public void WhenGettingSystemHistory_GivenAValueForSinceAndBeforeWithNoModifications_TheServerShouldReturnAnEmptyResult()
        {
            Thread.Sleep(2000);
            _createdResource.Resource.Comment = "Changed by E2E test";

            // ensure that the server has fully processed the PUT
            var since = GetStartTimeForHistoryTest();
            var before = DateTime.UtcNow;

            Thread.Sleep(500);

            var newPatient = Client.CreateAsync(Samples.GetDefaultPatient().ToPoco()).GetAwaiter().GetResult();

            Thread.Sleep(500);

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
            var beforeUriString = HttpUtility.UrlEncode(before.ToString("o"));

            FhirResponse<Bundle> readResponse = Client.SearchAsync("_history?_since=" + sinceUriString + "&_before=" + beforeUriString).Result;

            Assert.Empty(readResponse.Resource.Entry);

            if (newPatient?.Resource != null)
            {
                Client.DeleteAsync(newPatient.Resource).GetAwaiter().GetResult();
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
        private DateTimeOffset GetStartTimeForHistoryTest()
        {
            Thread.Sleep(2000);
            var since = DateTime.UtcNow;

            for (int i = 0; i < 10; i++)
            {
                var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
                FhirResponse<Bundle> readResponse = Client.SearchAsync("_history?_since=" + sinceUriString).Result;

                if (readResponse.Resource.Entry.Count == 0)
                {
                    break;
                }

                Thread.Sleep(2000);
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
