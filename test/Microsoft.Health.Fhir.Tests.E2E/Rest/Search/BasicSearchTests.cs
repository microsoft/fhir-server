// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json, FhirVersion.All)]
    public class BasicSearchTests : SearchTestsBase<HttpIntegrationTestFixture>
    {
        public BasicSearchTests(HttpIntegrationTestFixture fixture)
            : base(fixture)
        {
            // Delete all patients before starting the test.
            Client.DeleteAllResources("Patient").Wait();
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResourceWithVariousValues_WhenSearchedWithMultipleParams_ThenOnlyResourcesMatchingAllSearchParamsShouldBeReturned()
        {
            // Create various resources.
            ResourceElement[] patients = await Client.CreateResourcesAsync(
                Client.GetDefaultPatient,
                p => SetPatientInfo(p, "Seattle", "Robinson"),
                p => SetPatientInfo(p, "Portland", "Williamas"),
                p => SetPatientInfo(p, "Seattle", "Jones"));

            await ExecuteAndValidateBundle("Patient?address-city=seattle&family=Jones", patients[2]);

            ResourceElement SetPatientInfo(ResourceElement patient, string city, string family)
            {
                patient = Client.UpdatePatientAddressCity(patient, city);

                patient = Client.UpdatePatientFamilyName(patient, family);

                return patient;
            }
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenVariousTypesOfResources_WhenSearchedByResourceType_ThenOnlyResourcesMatchingTheResourceTypeShouldBeReturned()
        {
            // Create various resources.
            ResourceElement[] patients = await Client.CreateResourcesAsync(Client.GetDefaultPatient, 3);
            await Client.CreateAsync(Client.GetDefaultObservation());
            await Client.CreateAsync(Client.GetDefaultOrganization());

            await ExecuteAndValidateBundle("Patient", patients);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResourcesWithVariousValues_WhenSearchedWithTheMissingModifer_ThenOnlyTheResourcesWithMissingOrPresentParametersAreReturned()
        {
            ResourceElement femalePatient = (await Client.CreateResourcesAsync(Client.GetDefaultPatient, p => Client.UpdatePatientGender(p, "Female"))).Single();
            ResourceElement unspecifiedPatient = (await Client.CreateResourcesAsync(Client.GetDefaultPatient, p => Client.UpdatePatientGender(p, string.Empty))).Single();

            await ExecuteAndValidateBundle("Patient?gender:missing=true", unspecifiedPatient);
            await ExecuteAndValidateBundle("Patient?gender:missing=false", femalePatient);

            await ExecuteAndValidateBundle("Patient?_type:missing=true");
            await ExecuteAndValidateBundle("Patient?_type:missing=false", femalePatient, unspecifiedPatient);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenVariousTypesOfResources_WhenSearchingAcrossAllResourceTypes_ThenOnlyResourcesMatchingTypeParameterShouldBeReturned()
        {
            // Create various resources.
            ResourceElement[] patients = await Client.CreateResourcesAsync(Client.GetDefaultPatient, 3);
            ResourceElement observation = (await Client.CreateAsync(Client.GetDefaultObservation())).Resource;
            ResourceElement organization = (await Client.CreateAsync(Client.GetDefaultOrganization())).Resource;

            await ExecuteAndValidateBundle("?_type=Patient", patients);

            ResourceElement bundle = await Client.SearchPostAsync(null, ("_type", "Patient"));
            ValidateBundle(bundle, "_search", patients);

            bundle = await Client.SearchAsync("?_type=Observation,Patient");

            var bundleCount = bundle.Select(KnownFhirPaths.BundleEntries).Count();

            Assert.True(bundleCount > patients.Length);
            bundle = await Client.SearchPostAsync(null, ("_type", "Patient,Observation"));
            Assert.True(bundleCount > patients.Length);

            await ExecuteAndValidateBundle($"?_type=Observation,Patient&_id={observation.Id}", observation);
            bundle = await Client.SearchPostAsync(null, ("_type", "Patient,Observation"), ("_id", observation.Id));
            ValidateBundle(bundle, "_search", observation);

            await ExecuteAndValidateBundle($"?_type=Observation,Patient&_id={organization.Id}");
            bundle = await Client.SearchPostAsync(null, ("_type", "Patient,Observation"), ("_id", organization.Id));
            ValidateBundle(bundle, "_search");
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResources_WhenSearchedWithCount_ThenNumberOfResourcesReturnedShouldNotExceedCount()
        {
            const int numberOfResources = 5;
            const int count = 2;

            // Create the resources
            ResourceElement[] patients = await Client.CreateResourcesAsync(Client.GetDefaultPatient, numberOfResources);

            // Search with count = 2, which should results in 5 pages.
            string url = $"Patient?_count={count}";
            string baseUrl = Fixture.GenerateFullUrl(url);

            int loop = 1;

            var results = new List<ResourceElement>();

            while (!string.IsNullOrEmpty(url))
            {
                // There should not be more than 3 loops.
                Assert.True(loop <= 3);

                ResourceElement bundle = await Client.SearchAsync(url);

                Assert.NotNull(bundle);

                var entries = bundle.Select(KnownFhirPaths.BundleEntries).ToList();

                Assert.NotEmpty(entries);
                Assert.True(entries.Count <= count);

                results.AddRange(entries.Select(e => e.ToResourceElement()));

                url = bundle.Scalar<string>(KnownFhirPaths.BundleNextLink);

                if (url != null)
                {
                    Assert.StartsWith(baseUrl, url);
                }

                loop++;
            }

            ValidateBundle(results, patients);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenMoreSearchResultsThanCount_WhenSearched_ThenNextLinkUrlShouldBePopulated()
        {
            // Create the resources
            ResourceElement[] patients = await Client.CreateResourcesAsync(Client.GetDefaultPatient, 10);

            var results = new List<ResourceElement>();

            // Search with count = 2, which should results in 5 pages.
            string url = "Patient?_count=2";
            string baseUrl = Fixture.GenerateFullUrl(url);

            int loop = 1;

            while (!string.IsNullOrEmpty(url))
            {
                // There should not be more than 5 loops.
                Assert.True(loop <= 5);

                ResourceElement bundle = await Client.SearchAsync(url);

                Assert.NotNull(bundle);

                var entries = bundle.Select(KnownFhirPaths.BundleEntries);
                results.AddRange(entries.Select(e => e.ToResourceElement()));

                url = bundle.Scalar<string>(KnownFhirPaths.BundleNextLink);

                if (url != null)
                {
                    Assert.StartsWith(baseUrl, url);
                }

                loop++;
            }

            ValidateBundle(results, patients);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResourceWithHistory_WhenSearchedWithParams_ThenOnlyCurrentVersionShouldBeReturned()
        {
            // Create a coding that can be used to limit search to only the items within this test
            var system = "https://testsystem";
            var code = Guid.NewGuid().ToString();

            // Add the coding and set the observation status
            var originalObservation = Client.GetDefaultObservation();

            originalObservation = Client.UpdateObservationStatus(originalObservation, "Preliminary");
            originalObservation = Client.AddObservationCoding(originalObservation, system, code);

            // Create the original resource
            var createdResource = await Client.CreateAsync(originalObservation);

            // Update the status and submit the update to the server
            var resourceToUpdate = createdResource.Resource;
            resourceToUpdate = Client.UpdateObservationStatus(resourceToUpdate, "Corrected");
            var updatedResource = await Client.UpdateAsync(resourceToUpdate);

            // Search for the given coding. The only thing returned should be the updated resource
            await ExecuteAndValidateBundle($"Observation?code={system}|{code}", updatedResource);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenListOfResources_WhenSearchedWithCountSummary_ThenTotalCountShouldBeReturned()
        {
            const int numberOfResources = 5;

            string code = Guid.NewGuid().ToString();

            await Client.CreateResourcesAsync(PatientFactory, 5);

            ResourceElement bundle = await Client.SearchAsync($"Patient?_tag={code}&_summary=count");

            Assert.NotNull(bundle);
            Assert.Equal(numberOfResources, bundle.Scalar<long>("Resource.total"));
            Assert.Empty(bundle.Select(KnownFhirPaths.BundleEntries));

            ResourceElement PatientFactory()
            {
                var patient = Client.GetDefaultPatient();
                return Client.AddMetaTag(patient, string.Empty, code);
            }
        }
    }
}
