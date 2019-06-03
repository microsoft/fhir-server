// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json, FhirVersion.All)]
    public class CompartmentTests : SearchTestsBase<CompartmentTestFixture>
    {
        public CompartmentTests(CompartmentTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenADeviceCompartment_WhenRetrievingObservations_ThenOnlyResourcesMatchingCompartmentShouldBeReturned()
        {
            string searchUrl = $"Device/{Fixture.Device.Id}/Observation";

            ResourceElement bundle = await Client.SearchAsync(searchUrl);
            ValidateBundle(bundle, searchUrl, Fixture.Observation);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatientCompartment_WhenRetrievingObservation_ThenOnlyResourcesMatchingCompartmentShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/Observation";

            ResourceElement bundle = await Client.SearchAsync(searchUrl);
            ValidateBundle(bundle, searchUrl, Fixture.Observation);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatientCompartment_WhenRetrievingResourcesWithAWildCard_ThenAllResourcesMatchingCompartmentShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/*";

            ResourceElement bundle = await Client.SearchAsync(searchUrl);
            ValidateBundle(bundle, searchUrl, Fixture.Observation, Fixture.Encounter, Fixture.Condition);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatientCompartment_WhenRetrievingResourcesWithAWildCardWithSearchByType_ThenAllResourcesMatchingCompartmentAndTypeSearchedShouldBeReturned()
        {
            string observationSearchUrl = $"Patient/{Fixture.Patient.Id}/*?_type=Observation";

            ResourceElement bundle = await Client.SearchAsync(observationSearchUrl);
            ValidateBundle(bundle, observationSearchUrl, Fixture.Observation);

            string patientSearchUrl = $"Patient/{Fixture.Patient.Id}/*?_type=Encounter";

            bundle = await Client.SearchAsync(patientSearchUrl);
            ValidateBundle(bundle, patientSearchUrl, Fixture.Encounter);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatientCompartment_WhenRetrievingResourcesWithAWildCardWithSearchWithNoMatchingValue_ThenNoResourcesMatchingCompartmentSearchedShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/*?_type=foo";

            ResourceElement bundle = await Client.SearchAsync(searchUrl);
            ValidateBundle(bundle, searchUrl);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatientCompartment_WhenSearchingResourcesWithAMatchingValue_ThenResourcesMatchingCompartmentSearchedShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/Observation?performer=Practitioner/f005";

            ResourceElement bundle = await Client.SearchAsync(searchUrl);
            ValidateBundle(bundle, searchUrl, Fixture.Observation);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenMoreSearchResultsThanCount_WhenSearchingAPatientCompartment_ThenNextLinkShouldBePopulated()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/*?_count=1";
            string baseUrl = Fixture.GenerateFullUrl(searchUrl);

            var results = new List<ResourceElement>();

            int loop = 1;

            while (searchUrl != null)
            {
                // There should not be more than 3 loops.
                Assert.True(loop <= 3);

                ResourceElement bundle = await Client.SearchAsync(searchUrl);

                searchUrl = bundle.Scalar<string>("Resource.link.where(relation = 'next').url");

                if (searchUrl != null)
                {
                    Assert.StartsWith(baseUrl, searchUrl);
                }

                var entries = bundle.Select("Resource.entry.resource");
                results.AddRange(entries.Select(e => e.ToResourceElement()));
            }

            ValidateBundle(results, Fixture.Observation, Fixture.Encounter, Fixture.Condition);
        }
    }
}
