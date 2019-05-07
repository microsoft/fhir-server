// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
