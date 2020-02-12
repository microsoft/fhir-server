// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class SearchProfileTests : IClassFixture<HttpIntegrationTestFixture>
    {
        public SearchProfileTests(HttpIntegrationTestFixture fixture)
        {
            Client = fixture.FhirClient;
        }

        protected FhirClient Client { get; set; }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenSearchingForAResourceWithProfile_GivenAnR4Server_TheServerShouldDropTheParamFromTheSelfLink()
        {
            string profile = $"https://e2e-test-profile|{Guid.NewGuid()}";

            Observation observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Meta = new Meta();
            observation.Meta.Profile = new[] { profile };
            await Client.CreateAsync(observation);

            FhirResponse<Bundle> searchResult = await Client.SearchAsync(ResourceType.Observation, "_profile=" + profile);

            Assert.DoesNotContain("_profile", searchResult.Resource.SelfLink.ToString());
        }
    }
}
