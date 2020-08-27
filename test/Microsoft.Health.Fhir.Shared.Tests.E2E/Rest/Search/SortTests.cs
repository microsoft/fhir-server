// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
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
        public async Task GivenResources_WhenSearchedWithUnsupportedSortParams_ThenSortIsDroppedFromUrl()
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreateResources(tag);

            await Assert.ThrowsAsync<FhirException>(async () => await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=name", false, patients.Cast<Resource>().ToArray()));
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResources_WhenSearchedWithSortParams_ThenResourcesAreReturnedInTheAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreateResources(tag);

            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=_lastUpdated", false, patients.Cast<Resource>().ToArray());
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResources_WhenSearchedWithSortParamsWithHyphen_ThenResourcesAreReturnedInTheDescendingOrder()
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreateResources(tag);

            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=-_lastUpdated", false, patients.Reverse().Cast<Resource>().ToArray());
        }

        [Fact]
        public async Task GivenMoreThanTenResources_WhenSearchedWithSortParam_ThenResourcesAreReturnedInAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePaginatedResources(tag);

            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=_lastUpdated", false, patients.Cast<Resource>().ToArray());
        }

        [Fact]
        public async Task GivenMoreThanTenResources_WhenSearchedWithSortParamWithHyphen_ThenResourcesAreReturnedInDescendingOrder()
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePaginatedResources(tag);

            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=-_lastUpdated", false, patients.Reverse().Cast<Resource>().ToArray());
        }

        // uncomment only when db cleanup happens on each run, otherwise the paging might cause expected resources to not arrive
        /*[Fact]
        public async Task GivenQueryWithNoFilter_WhenSearchedWithSortParam_ThenResourcesAreReturnedInAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreateResources(tag);

            // Ask to get all patient without any filters
            // Note: this might result in getting Patients that were created on other tests,
            // e.g. previous runs which were not cleaned yet, or concurrent tests.
            // we overcome this issue by not looking for specific results, rather just make sure they are
            // sorted.
            await ExecuteAndValidateBundleSuperset($"Patient?_sort=birthdate", false, patients.Cast<Resource>().ToArray());
        }*/

        [Fact]
        public async Task GivenQueryWithDatetimeFilter_WhenSearchedWithSortParamOnDatetime_ThenResourcesAreReturnedInAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            // save the timestamp prior to creating the resources
            var now = DateTime.Now;
            var time = now.AddSeconds(-1);

            // create the resources which will have an timestamp bigger than the 'now' var
            var patients = await CreateResources(tag);

            // Ask to get all patient with datetime filter
            // Note: this might result in getting Patients that were created on other tests (concurrent tests),
            // e.g. previous runs which were not cleaned yet, or concurrent tests.
            // we overcome this issue by not looking for specific results, rather just make sure they are
            // sorted.

            // Format the time to fit yyyy-MM-ddTHH:mm:ss.fffffffzzz, and encode its special characters.
            // sort and filter are based on same type (datetime)
            string lastUpdated = HttpUtility.UrlEncode($"{time:o}");
            await ExecuteAndValidateBundleSuperset($"Patient?_lastUpdated=gt{lastUpdated}&_sort=birthdate", false, patients.Cast<Resource>().ToArray());
        }

        [Fact]
        public async Task GivenQueryWithTagFilter_WhenSearchedWithSortParamOnDatetime_ThenResourcesAreReturnedInAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            // create the resources which will have an timestamp bigger than the 'now' var
            var patients = await CreateResources(tag);

            // Ask to get all patient with specific tag order by birthdate (timestamp)
            // filter and sort are different based on different types
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=birthdate", false, patients.Cast<Resource>().ToArray());
        }

        [Fact]
        public async Task GivenQueryWithMultipleFilters_WhenSearchedWithSortParamOnDatetime_ThenResourcesAreReturnedInAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            var patients = await CreateResources(tag);

            // Ask to get all patient with specific tag and family name order by birthdate (timestamp)
            // filter and sort are different based on different types
            var filteredFamilyName = "Williamas";
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&family={filteredFamilyName}&_sort=birthdate", false, patients.Reverse().Where(x => x.Name[0].Family == filteredFamilyName).OrderBy(x => x.BirthDate).Cast<Resource>().ToArray());
        }

        private async Task<Patient[]> CreateResources(string tag)
        {
            // Create various resources.
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(
                p => SetPatientInfo(p, "Seattle", "Robinson", tag, DateTime.Now.Subtract(TimeSpan.FromDays(90))),
                p => SetPatientInfo(p, "Portland", "Williamas", tag, DateTime.Now.Subtract(TimeSpan.FromDays(60))),
                p => SetPatientInfo(p, "Seattle", "Jones", tag, DateTime.Now.Subtract(TimeSpan.FromDays(30))));

            return patients;
        }

        private async Task<Patient[]> CreatePaginatedResources(string tag)
        {
            // Create various resources.
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(
                p => SetPatientInfo(p, "Seattle", "Robinson", tag),
                p => SetPatientInfo(p, "Portland", "Williamas", tag),
                p => SetPatientInfo(p, "Portland", "James", tag),
                p => SetPatientInfo(p, "Seatt;e", "Alex", tag),
                p => SetPatientInfo(p, "Portland", "Rock", tag),
                p => SetPatientInfo(p, "Seattle", "Mike", tag),
                p => SetPatientInfo(p, "Portland", "Christie", tag),
                p => SetPatientInfo(p, "Portland", "Lone", tag),
                p => SetPatientInfo(p, "Seattle", "Sophie", tag),
                p => SetPatientInfo(p, "Portland", "Peter", tag),
                p => SetPatientInfo(p, "Portland", "Cathy", tag),
                p => SetPatientInfo(p, "Seattle", "Jones", tag));

            return patients;
        }

        private void SetPatientInfo(Patient patient, string city, string family, string tag)
        {
            SetPatientInfoInternal(patient, city, family, tag, "1970-01-01");
        }

        private void SetPatientInfo(Patient patient, string city, string family, string tag, DateTime birthDate)
        {
            // format according as expected
            SetPatientInfoInternal(patient, city, family, tag, birthDate.ToString("yyyy-MM-dd"));
        }

        private void SetPatientInfoInternal(Patient patient, string city, string family, string tag, string birthDate)
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
            patient.BirthDate = birthDate;
        }
    }
}
