// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Stu3.Tests.E2E.Rest.Search
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class SearchDurationTests : IClassFixture<HttpIntegrationTestFixture>
    {
        public SearchDurationTests(HttpIntegrationTestFixture fixture)
        {
            Client = fixture.FhirClient;
        }

        private FhirClient Client { get; set; }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenSearchingForDurationOnEncounter_GivenAnStu3Server_TheServerResultsAreReturnedCorrectly()
        {
            string tag = Guid.NewGuid().ToString();

            Encounter encounter = Samples.GetJsonSample("Encounter-For-Patient-f001").ToPoco<Encounter>();

            encounter.Meta = new Meta();
            encounter.Meta.Tag = new List<Coding> { new Coding("http://e2e-test/", tag) };
            var saved = await Client.CreateAsync(encounter);

            FhirResponse<Bundle> searchResult = await Client.SearchAsync(ResourceType.Encounter, "length=140&_tag=" + tag);
            Assert.Equal(saved.Resource.Id, searchResult.Resource.Entry.First().Resource.Id);
        }
    }
}
