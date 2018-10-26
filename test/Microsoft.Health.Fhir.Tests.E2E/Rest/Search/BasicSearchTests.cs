// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Web;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class BasicSearchTests : SearchTestsBase<HttpIntegrationTestFixture<Startup>>
    {
        public BasicSearchTests(HttpIntegrationTestFixture<Startup> fixture)
            : base(fixture)
        {
            // Delete all patients before starting the test.
            Client.DeleteAllResources(ResourceType.Patient).Wait();
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResourceWithVariousValues_WhenSearchedWithMultipleParams_ThenOnlyResourcesMatchingAllSearchParamsShouldBeReturned()
        {
            // Create various resources.
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(
                p => SetPatientInfo(p, "Seattle", "Robinson"),
                p => SetPatientInfo(p, "Portland", "Williamas"),
                p => SetPatientInfo(p, "Seattle", "Jones"));

            Bundle bundle = await Client.SearchAsync("Patient?address-city=seattle&family=Jones");

            ValidateBundle(bundle, patients[2]);

            void SetPatientInfo(Patient patient, string city, string family)
            {
                patient.Address = new List<Address>()
                {
                    new Address() { City = city },
                };

                patient.Name = new List<HumanName>()
                {
                    new HumanName() { Family = family },
                };
            }
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenVariousTypesOfResources_WhenSearchedByResourceType_ThenOnlyResourcesMatchingTheResourceTypeShouldBeReturned()
        {
            // Create various resources.
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(3);
            await Client.CreateAsync(Samples.GetDefaultObservation());
            await Client.CreateAsync(Samples.GetDefaultOrganization());

            Bundle bundle = await Client.SearchAsync("Patient");

            ValidateBundle(bundle, patients);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenVariousTypesOfResources_WhenSearchingAcrossAllResourceTypes_ThenOnlyResourcesMatchingTypeParameterShouldBeReturned()
        {
            // Create various resources.
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(3);
            Observation observation = (await Client.CreateAsync(Samples.GetDefaultObservation())).Resource;
            Organization organization = (await Client.CreateAsync(Samples.GetDefaultOrganization())).Resource;

            Bundle bundle = await Client.SearchAsync("?_type=Patient");
            ValidateBundle(bundle, patients);
            bundle = await Client.SearchPostAsync(null, ("_type", "Patient"));
            ValidateBundle(bundle, patients);

            bundle = await Client.SearchAsync("?_type=Observation,Patient");
            Assert.True(bundle.Entry.Count > patients.Length);
            bundle = await Client.SearchPostAsync(null, ("_type", "Patient,Observation"));
            Assert.True(bundle.Entry.Count > patients.Length);

            bundle = await Client.SearchAsync($"?_type=Observation,Patient&_id={observation.Id}");
            ValidateBundle(bundle, observation);
            bundle = await Client.SearchPostAsync(null, ("_type", "Patient,Observation"), ("_id", observation.Id));
            ValidateBundle(bundle, observation);

            bundle = await Client.SearchAsync($"?_type=Observation,Patient&_id={organization.Id}");
            ValidateBundle(bundle);
            bundle = await Client.SearchPostAsync(null, ("_type", "Patient,Observation"), ("_id", organization.Id));
            ValidateBundle(bundle);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResources_WhenSearchedWithCount_ThenNumberOfResourcesReturnedShouldNotExceedCount()
        {
            const int numberOfResources = 5;
            const int count = 2;

            // Create the resources
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(numberOfResources);

            Bundle results = new Bundle();

            // Search with count = 2, which should results in 5 pages.
            string url = $"Patient?_count={count}";

            int loop = 1;

            while (!string.IsNullOrEmpty(url))
            {
                // There should not be more than 3 loops.
                Assert.True(loop <= 3);

                Bundle bundle = await Client.SearchAsync(url);

                Assert.NotNull(bundle);
                Assert.NotEmpty(bundle.Entry);
                Assert.True(bundle.Entry.Count <= count);

                results.Entry.AddRange(bundle.Entry);

                url = bundle.NextLink?.ToString();

                loop++;
            }

            ValidateBundle(results, patients);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenMoreSearchResultsThanCount_WhenSearched_ThenNextLinkUrlShouldBePopulated()
        {
            // Create the resources
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(10);

            Bundle results = new Bundle();

            // Search with count = 2, which should results in 5 pages.
            string url = "Patient?_count=2";

            int loop = 1;

            while (!string.IsNullOrEmpty(url))
            {
                // There should not be more than 5 loops.
                Assert.True(loop <= 5);

                Bundle bundle = await Client.SearchAsync(url);

                Assert.NotNull(bundle);

                results.Entry.AddRange(bundle.Entry);

                url = bundle.NextLink?.ToString();

                loop++;
            }

            ValidateBundle(results, patients);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResourceWithHistory_WhenSearchedWithParams_ThenOnlyCurrentVersionShouldBeReturned()
        {
            // Create a coding that can be used to limit search to only the items within this test
            var testCoding = new Coding("https://testSystem", Guid.NewGuid().ToString());

            // Add the coding and set the observation status
            var originalObservation = Samples.GetDefaultObservation();
            originalObservation.Status = ObservationStatus.Preliminary;
            originalObservation.Code.Coding.Add(testCoding);

            // Create the original resource
            var createdResource = await Client.CreateAsync(originalObservation);

            // Update the status and submit the update to the server
            var resourceToUpdate = createdResource.Resource;
            resourceToUpdate.Status = ObservationStatus.Corrected;
            var updatedResource = await Client.UpdateAsync(resourceToUpdate);

            // Search for the given coding
            Bundle bundle = await Client.SearchAsync($"Observation?code={testCoding.System}|{testCoding.Code}");

            // The only thing returned should be the updated resource
            ValidateBundle(bundle, updatedResource);
        }
    }
}
