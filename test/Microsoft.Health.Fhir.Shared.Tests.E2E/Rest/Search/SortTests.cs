// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;
using Xunit.Sdk;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class SortTests : SearchTestsBase<HttpIntegrationTestFixture>
    {
        public SortTests(HttpIntegrationTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        public async Task GivenPatientResources_WhenSearchedWithUnsupportedSortParams_ThenSortIsDroppedFromUrl()
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreateResources(tag);

            await Assert.ThrowsAsync<FhirException>(async () => await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=name", false, patients.Cast<Resource>().ToArray()));
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatientResources_WhenSearchedWithSortParams_ThenResourcesAreReturnedInTheCorrectOrder()
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreateResources(tag);

            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=_lastUpdated", false, patients.Cast<Resource>().ToArray());
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=-_lastUpdated", false, patients.Reverse().Cast<Resource>().ToArray());

            await Assert.ThrowsAsync<CollectionException>(async () => await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=_lastUpdated", false, patients.Reverse().Cast<Resource>().ToArray()));
        }

        [Fact]
        public async Task GivenResources_WhenSearchedWithLastUpdatedSortParam_ThenResourcesAreReturnedInAscendingOrder()
        {
            var newResources = new List<Resource>();
            var tag = Guid.NewGuid().ToString();

            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetDefaultPatient().ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetDefaultOrganization().ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("BloodGlucose").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("BloodPressure").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("Patient-f001").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("Condition-For-Patient-f001").ToPoco(), tag)));

            await ExecuteAndValidateBundle($"?_tag={tag}&_sort=_lastUpdated", false, newResources.ToArray());
        }

        [Fact]
        public async Task GivenResources_WhenSearchedWithLastUpdatedSortParamWithHyphen_ThenResourcesAreReturnedInDescendingOrder()
        {
            var newResources = new List<Resource>();
            var tag = Guid.NewGuid().ToString();

            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetDefaultPatient().ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetDefaultOrganization().ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("BloodGlucose").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("BloodPressure").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("Patient-f001").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("Condition-For-Patient-f001").ToPoco(), tag)));
            newResources.Reverse();

            await ExecuteAndValidateBundle($"?_tag={tag}&_sort=-_lastUpdated", false, newResources.ToArray());
        }

        [Fact]
        public async Task GivenMoreThanTenResources_WhenSearchedWithLastUpdatedAsSortParam_ThenPaginatedResourcesReturnedInAscendingOrder()
        {
            var newResources = new List<Resource>();
            var tag = Guid.NewGuid().ToString();

            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetDefaultPatient().ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetDefaultOrganization().ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("BloodGlucose").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("BloodPressure").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("Patient-f001").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("Condition-For-Patient-f001").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("Encounter-For-Patient-f001").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("Observation-For-Patient-f001").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("ObservationWith1MinuteApgarScore").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("ObservationWith20MinuteApgarScore").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("ObservationWithEyeColor").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("ObservationWithTemperature").ToPoco(), tag)));

            await ExecuteAndValidateBundle($"?_tag={tag}&_sort=_lastUpdated", false, newResources.ToArray());
        }

        [Fact]
        public async Task GivenMoreThanTenResources_WhenSearchedWithLastUpdatedAsSortParamWithHyphen_ThenPaginatedResourcesReturnedInDescendingOrder()
        {
            var newResources = new List<Resource>();
            var tag = Guid.NewGuid().ToString();

            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetDefaultPatient().ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetDefaultOrganization().ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("BloodGlucose").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("BloodPressure").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("Patient-f001").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("Condition-For-Patient-f001").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("Encounter-For-Patient-f001").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("Observation-For-Patient-f001").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("ObservationWith1MinuteApgarScore").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("ObservationWith20MinuteApgarScore").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("ObservationWithEyeColor").ToPoco(), tag)));
            newResources.Add(await Client.CreateAsync(AddTagToResource(Samples.GetJsonSample("ObservationWithTemperature").ToPoco(), tag)));
            newResources.Reverse();

            await ExecuteAndValidateBundle($"?_tag={tag}&_sort=-_lastUpdated", false, newResources.ToArray());
        }

        private Resource AddTagToResource(Resource resource, string tag)
        {
            resource.Meta = new Meta { Tag = new List<Coding> { new Coding(null, tag) }, };
            return resource;
        }

        private async Task<Patient[]> CreateResources(string tag)
        {
            // Create various resources.
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(
                p => SetPatientInfo(p, "Seattle", "Robinson", tag),
                p => SetPatientInfo(p, "Portland", "Williamas", tag),
                p => SetPatientInfo(p, "Seattle", "Jones", tag));

            return patients;
        }

        private void SetPatientInfo(Patient patient, string city, string family, string tag)
        {
            patient.Meta = new Meta { Tag = new List<Coding> { new Coding(null, tag) }, };

            patient.Address = new List<Address>
            {
                new Address
                {
                    City = city,
                },
            };

            patient.Name = new List<HumanName> { new HumanName { Family = family }, };
        }
    }
}
