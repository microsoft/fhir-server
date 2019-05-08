// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Web;
using Hl7.Fhir.Model;
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
    public class HistoryTests : IClassFixture<HttpIntegrationTestFixture<Startup>>, IDisposable
    {
        private FhirResponse<Observation> _createdResource;

        public HistoryTests(HttpIntegrationTestFixture<Startup> fixture)
        {
            Client = fixture.FhirClient;

            _createdResource = Client.CreateAsync(Samples.GetDefaultObservation()).GetAwaiter().GetResult();
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
            Thread.Sleep(2000);
            var since = DateTime.UtcNow;
            Thread.Sleep(1000);
            _createdResource.Resource.Comment = "Changed by E2E test";

            var updatedResource = Client.UpdateAsync<Observation>(_createdResource).GetAwaiter().GetResult();

            Thread.Sleep(3000); // ensure that the server has fully processed the PUT

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));

            FhirResponse<Bundle> readResponse = await Client.SearchAsync("_history?_since=" + sinceUriString);

            var obsHistory = readResponse.Resource.Entry[0].Resource as Observation;

            Assert.NotNull(obsHistory);
            Assert.Contains("Changed by E2E test", obsHistory.Comment);

            // Validate selflink with added _before parameter
            Assert.Contains("_before", readResponse.Resource.SelfLink.Query);
            FhirResponse<Bundle> selfLinkResponse = await Client.SearchAsync(readResponse.Resource.SelfLink.ToString());

            obsHistory = selfLinkResponse.Resource.Entry[0].Resource as Observation;

            Assert.NotNull(obsHistory);
            Assert.Contains("Changed by E2E test", obsHistory.Comment);
        }

        [Fact]
        public async Task WhenGettingSystemHistory_GivenAValueForSinceAndBeforeWithModifications_TheServerShouldOnlyCorrectResources()
        {
            Thread.Sleep(2000);
            var since = DateTime.UtcNow;

            _createdResource.Resource.Comment = "Changed by E2E test";
            var updatedResource = Client.UpdateAsync<Observation>(_createdResource).GetAwaiter().GetResult();
            var newPatient = Client.CreateAsync(Samples.GetDefaultPatient()).GetAwaiter().GetResult();

            Thread.Sleep(5000); // ensure that the server has fully processed the edits
            var before = DateTime.UtcNow;

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
            Assert.Contains("Changed by E2E test", obsHistory.Comment);
            Assert.Equal(newPatient.Resource.Id, patientHistory.Id);

            if (newPatient?.Resource != null)
            {
                Client.DeleteAsync(newPatient.Resource).GetAwaiter().GetResult();
            }
        }

        [Fact]
        public async Task WhenGettingSystemHistory_GivenAQueryThatReturnsMoreThan10Results_TheServerShouldBatchTheResponse()
        {
            Thread.Sleep(2000);
            var since = DateTime.UtcNow;

            var newResources = new List<Resource>();

            // First make 11 edits
            _createdResource.Resource.Comment = "Changed by E2E test";
            await Client.UpdateAsync<Observation>(_createdResource);
            newResources.Add(await Client.CreateAsync(Samples.GetDefaultPatient()));
            newResources.Add(await Client.CreateAsync(Samples.GetDefaultOrganization()));
            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("BloodGlucose") as Observation));
            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("BloodPressure") as Observation));
            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("Patient-f001") as Patient));
            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("Condition-For-Patient-f001") as Condition));
            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("Encounter-For-Patient-f001") as Encounter));
            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("Observation-For-Patient-f001") as Observation));
            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("ObservationWith1MinuteApgarScore") as Observation));
            newResources.Add(await Client.CreateAsync(Samples.GetJsonSample("ObservationWith20MinuteApgarScore") as Observation));

            Thread.Sleep(5000); // ensure that the server has fully processed the edits
            var before = DateTime.UtcNow;

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
            var beforeUriString = HttpUtility.UrlEncode(before.ToString("o"));

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
        public async Task WhenGettingSystemHistory_GivenAValueForSinceAfterAllModificatons_TheServerShouldReturnAnEmptyResult()
        {
            _createdResource.Resource.Comment = "Changed by E2E test";

            var updatedResource = Client.UpdateAsync<Observation>(_createdResource).GetAwaiter().GetResult();

            Thread.Sleep(4000); // ensure that the server has fully processed the PUT
            var since = DateTime.UtcNow;

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));

            FhirResponse<Bundle> readResponse = await Client.SearchAsync("_history?_since=" + sinceUriString);

            Assert.Empty(readResponse.Resource.Entry);
        }

        [Fact]
        public async Task WhenGettingSystemHistory_GivenAValueForSinceAndBeforeWithNoModifications_TheServerShouldReturnAnEmptyResult()
        {
            Thread.Sleep(2000);
            _createdResource.Resource.Comment = "Changed by E2E test";

            Thread.Sleep(4000); // ensure that the server has fully processed the PUT
            var since = DateTime.UtcNow;
            Thread.Sleep(5000);
            var before = DateTime.UtcNow;

            Thread.Sleep(1000); // ensure that the server has fully processed the PUT

            var newPatient = Client.CreateAsync(Samples.GetDefaultPatient()).GetAwaiter().GetResult();

            Thread.Sleep(4000); // ensure that the server has fully processed the POST

            var sinceUriString = HttpUtility.UrlEncode(since.ToString("o"));
            var beforeUriString = HttpUtility.UrlEncode(before.ToString("o"));

            FhirResponse<Bundle> readResponse = await Client.SearchAsync("_history?_since=" + sinceUriString + "&_before=" + beforeUriString);

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

        public void Dispose()
        {
            if (_createdResource?.Resource != null)
            {
                Client.DeleteAsync(_createdResource.Resource).GetAwaiter().GetResult();
            }
        }
    }
}
