// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Web;
using Xunit;
using FhirClient = Microsoft.Health.Fhir.Tests.E2E.Common.FhirClient;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public abstract class HistoryTests<TFixture> : IClassFixture<TFixture>, IAsyncLifetime
        where TFixture : HttpIntegrationTestFixture<Startup>
    {
        private FhirResponse<Observation> _createdResource;

        protected HistoryTests(TFixture fixture)
        {
            Client = fixture.FhirClient;
        }

        protected FhirClient Client { get; }

        public async Task InitializeAsync()
        {
            _createdResource = await Client.CreateAsync(Samples.GetDefaultObservation());
        }

        public async Task DisposeAsync()
        {
            if (_createdResource?.Resource != null)
            {
                await Client.DeleteAsync(_createdResource.Resource);
            }
        }

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
    }
}
