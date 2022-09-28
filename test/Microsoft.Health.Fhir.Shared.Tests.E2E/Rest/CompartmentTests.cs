// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    [Trait(Traits.Category, Categories.Search)]
    [Trait(Traits.Category, Categories.CompartmentSearch)]
    public class CompartmentTests : SearchTestsBase<CompartmentTestFixture>
    {
        public CompartmentTests(CompartmentTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenADeviceCompartment_WhenRetrievingDeviceComponents_ThenOnlyResourcesMatchingCompartmentShouldBeReturned()
        {
            string searchUrl = $"Device/{Fixture.Device.Id}/Observation";

            Bundle bundle = await Client.SearchAsync(searchUrl);
            ValidateBundle(bundle, searchUrl, Fixture.Observation);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatientCompartment_WhenRetrievingObservation_ThenOnlyResourcesMatchingCompartmentShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/Observation";

            Bundle bundle = await Client.SearchAsync(searchUrl);
            ValidateBundle(bundle, searchUrl, Fixture.Observation);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatientCompartment_WhenFilteringObservations_ThenOnlyResourcesMatchingCompartmentShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/*?_type=Observation";

            Bundle bundle = await Client.SearchAsync(searchUrl);
            ValidateBundle(bundle, searchUrl, Fixture.Observation);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatientCompartment_WhenRetrievingResourcesWithAWildCard_ThenAllResourcesMatchingCompartmentShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/*";

            Bundle bundle = await Client.SearchAsync(searchUrl);
            ValidateBundle(bundle, searchUrl, Fixture.Observation, Fixture.Encounter, Fixture.Condition);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatientCompartment_WhenRetrievingResourcesWithAWildCardWithSearchByType_ThenAllResourcesMatchingCompartmentAndTypeSearchedShouldBeReturned()
        {
            string observationSearchUrl = $"Patient/{Fixture.Patient.Id}/*?_type=Observation";

            Bundle bundle = await Client.SearchAsync(observationSearchUrl);
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

            Bundle bundle = await Client.SearchAsync(searchUrl);

            string[] expectedDiagnostics = { string.Format(Core.Resources.InvalidTypeParameter, "'foo'") };
            OperationOutcome.IssueType[] expectedCodeTypes = { OperationOutcome.IssueType.NotSupported };
            OperationOutcome.IssueSeverity[] expectedIssueSeverities = { OperationOutcome.IssueSeverity.Warning };
            var outcome = GetAndValidateOperationOutcome(bundle);
            ValidateOperationOutcome(expectedDiagnostics, expectedIssueSeverities, expectedCodeTypes, outcome);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatientCompartment_WhenSearchingForAResourcesWithAWildCardWithSearchWithNoMatchingValue_ThenNoResourcesMatchingCompartmentSearchedShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/foo";

            FhirException exception = await Assert.ThrowsAsync<FhirException>(async () => await Client.SearchAsync(searchUrl));
            Assert.Equal(HttpStatusCode.NotFound, exception.Response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatientCompartment_WhenFilteringResourcesWithAMatchingValue_ThenResourcesMatchingCompartmentSearchedShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/*?_type=Observation&performer=Practitioner/f005";

            Bundle bundle = await Client.SearchAsync(searchUrl);
            ValidateBundle(bundle, searchUrl, Fixture.Observation);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatientCompartment_WhenNotFilteringResourcesWithAMatchingValue_ThenNoResourcesMatchingCompartmentSearchedShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/*?_type=Observation&performer=Practitioner/f2112";

            Bundle bundle = await Client.SearchAsync(searchUrl);
            Assert.Empty(bundle.Entry);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatientCompartment_WhenSearchingResourcesWithAMatchingValue_ThenResourcesMatchingCompartmentSearchedShouldBeReturned()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/Observation?performer=Practitioner/f005";

            Bundle bundle = await Client.SearchAsync(searchUrl);
            ValidateBundle(bundle, searchUrl, Fixture.Observation);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAPatientCompartment_WhenSearchingForAResourceTypeUsingDifferentWays_ThenResourcesShouldBeTheSame()
        {
            string searchUrl1 = $"Patient/{Fixture.Patient.Id}/*?_type=Observation&performer=Practitioner/f005";
            Bundle bundle1 = await Client.SearchAsync(searchUrl1);
            ValidateBundle(bundle1, searchUrl1, Fixture.Observation);

            string searchUrl2 = $"Patient/{Fixture.Patient.Id}/Observation?performer=Practitioner/f005";
            Bundle bundle2 = await Client.SearchAsync(searchUrl2);
            ValidateBundle(bundle2, searchUrl2, Fixture.Observation);

            Assert.Equal(bundle1.Entry.Count, bundle2.Entry.Count);

            for (int i = 0; i < bundle1.Entry.Count; i++)
            {
                Assert.Equal(bundle1.Entry[i].FullUrl, bundle2.Entry[i].FullUrl);
                Assert.Equal(bundle1.Entry[i].FullUrlElement, bundle2.Entry[i].FullUrlElement);
            }
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenMoreSearchResultsThanCount_WhenSearchingAPatientCompartment_ThenNextLinkShouldBePopulated()
        {
            string searchUrl = $"Patient/{Fixture.Patient.Id}/*?_count=1";
            string baseUrl = Fixture.GenerateFullUrl(searchUrl);

            var results = new Bundle();

            int loop = 1;

            while (searchUrl != null)
            {
                // There should not be more than 3 loops.
                Assert.True(loop <= 3);

                Bundle bundle = await Client.SearchAsync(searchUrl);

                searchUrl = bundle.NextLink?.ToString();

                if (searchUrl != null)
                {
                    Assert.StartsWith(baseUrl, searchUrl);
                }

                results.Entry.AddRange(bundle.Entry);
            }

            ValidateBundle(results, Fixture.Observation, Fixture.Encounter, Fixture.Condition);
        }
    }
}
