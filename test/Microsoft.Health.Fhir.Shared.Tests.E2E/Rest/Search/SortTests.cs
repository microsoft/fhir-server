// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Sort)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class SortTests : SearchTestsBase<HttpIntegrationTestFixture>
    {
        private static readonly string _unsupportedSearchAndSortParam = "abcd1234"; // Parameter is invalid search parameter, therefore it is invalid sort parameter as well.
        private static readonly string _unsupportedSortParam = "link"; // Parameter "link" is of reference type so sorting by link will always fail.

        public SortTests(HttpIntegrationTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatients_WhenSearchedWithSortParams_ThenPatientsAreReturnedInTheAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatients(tag);

            // Due to multiple service instances lastUpdated time could vary, and patients array might not be in the ascending order by lastUpdated. So sort the expected patients array
            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort=_lastUpdated");
            SortTestsAssert.AssertNumberOfResources(patients, returnedResults);
            SortTestsAssert.AssertResourcesAreInAscendingOrderByLastUpdateInRange(0, returnedResults.Count, returnedResults);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatients_WhenSearchedWithSortParamsWithHyphen_ThenPatientsAreReturnedInTheDescendingOrder()
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatients(tag);

            // Due to multiple service instances lastUpdated time could vary, and patients array might not be in the ascending order by lastUpdated. So sort the expected patients array
            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort=-_lastUpdated");

            SortTestsAssert.AssertNumberOfResources(patients, returnedResults);
            SortTestsAssert.AssertResourcesAreInDescendingOrderByLastUpdateInRange(0, returnedResults.Count, returnedResults);
        }

        [Theory]
        [InlineData("birthdate")]
        [InlineData("_lastUpdated")]
        public async Task GivenMoreThanTenPatients_WhenSearchedWithSortParam_ThenPatientsAreReturnedInAscendingOrder(string sortParameterName)
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePaginatedPatients(tag);

            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort={sortParameterName}");

            if (sortParameterName == "birthdate")
            {
                SortTestsAssert.AssertPatientBirthDateAscendingOrderInRange(0, patients.Count(), returnedResults);
            }
            else if (sortParameterName == "_lastUpdated")
            {
                SortTestsAssert.AssertResourcesAreInAscendingOrderByLastUpdateInRange(0, patients.Count(), returnedResults);
            }
            else
            {
                Assert.Fail("Not expected sort parameter name.");
            }
        }

        [Theory]
        [InlineData("birthdate")]
        [InlineData("_lastUpdated")]
        public async Task GivenMoreThanTenPatients_WhenSearchedWithSortParamWithHyphen_ThenPatientsAreReturnedInDescendingOrder(string sortParameterName)
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePaginatedPatients(tag);

            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort=-{sortParameterName}");

            if (sortParameterName == "birthdate")
            {
                SortTestsAssert.AssertPatientBirthDateDescendingOrderInRange(0, patients.Count(), returnedResults);
            }
            else if (sortParameterName == "_lastUpdated")
            {
                SortTestsAssert.AssertResourcesAreInDescendingOrderByLastUpdateInRange(0, patients.Count(), returnedResults);
            }
            else
            {
                Assert.Fail("Not expected sort parameter name.");
            }
        }

        [Theory]
        [InlineData("birthdate")]
        [InlineData("-birthdate")]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenPatientsWithSameBirthdateAndMultiplePages_WhenSortedByBirthdate_ThenPatientsAreReturnedInCorrectOrder(string sortParameterName)
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatientsWithSameBirthdate(tag);

            // For SQL, we always choose the "oldest" resource based on last updated time (irrespective of overall sort order)
            // Since sort is based on same BirthDate, the order in which resources will be returned depends on their creation time for both sortParameterName
            // Above patients array could be out of sync due to inconsistent system time across multiple server instances so sort the expected patients array
            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort={sortParameterName}&_count=3");

            if (sortParameterName == "birthdate")
            {
                SortTestsAssert.AssertPatientBirthDateAscendingOrderInRange(0, returnedResults.Count, returnedResults);
            }
            else if (sortParameterName == "-birthdate")
            {
                SortTestsAssert.AssertPatientBirthDateDescendingOrderInRange(0, returnedResults.Count, returnedResults);
            }
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.CosmosDb)]
        public async Task GivenPatientsWithSameBirthdateAndMultiplePages_WhenSortedByBirthdate_ThenPatientsAreReturnedInAscendingOrder()
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatientsWithSameBirthdate(tag);

            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort=birthdate&_count=3");

            SortTestsAssert.AssertPatientBirthDateAscendingOrderInRange(0, returnedResults.Count, returnedResults);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.CosmosDb)]
        public async Task GivenPatientsWithSameBirthdateAndMultiplePages_WhenSortedByBirthdateWithHyphen_ThenPatientsAreReturnedInDescendingOrder()
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatientsWithSameBirthdate(tag);

            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort=-birthdate&_count=3");

            SortTestsAssert.AssertPatientBirthDateDescendingOrderInRange(0, 3, returnedResults.Take(3).ToList());
        }

        // uncomment only when db cleanup happens on each run, otherwise the paging might cause expected resources to not arrive
        /*[Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenQueryWithNoFilter_WhenSearchedWithSortParam_ThenResourcesAreReturnedInAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreateResources(tag);

            // Ask to get all patient without any filters
            // Note: this might result in getting Patients that were created on other tests,
            // e.g. previous runs which were not cleaned yet, or concurrent tests.
            // we overcome this issue by not looking for specific results, rather just make sure they are
            // sorted.
            await ExecuteAndValidateBundle($"Patient?_sort=birthdate", false, patients.Cast<Resource>().ToArray());
        }*/

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatients_WhenSearchedWithFamilySortParams_ThenPatientsAreReturnedInTheAscendingOrder()
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatients(tag);

            // We are ordering the patients array by Family name
            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort=family");

            SortTestsAssert.AssertNumberOfResources(patients, returnedResults);
            SortTestsAssert.AssertPatientFamilyNamesAreEqualInRange(0, patients.OrderBy(x => x.Name.Min(n => n.Family)).ToList(), returnedResults);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatients_WhenSearchedWithTextSortAndInclude_ThenPatientsAreReturned()
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatients(tag);
            var results = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort=family&_include=Observation:performer");
            SortTestsAssert.AssertNumberOfResources(patients, results);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatients_WhenSearchedWithFamilySortParamsWithHyphen_ThenPatientsAreReturnedInTheDescendingOrder()
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatients(tag);

            // We are ordering the patients array by Family name
            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort=-family");

            SortTestsAssert.AssertNumberOfResources(patients, returnedResults);
            SortTestsAssert.AssertPatientFamilyNamesAreEqualInRange(0, patients.OrderByDescending(x => x.Name.Max(n => n.Family)).ToList(), returnedResults);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatients_WhenSearchedWithInvalidSortParamsAndHandlingLenient_ThenPatientsAreReturnedUnsortedWithWarning()
        {
            var tag = Guid.NewGuid().ToString();
            Patient[] patients = await CreatePatients(tag);
            Resource[] expectedResources = new Resource[patients.Length + 1];
            expectedResources[0] = OperationOutcome.ForMessage(
                string.Format(CultureInfo.InvariantCulture, Core.Resources.SearchSortParameterNotSupported, _unsupportedSortParam),
                OperationOutcome.IssueType.NotSupported,
                OperationOutcome.IssueSeverity.Warning);
            patients.Cast<Resource>().ToArray().CopyTo(expectedResources, 1);

            await ExecuteAndValidateBundle(
                $"Patient?_tag={tag}&_sort={_unsupportedSortParam}",
                true, // Server sort will fail, so sort expected result and actual result before comparing.
                true, // Turn on test logic for invalid sort parameter.
                new Tuple<string, string>("Prefer", "handling=lenient"),
                expectedResources);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatients_WhenSearchedWithInvalidSearchAndSortParamsAndHandlingLenient_ThenPatientsAreReturnedUnsortedWithWarning()
        {
            var tag = Guid.NewGuid().ToString();
            Patient[] patients = await CreatePatients(tag);
            Resource[] expectedResources = new Resource[patients.Length + 1];
            expectedResources[0] = OperationOutcome.ForMessage(
                string.Format(CultureInfo.InvariantCulture, Core.Resources.SortParameterValueIsNotValidSearchParameter, _unsupportedSearchAndSortParam, "Patient"),
                OperationOutcome.IssueType.NotSupported,
                OperationOutcome.IssueSeverity.Warning);
            patients.Cast<Resource>().ToArray().CopyTo(expectedResources, 1);

            await ExecuteAndValidateBundle(
                $"Patient?_tag={tag}&_sort={_unsupportedSearchAndSortParam}",
                true, // Server sort will fail, so sort expected result and actual result before comparing.
                true, // Turn on test logic for invalid sort parameter.
                new Tuple<string, string>("Prefer", "handling=lenient"),
                expectedResources);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatients_WhenSearchedWithInvalidSortParamsAndHandlingStrict_ThenErrorReturnedWithMessage()
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatients(tag);
            OperationOutcome expectedOperationOutcome = OperationOutcome.ForMessage(
                string.Format(CultureInfo.InvariantCulture, Core.Resources.SearchSortParameterNotSupported, _unsupportedSortParam),
                OperationOutcome.IssueType.Invalid,
                OperationOutcome.IssueSeverity.Error);

            await ExecuteAndValidateErrorOperationOutcomeAsync(
                $"Patient?_tag={tag}&_sort={_unsupportedSortParam}",
                new Tuple<string, string>("Prefer", "handling=strict"),
                HttpStatusCode.BadRequest,
                expectedOperationOutcome);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatients_WhenSearchedWithInvalidSearchAndSortParamsAndHandlingStrict_ThenErrorReturnedWithMessage()
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatients(tag);
            OperationOutcome expectedOperationOutcome = OperationOutcome.ForMessage(
                string.Format(CultureInfo.InvariantCulture, Core.Resources.SortParameterValueIsNotValidSearchParameter, _unsupportedSearchAndSortParam, "Patient"),
                OperationOutcome.IssueType.Invalid,
                OperationOutcome.IssueSeverity.Error);

            await ExecuteAndValidateErrorOperationOutcomeAsync(
                $"Patient?_tag={tag}&_sort={_unsupportedSearchAndSortParam}",
                new Tuple<string, string>("Prefer", "handling=strict"),
                HttpStatusCode.BadRequest,
                expectedOperationOutcome);
        }

        [Fact]
        public async Task GivenQueryWithDatetimeFilter_WhenSearchedWithSortParamOnDatetime_ThenResourcesAreReturnedInAscendingOrder()
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();

            // save the timestamp prior to creating the resources
            var since = DateTime.Now.AddSeconds(-10);

            // create the resources which will have an timestamp bigger than the 'since' var
            var patients = await CreatePatients(tag);

            // Format the time to fit yyyy-MM-ddTHH:mm:ss.fffffffzzz, and encode its special characters.
            // sort and filter are based on same type (datetime)
            string lastUpdated = HttpUtility.UrlEncode($"{since:o}");

            // CreatePatients - Creates patients with birthdates in Ascending order, sort to avoid failures
            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_lastUpdated=gt{lastUpdated}&_sort=birthdate&_tag={tag}");

            SortTestsAssert.AssertNumberOfResources(patients, returnedResults);
            SortTestsAssert.AssertPatientBirthDateAscendingOrderInRange(0, patients.Length, returnedResults);
        }

        [Fact]
        public async Task GivenQueryWitDatetimeFilter_WhenSearchedWithHyphenSortParamOnDatetime_ThenResourcesAreReturnedInDescendingOrder()
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();

            // save the timestamp prior to creating the resources
            var since = DateTime.Now.AddSeconds(-10);

            // create the resources which will have an timestamp bigger than the 'now' var
            var patients = await CreatePatients(tag);

            // Format the time to fit yyyy-MM-ddTHH:mm:ss.fffffffzzz, and encode its special characters.
            // sort and filter are based on same type (datetime)
            string lastUpdated = HttpUtility.UrlEncode($"{since:o}");

            // CreatePatients - Creates patients with birthdates in Ascending order, sort to avoid failures
            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_lastUpdated=gt{lastUpdated}&_sort=-birthdate&_tag={tag}");

            SortTestsAssert.AssertNumberOfResources(patients, returnedResults);
            SortTestsAssert.AssertPatientBirthDateDescendingOrderInRange(0, patients.Length, returnedResults);
        }

        [Fact]
        public async Task GivenQueryWithTagFilter_WhenSearchedWithSortParamOnDatetime_ThenResourcesAreReturnedInAscendingOrder()
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();

            var patients = await CreatePatients(tag);

            // Ask to get all patient with specific tag order by birthdate (timestamp)
            // filter and sort are different based on different types

            // CreatePatients - Creates patients with birthdates in Ascending order, sort to avoid failures
            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort=birthdate");

            SortTestsAssert.AssertNumberOfResources(patients, returnedResults);
            SortTestsAssert.AssertPatientBirthDateAscendingOrderInRange(0, patients.Length, returnedResults);
        }

        [Fact]
        public async Task GivenQueryWithTagFilter_WhenSearchedWithHyphenSortParamOnDatetime_ThenResourcesAreReturnedInDescendingOrder()
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();

            // create the resources which will have an timestamp bigger than the 'now' var
            var patients = await CreatePatients(tag);

            // Ask to get all patient with specific tag order by birthdate (timestamp)
            // filter and sort are different based on different types

            // CreatePatients - Creates patients with birthdates in Ascending order, sort to avoid failures
            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort=-birthdate");

            SortTestsAssert.AssertNumberOfResources(patients, returnedResults);
            SortTestsAssert.AssertPatientBirthDateDescendingOrderInRange(0, patients.Length, returnedResults);
        }

        [Fact]
        public async Task GivenQueryWithMultipleFilters_WhenSearchedWithSortParamOnDatetime_ThenResourcesAreReturnedInAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            var patients = await CreatePatients(tag);

            // Ask to get all patient with specific tag and family name order by birthdate (timestamp)
            // filter and sort are different based on different types
            var filteredFamilyName = "Williamas";
            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&family={filteredFamilyName}&_sort=birthdate");

            var filteredPatients = patients.Where(x => x.Name[0].Family == filteredFamilyName);

            SortTestsAssert.AssertNumberOfResources(filteredPatients, returnedResults);
            SortTestsAssert.AssertPatientBirthDateAscendingOrderInRange(0, filteredPatients.Count(), returnedResults);
        }

        [Fact]
        public async Task GivenQueryWithMultipleFilters_WhenSearchedWithHyphenSortParamOnDatetime_ThenResourcesAreReturnedInDescendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            var patients = await CreatePatients(tag);

            // Ask to get all patient with specific tag and family name order by birthdate (timestamp)
            // filter and sort are different based on different types
            var filteredFamilyName = "Williamas";
            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&family={filteredFamilyName}&_sort=-birthdate");

            var filteredPatients = patients.Where(x => x.Name[0].Family == filteredFamilyName);

            SortTestsAssert.AssertNumberOfResources(filteredPatients, returnedResults);
            SortTestsAssert.AssertPatientBirthDateDescendingOrderInRange(0, filteredPatients.Count(), returnedResults);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenQueryWithRevinclude_WhenSearchedWithSortParamOnDatetime_ThenResourcesAreReturnedInAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            var resources = new List<Resource>();

            // create the resources which will have an timestamp bigger than the 'now' var
            var patients = await CreatePatients(tag);

            // CreatePatients - Creates patients with birthdates in Ascending order, sort to avoid failures
            patients = patients.OrderBy(x => x.BirthDate).ToArray();
            resources.AddRange(patients);

            foreach (Patient p in patients)
            {
                var obs = await AddObservationToPatient(p, "1990-01-01", tag);
                resources.AddRange(obs);
            }

            // Ask to get all patient with specific tag order by birthdate (timestamp)
            // filter and sort are different based on different types
            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort=birthdate&_revinclude=Observation:subject");

            SortTestsAssert.AssertNumberOfResources(resources, returnedResults);
            SortTestsAssert.AssertPatientBirthDateAscendingOrderInRange(0, patients.Count(), returnedResults);

            SortTestsAssert.AssertResourceTypeInRange<Observation>(patients.Count(), returnedResults.Count, returnedResults);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenQueryWithRevinclude_WhenSearchedWithSortParamOnDatetimeWithHyphen_ThenResourcesAreReturnedInDescendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            var resources = new List<Resource>();

            // create the resources which will have an timestamp bigger than the 'now' var
            var patients = await CreatePatients(tag);

            // CreatePatients - Creates patients with birthdates in Ascending order, sort to avoid failures
            patients = patients.OrderBy(x => x.BirthDate).ToArray();

            var observations = new List<Observation>();
            for (int i = 0; i < patients.Length; i++)
            {
                var obs = await AddObservationToPatient(patients[i], "1990-01-01", tag);
                observations.Add(obs.First());
            }

            resources.AddRange(patients.Reverse());
            resources.AddRange(observations);

            // Ask to get all patient with specific tag order by birthdate (timestamp)
            // filter and sort are different based on different types
            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort=-birthdate&_revinclude=Observation:subject");

            SortTestsAssert.AssertNumberOfResources(resources, returnedResults);

            SortTestsAssert.AssertPatientBirthDateDescendingOrderInRange(0, patients.Count(), returnedResults);

            SortTestsAssert.AssertResourceTypeInRange<Observation>(patients.Count(), returnedResults.Count, returnedResults);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenQueryWithRevinclude_WhenSearchedWithSortParamOnLastupdated_ThenResourcesAreReturnedInAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            var resources = new List<Resource>();

            // create the resources which will have an timestamp bigger than the 'now' var
            var patients = await CreatePatients(tag);

            // CreatePatients - Creates patients in Ascending order but due to multiple instances lastUpdated time could vary
            patients = patients.OrderBy(x => x.Meta.LastUpdated).ToArray();

            var observations = new List<Observation>();
            for (int i = 0; i < patients.Length; i++)
            {
                var obs = await AddObservationToPatient(patients[i], "1990-01-01", tag);
                observations.Add(obs.First());
            }

            resources.AddRange(patients);
            resources.AddRange(observations);

            // Ask to get all patient with specific tag order by birthdate (timestamp)
            // filter and sort are different based on different types
            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort=_lastUpdated&_revinclude=Observation:subject");

            // Patients.
            SortTestsAssert.AssertResourceTypeInRange<Patient>(0, patients.Count(), returnedResults);
            SortTestsAssert.AssertResourcesAreInAscendingOrderByLastUpdateInRange(0, patients.Count(), returnedResults);

            // Observations.
            SortTestsAssert.AssertResourceTypeInRange<Observation>(patients.Count(), returnedResults.Count, returnedResults);
            SortTestsAssert.AssertResourcesAreInAscendingOrderByLastUpdateInRange(patients.Count(), returnedResults.Count, returnedResults);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenQueryWithRevinclude_WhenSearchedWithSortParamOnLastupdatedWithHyphen_ThenResourcesAreReturnedInDescendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            var resources = new List<Resource>();

            // create the resources which will have an timestamp bigger than the 'now' var
            var patients = await CreatePatients(tag);

            // CreatePatients - Creates patients in Ascending order but due to multiple instances lastUpdated time could vary
            patients = patients.OrderBy(x => x.Meta.LastUpdated).ToArray();

            var observations = new List<Observation>();
            for (int i = 0; i < patients.Length; i++)
            {
                var obs = await AddObservationToPatient(patients[i], "1990-01-01", tag);
                observations.Add(obs.First());
            }

            resources.AddRange(patients.Reverse());
            observations.Reverse();
            resources.AddRange(observations);

            // Ask to get all patient with specific tag order by birthdate (timestamp)
            // filter and sort are different based on different types
            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort=-_lastUpdated&_revinclude=Observation:subject");

            // Patients.
            SortTestsAssert.AssertResourceTypeInRange<Patient>(0, patients.Count(), returnedResults);
            SortTestsAssert.AssertResourcesAreInDescendingOrderByLastUpdateInRange(0, patients.Count(), returnedResults);

            // Observations.
            SortTestsAssert.AssertResourceTypeInRange<Observation>(patients.Count(), returnedResults.Count, returnedResults);
            SortTestsAssert.AssertResourcesAreInDescendingOrderByLastUpdateInRange(patients.Count(), returnedResults.Count, returnedResults);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenQueryWithObservationInclude_WhenSearchedWithSortParamOnDatetime_ThenResourcesAreReturnedInAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            var resources = new List<Resource>();

            // create the resources which will have an timestamp bigger than the 'now' var
            var patients = await CreatePatients(tag);

            var dates = new string[] { "1990-01-01", "1991-01-01", "1992-01-01", "1993-01-01" };
            var observations = new List<Observation>();
            for (int i = 0; i < patients.Length; i++)
            {
                var obs = await AddObservationToPatient(patients[i], dates[i], tag);
                observations.Add(obs.First());
            }

            resources.AddRange(observations);
            resources.AddRange(patients);

            // Ask to get all patient with specific tag order by birthdate (timestamp)
            // filter and sort are different based on different types
            var returnedResults = await GetResultsFromAllPagesAsync($"Observation?_tag={tag}&_sort=date&_include=Observation:subject");

            Assert.Equal(observations.Count() + patients.Count(), returnedResults.Count);

            // Observations.
            SortTestsAssert.AssertObservationEffectiveDateAscendingOrderInRange(0, observations.Count, returnedResults);

            // Patients.
            SortTestsAssert.AssertResourceTypeInRange<Patient>(observations.Count, returnedResults.Count, returnedResults);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenQueryWithObservationInclude_WhenSearchedWithSortParamOnDatetimeWithHyphen_ThenResourcesAreReturnedInDescendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            var resources = new List<Resource>();

            // create the resources which will have an timestamp bigger than the 'now' var
            var patients = await CreatePatients(tag);

            var dates = new string[] { "1990-01-01", "1991-01-01", "1992-01-01", "1993-01-01" };
            var observations = new List<Observation>();
            for (int i = 0; i < patients.Length; i++)
            {
                var obs = await AddObservationToPatient(patients[i], dates[i], tag);
                observations.Add(obs.First());
            }

            observations.Reverse();
            resources.AddRange(observations);
            resources.AddRange(patients);

            // Ask to get all patient with specific tag order by birthdate (timestamp)
            // filter and sort are different based on different types
            var returnedResults = await GetResultsFromAllPagesAsync($"Observation?_tag={tag}&_sort=-date&_include=Observation:subject");

            Assert.Equal(observations.Count() + patients.Count(), returnedResults.Count);

            // Observations.
            SortTestsAssert.AssertObservationEffectiveDateDescendingOrderInRange(0, observations.Count, returnedResults);

            // Patients.
            SortTestsAssert.AssertResourceTypeInRange<Patient>(observations.Count, returnedResults.Count, returnedResults);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenSingleWrittenObservation_WhenSearched_ThenObservationWithMatchingIdAndLastUpdatedIsReturned()
        {
            var tag = Guid.NewGuid().ToString();
            var written = (await AddObservationToPatient(null, "1990-01-01", tag))[0];
            var read = (await Client.SearchAsync($"Observation?_tag={tag}", null)).Resource.Entry[0].Resource;
            Assert.Equal(written.Id, read.Id);
            Assert.Equal(written.Meta.LastUpdated, read.Meta.LastUpdated);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenQueryWithObservation_WhenSearchedForItemsWithSubjectAndSort_ThenResourcesAreReturnedInAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            var expected_resources = new List<Resource>();

            // create the resources which will have an timestamp bigger than the 'now' var
            var patients = await CreatePatients(tag);

            var dates = new string[] { "1990-01-01", "1991-01-01", "1992-01-01", "1993-01-01" };
            var observations = new List<Observation>();
            for (int i = 0; i < patients.Length; i++)
            {
                var obs = await AddObservationToPatient(patients[i], dates[i], tag);
                observations.Add(obs.First());
            }

            // Add observation with no patient-> no subject, but we don't keep it in the expected result set.
            // observations.Add(AddObservationToPatient(null, dates[0], tag).Result.First());
            await AddObservationToPatient(null, dates[0], tag);

            expected_resources.AddRange(observations);

            // Get observations
            var returnedResults = await GetResultsFromAllPagesAsync($"Observation?_tag={tag}&_sort=date&subject:missing=false");

            SortTestsAssert.AssertNumberOfResources(expected_resources, returnedResults);
            SortTestsAssert.AssertObservationEffectiveDateAscendingOrderInRange(0, expected_resources.Count, returnedResults);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenQueryWithObservation_WhenSearchedForItemsWithNoSubjectAndSort_ThenResourcesAreReturnedInAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            var expected_resources = new List<Resource>();

            // create the resources which will have an timestamp bigger than the 'now' var
            var patients = await CreatePatients(tag);

            var dates = new string[] { "1990-01-01", "1991-01-01", "1992-01-01", "1993-01-01" };
            var observations = new List<Observation>();
            for (int i = 0; i < patients.Length; i++)
            {
                var obs = await AddObservationToPatient(patients[i], dates[i], tag);
                observations.Add(obs.First());
            }

            // Add observation with no patient-> no subject, and we keep it alone in the expected result set.
            expected_resources.Add(AddObservationToPatient(null, dates[0], tag).Result.First());

            // Get observations
            var returnedResults = await GetResultsFromAllPagesAsync($"Observation?_tag={tag}&_sort=date&subject:missing=true");

            SortTestsAssert.AssertNumberOfResources(expected_resources, returnedResults);
            SortTestsAssert.AssertObservationEffectiveDateAscendingOrderInRange(0, expected_resources.Count, returnedResults);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenQueryWithObservation_WhenSearchedForItemsWithNoSubjectAndLastUpdatedSort_ThenResourcesAreReturnedInAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            var expected_resources = new List<Resource>();

            // create the resources which will have an timestamp bigger than the 'now' var
            var patients = await CreatePatients(tag);

            var dates = new string[] { "1990-01-01", "1991-01-01", "1992-01-01", "1993-01-01" };
            var observations = new List<Observation>();
            for (int i = 0; i < patients.Length; i++)
            {
                var obs = await AddObservationToPatient(patients[i], dates[i], tag);
                observations.Add(obs.First());
            }

            // Add observation with no patient-> no subject, and we keep it alone in the expected result set.
            expected_resources.Add(AddObservationToPatient(null, dates[0], tag).Result.First());

            // Get observations
            var returnedResults = await GetResultsFromAllPagesAsync($"Observation?_tag={tag}&_sort=_lastUpdated&subject:missing=true");

            SortTestsAssert.AssertNumberOfResources(expected_resources, returnedResults);
            SortTestsAssert.AssertResourcesAreInAscendingOrderByLastUpdateInRange(0, expected_resources.Count, returnedResults);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenQueryWithObservation_WhenSearchedForItemsWithNoSubjectAndLastUpdatedSort_ThenResourcesAreReturnedInDescendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            var expected_resources = new List<Resource>();

            // create the resources which will have an timestamp bigger than the 'now' var
            var patients = await CreatePatients(tag);

            var dates = new string[] { "1990-01-01", "1991-01-01", "1992-01-01", "1993-01-01" };
            var observations = new List<Observation>();
            for (int i = 0; i < patients.Length; i++)
            {
                var obs = await AddObservationToPatient(patients[i], dates[i], tag);
                observations.Add(obs.First());
            }

            // Add observation with no patient-> no subject, and we keep it alone in the expected result set.
            expected_resources.Add(AddObservationToPatient(null, dates[0], tag).Result.First());

            // Get observations
            var returnedResults = await GetResultsFromAllPagesAsync($"Observation?_tag={tag}&_sort=-_lastUpdated&subject:missing=true");

            SortTestsAssert.AssertNumberOfResources(expected_resources, returnedResults);
            SortTestsAssert.AssertResourcesAreInDescendingOrderByLastUpdateInRange(0, expected_resources.Count, returnedResults);
        }

        [Fact]
        public async Task GivenPatientsWithMultipleNames_WhenFilteringAndSortingByFamilyName_ThenResourcesAreReturnedInAscendingOrder()
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatientsWithMultipleFamilyNames(tag);

            var returnedPatients = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&family=R&_sort=family");

            var expectedPatients = patients[0..5];
            SortTestsAssert.AssertPatientFamilyNamesAreEqualInRange(expectedPatients.Length, expectedPatients, returnedPatients);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public async Task GivenPatientsWithMultipleNamesAndPaginated_WhenFilteringAndSortingByFamilyName_ThenResourcesAreReturnedInAscendingOrder(int count)
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatientsWithMultipleFamilyNames(tag);
            var expectedPatients = patients[0..5];

            // Make sure that [0..5] are the Patients with R in the family name.
            var returnedPatients = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&family=R&_sort=family&_count={count}");

            SortTestsAssert.AssertPatientFamilyNamesAreEqualInRange(count, expectedPatients, returnedPatients);
        }

        [SkippableFact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.CosmosDb)]
        public async Task GivenPatientsWithMultipleNamesForCosmos_WhenFilteringAndSortingByFamilyNameWithHyphen_ThenResourcesAreReturnedInAscendingOrder()
        {
            // Make sure that [0..5] are the Patients with R in the family name
            var tag = Guid.NewGuid().ToString();
            Patient[] patients = await CreatePatientsWithMultipleFamilyNames(tag);

            // Make sure that [0..5] are the Patients with R in the family name
            List<Patient> expectedPatients = new List<Patient>() { patients[4], patients[1], patients[2], patients[3], patients[0], };

            var returnedPatients = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&family=R&_sort=-family");

            SortTestsAssert.AssertNumberOfResources(expectedPatients, returnedPatients);
            SortTestsAssert.AssertPatientFamilyNamesAreEqualInRange(expectedPatients.Count, expectedPatients, returnedPatients);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenPatientsWithMultipleNamesForSql_WhenFilteringAndSortingByFamilyNameWithHyphen_ThenResourcesAreReturnedInAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();
            Patient[] patients = await CreatePatientsWithMultipleFamilyNames(tag);

            List<Patient> expectedPatients = new List<Patient>() { patients[4], patients[1], patients[2], patients[3], patients[0], };

            var returnedPatients = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&family=R&_sort=-family");

            SortTestsAssert.AssertNumberOfResources(expectedPatients, returnedPatients);
            SortTestsAssert.AssertPatientFamilyNamesAreEqualInRange(expectedPatients.Count, expectedPatients, returnedPatients);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenPatientsWithMultipleNamesAndPaginatedForSql_WhenFilteringAndSortingByFamilyNameWithHyphen_ThenResourcesAreReturnedInAscendingOrder(int count)
        {
            var tag = Guid.NewGuid().ToString();
            Patient[] patients = await CreatePatientsWithMultipleFamilyNames(tag);

            List<Patient> expectedPatients = new List<Patient>() { patients[4], patients[1], patients[2], patients[3], patients[0], };
            var returnedPatients = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&family=R&_sort=-family&_count={count}");

            SortTestsAssert.AssertNumberOfResources(expectedPatients.Take(count), returnedPatients.Take(count));
            SortTestsAssert.AssertPatientFamilyNamesAreEqualInRange(count, expectedPatients, returnedPatients);
        }

        /*
         * There is a difference in the way we break ties between Cosmos and SQL.
         * For Cosmos, we choose the resource based on last updated time ordered by overall sort order.
         * For SQL, we always choose the "oldest" resource based on last updated time (irrespective of overall sort order).
         * Hence we see a difference when sorting by Descending order.
        */
        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.CosmosDb)]
        public async Task GivenPatientsWithMultipleNamesAndPaginatedForCosmos_WhenFilteringAndSortingByFamilyNameWithHyphen_ThenResourcesAreReturnedInAscendingOrder(int count)
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail.
            var tag = Guid.NewGuid().ToString();
            Patient[] patients = await CreatePatientsWithMultipleFamilyNames(tag);

            List<Patient> expectedPatients = new List<Patient>() { patients[4], patients[1], patients[3], patients[2], patients[0], };
            var returnedPatients = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&family=R&_sort=-family&_count={count}");

            SortTestsAssert.AssertNumberOfResources(expectedPatients.Take(count), returnedPatients.Take(count));
            SortTestsAssert.AssertPatientFamilyNamesAreEqualInRange(count, expectedPatients, returnedPatients);
        }

        [Fact]
        public async Task GivenPatientsWithFamilyNameMissing_WhenSortingByFamilyName_ThenThosePatientsAreIncludedInResult()
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            Patient[] patients = await CreatePatientsWithMissingFamilyNames(tag);

            var expectedPatients = patients.OrderBy(x => x.Name.Min(y => y.Family)).ToArray();
            var returnedPatients = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort=family");

            // This test should compare the number of records returned. It's possible that results will not be in the same sequence as expected,
            // but as this test is validating if the resources are the expected ones, then comparing the number of elements returned is the
            // main priority of this test.
            SortTestsAssert.AssertNumberOfResources(expectedPatients, returnedPatients);

            // Comparing the city names to ensure the results are the same as the ingested.
            SortTestsAssert.AssertRangeOfCitiesInPatientsCollections(expectedPatients, returnedPatients.Cast<Patient>().ToArray());
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public async Task GivenPatientsWithFamilyNameMissingAndPaginated_WhenSortingByFamilyName_ThenThosePatientsAreIncludedInResult(int count)
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            Patient[] patients = await CreatePatientsWithMissingFamilyNames(tag);

            var expectedPatients = patients.OrderBy(x => x.Name.Min(y => y.Family)).ToArray();
            var returnedPatients = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort=family&_count={count}");

            // This test should compare the number of records returned. It's possible that results will not be in the same sequence as expected,
            // but as this test is validating if the resources are the expected ones, then comparing the number of elements returned is the
            // main priority of this test.
            SortTestsAssert.AssertNumberOfResources(expectedPatients, returnedPatients);

            // Comparing the city names to ensure the results are the same as the ingested.
            SortTestsAssert.AssertRangeOfCitiesInPatientsCollections(expectedPatients, returnedPatients.Cast<Patient>().ToArray());
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenPatientsWithFamilyNameMissingAndPaginatedForSql_WhenSortingByFamilyNameWithHyphen_ThenThosePatientsAreIncludedInResult(int count)
        {
            var tag = Guid.NewGuid().ToString();
            Patient[] patients = await CreatePatientsWithMissingFamilyNames(tag);

            var expectedPatients = new Patient[]
                {
                    patients[0],
                    patients[4],
                    patients[6],
                    patients[5],
                    patients[1],
                    patients[2],
                    patients[3],
                };

            var returnedPatients = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort=-family&_count={count}");

            SortTestsAssert.AssertNumberOfResources(expectedPatients.Take(count), returnedPatients.Take(count));
            SortTestsAssert.AssertPatientFamilyNamesAreEqualInRange(count, expectedPatients, returnedPatients);
        }

        /*
         * There is a difference in the way we break ties between Cosmos and SQL.
         * For Cosmos, we choose the resource based on overall sort order.
         * For SQL, we always choose the oldest resource (irrespective of overall sort order).
         * Hence we see a difference when sorting by Descending order.
         * */

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.CosmosDb)]
        public async Task GivenPatientsWithFamilyNameMissingAndPaginatedForCosmos_WhenSortingByFamilyNameWithHyphen_ThenThosePatientsAreIncludedInResult(int count)
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            Patient[] patients = await CreatePatientsWithMissingFamilyNames(tag);

            var expectedPatients = patients.OrderBy(x => x.Name.Min(y => y.Family)).Reverse().ToArray();

            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort=-family&_count={count}");

            SortTestsAssert.AssertNumberOfResources(expectedPatients, returnedResults);
            SortTestsAssert.AssertPatientFamilyNamesAreEqualInRange(count, expectedPatients, returnedResults);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public async Task GivenPatientsWithFamilyNameMissingAndPaginated_WhenSortingByFamilyNameWithTotal_ThenCorrectTotalReturned(int count)
        {
            var tag = Guid.NewGuid().ToString();
            Patient[] patients = await CreatePatientsWithMissingFamilyNames(tag);

            var expectedPatients = patients.OrderBy(x => x.Name.Min(y => y.Family)).ToArray();

            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort=family&_count={count}&_total=accurate");

            SortTestsAssert.AssertNumberOfResources(expectedPatients, returnedResults);
        }

        [Fact]
        public async Task GivenPatientsWithFamilyNameMissing_WhenSortingByFamilyNameWithTotal_ThenCorrectTotalReturned()
        {
            var tag = Guid.NewGuid().ToString();
            Patient[] expectedPatients = await CreatePatientsWithMissingFamilyNames(tag);

            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?_tag={tag}&_sort=family&_total=accurate");

            SortTestsAssert.AssertNumberOfResources(expectedPatients, returnedResults);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatientWithManagingOrg_WhenSearchedWithOrgIdentifierAndSorted_ThenPatientsAreReturned()
        {
            // Arrange Patients with linked Managing Organization
            var tag = Guid.NewGuid().ToString();

            var org = Samples.GetDefaultOrganization().ToPoco<Organization>();
            org.Identifier.Add(new Identifier("http://e2etest", tag));
            var orgResponse = await Client.CreateAsync(org);

            var patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            patient.ManagingOrganization = new ResourceReference($"{KnownResourceTypes.Organization}/{orgResponse.Resource.Id}");

            var patients = new List<Patient>();

            SetPatientInfo(patient, "Seattle", "Robinson", tag);
            patients.Add((await Client.CreateAsync(patient)).Resource);

            SetPatientInfo(patient, "Portland", "Williams", tag);
            patients.Add((await Client.CreateAsync(patient)).Resource);

            var expectedPatients = patients.OrderByDescending(x => x.Name.Max(n => n.Family)).Cast<Resource>().ToArray();

            // Uses both chained search + sort
            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?organization.identifier={tag}&_sort=-family");

            SortTestsAssert.AssertNumberOfResources(expectedPatients, returnedResults);
            SortTestsAssert.AssertPatientFamilyNamesAreEqualInRange(expectedPatients.Length, expectedPatients, returnedResults);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenPatientWithManagingOrg_WhenSearchedWithOrgNameAndSortedByName_ThenPatientsAreReturned()
        {
            // Arrange Patients with linked Managing Organization
            var tag = Guid.NewGuid().ToString();

            var org = Samples.GetDefaultOrganization().ToPoco<Organization>();
            org.Identifier.Add(new Identifier("http://e2etest", tag));
            org.Name = "TESTINGCHAINEDSORT";
            var orgResponse = await Client.CreateAsync(org);

            var patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            patient.ManagingOrganization = new ResourceReference($"{KnownResourceTypes.Organization}/{orgResponse.Resource.Id}");

            var patients = new List<Patient>();

            SetPatientInfo(patient, "Seattle", "Robinson", tag);
            patients.Add((await Client.CreateAsync(patient)).Resource);

            SetPatientInfo(patient, "Portland", "Williams", tag);
            patients.Add((await Client.CreateAsync(patient)).Resource);

            // ASC BY NAME
            var expectedPatients = patients.OrderBy(x => x.Name.Max(n => n.Family)).Cast<Resource>().ToArray();
            var returnedResults = await GetResultsFromAllPagesAsync($"Patient?organization.identifier={tag}&organization.name=TESTINGCHAINEDSORT&_sort=name");

            SortTestsAssert.AssertNumberOfResources(expectedPatients, returnedResults);
            SortTestsAssert.AssertPatientFamilyNamesAreEqualInRange(expectedPatients.Length, expectedPatients, returnedResults);

            // DESC BY NAME
            expectedPatients = patients.OrderByDescending(x => x.Name.Max(n => n.Family)).Cast<Resource>().ToArray();
            returnedResults = await GetResultsFromAllPagesAsync($"Patient?organization.identifier={tag}&organization.name=TESTINGCHAINEDSORT&_sort=-name");

            SortTestsAssert.AssertNumberOfResources(expectedPatients, returnedResults);
            SortTestsAssert.AssertPatientFamilyNamesAreEqualInRange(expectedPatients.Length, expectedPatients, returnedResults);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenOrg_WhenSearchedWithPartOfAndSortedByName_ThenOrgsAreReturned()
        {
            var tagP = Guid.NewGuid().ToString();
            var orgP = Samples.GetDefaultOrganization().ToPoco<Organization>();
            orgP.Identifier.Add(new Identifier("http://e2etest", tagP));
            orgP.Name = "Parent";
            var orgResponseP = await Client.CreateAsync(orgP);

            var tag1 = Guid.NewGuid().ToString();
            var org1 = Samples.GetDefaultOrganization().ToPoco<Organization>();
            org1.Identifier.Add(new Identifier("http://e2etest", tag1));
            org1.Name = "MSFT1";
            org1.PartOf = new ResourceReference($"{KnownResourceTypes.Organization}/{orgResponseP.Resource.Id}");
            var orgResponse1 = await Client.CreateAsync(org1);

            var orgs = new List<Organization>();
            orgs.Add(orgResponse1.Resource);

            var tag2 = Guid.NewGuid().ToString();
            var org2 = Samples.GetDefaultOrganization().ToPoco<Organization>();
            org2.Identifier.Add(new Identifier("http://e2etest", tag2));
            org2.Name = "MSFT2";
            org2.PartOf = new ResourceReference($"{KnownResourceTypes.Organization}/{orgResponseP.Resource.Id}");
            var orgResponse2 = await Client.CreateAsync(org2);
            orgs.Add(orgResponse2.Resource);

            // ASC BY NAME
            var expectedOrganizations = orgs.OrderBy(x => x.Name).Cast<Resource>().ToArray();
            var returnedResults = await GetResultsFromAllPagesAsync($"Organization?partof.identifier={tagP}&partof.name=Parent&_sort=name");

            SortTestsAssert.AssertNumberOfResources(expectedOrganizations, returnedResults);
            SortTestsAssert.AssertOrganizationNamesAreEqualInRange(expectedOrganizations.Length, expectedOrganizations, returnedResults);

            // DESC BY NAME
            expectedOrganizations = orgs.OrderByDescending(x => x.Name).Cast<Resource>().ToArray();
            returnedResults = await GetResultsFromAllPagesAsync($"Organization?partof.identifier={tagP}&partof.name=Parent&_sort=-name");

            SortTestsAssert.AssertNumberOfResources(expectedOrganizations, returnedResults);
            SortTestsAssert.AssertOrganizationNamesAreEqualInRange(expectedOrganizations.Length, expectedOrganizations, returnedResults);
        }

        private async Task<Patient[]> CreatePatients(string tag)
        {
            // Create various resources.
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(
                p => SetPatientInfo(p, "Seattle", "Robinson", tag, DateTime.Now.Subtract(TimeSpan.FromDays(90))),
                p => SetPatientInfo(p, "Portland", "Williams", tag, DateTime.Now.Subtract(TimeSpan.FromDays(60))),
                p => SetPatientInfo(p, "New York", "Williamas", tag, DateTime.Now.Subtract(TimeSpan.FromDays(40))),
                p => SetPatientInfo(p, "Seattle", "Jones", tag, DateTime.Now.Subtract(TimeSpan.FromDays(30))));

            return patients;
        }

        private async Task<Patient[]> CreatePaginatedPatients(string tag)
        {
            // Create various resources.
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(
                p => SetPatientInfo(p, "Seattle", "Robinson", tag, new DateTime(1940, 01, 15)),
                p => SetPatientInfo(p, "Portland", "Williamas", tag, new DateTime(1942, 01, 15)),
                p => SetPatientInfo(p, "Portland", "James", tag, new DateTime(1943, 10, 23)),
                p => SetPatientInfo(p, "Seatt;e", "Alex", tag, new DateTime(1943, 11, 23)),
                p => SetPatientInfo(p, "Portland", "Rock", tag, new DateTime(1944, 06, 24)),
                p => SetPatientInfo(p, "Seattle", "Mike", tag, new DateTime(1946, 02, 24)),
                p => SetPatientInfo(p, "Portland", "Christie", tag, new DateTime(1947, 02, 24)),
                p => SetPatientInfo(p, "Portland", "Lone", tag, new DateTime(1950, 05, 12)),
                p => SetPatientInfo(p, "Seattle", "Sophie", tag, new DateTime(1953, 05, 12)),
                p => SetPatientInfo(p, "Portland", "Peter", tag, new DateTime(1956, 06, 12)),
                p => SetPatientInfo(p, "Portland", "Cathy", tag, new DateTime(1960, 09, 22)),
                p => SetPatientInfo(p, "Seattle", "Jones", tag, new DateTime(1970, 05, 13)));

            return patients;
        }

        private async Task<Patient[]> CreatePatientsWithSameBirthdate(string tag)
        {
            // Create various resources.
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(
                p => SetPatientInfo(p, "Seattle", "Robinson", tag),
                p => SetPatientInfo(p, "Portland", "Williams", tag),
                p => SetPatientInfo(p, "Portland", "James", tag),
                p => SetPatientInfo(p, "Seattle", "Alex", tag),
                p => SetPatientInfo(p, "Portland", "Rock", tag));

            return patients;
        }

        private async Task<Patient[]> CreatePatientsWithMissingFamilyNames(string tag)
        {
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(
                p => SetPatientInfo(p, "Portland", "Williams", tag),
                p => SetPatientInfo(p, "Vancouver", family: null, tag),
                p => SetPatientInfo(p, "Bellingham", family: null, tag),
                p => SetPatientInfo(p, "Bend", family: null, tag),
                p => SetPatientInfo(p, "Seattle", "Mary", tag),
                p => SetPatientInfo(p, "Portland", "Cathy", tag),
                p => SetPatientInfo(p, "Seattle", "Jones", tag));

            return patients;
        }

        private async Task<Patient[]> CreatePatientsWithMultipleFamilyNames(string tag)
        {
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(
                p => SetPatientInfo(p, "Portland", new List<string>() { "Rasputin", "Alex" }, tag),
                p => SetPatientInfo(p, "Portland", new List<string>() { "Christie", "James", "Rock" }, tag),
                p => SetPatientInfo(p, "Seattle", new List<string>() { "Robinson", "Ragnarok" }, tag),
                p => SetPatientInfo(p, "Portland", new List<string>() { "Robinson", "Ragnarok" }, tag),
                p => SetPatientInfo(p, "Seattle", new List<string>() { "Rasputin", "Ye" }, tag),
                p => SetPatientInfo(p, "Seattle", new List<string>() { "Mike", "Duke" }, tag),
                p => SetPatientInfo(p, "Portland", "Cathy", tag),
                p => SetPatientInfo(p, "Seattle", "Jones", tag));

            return patients;
        }

        private void SetPatientInfo(Patient patient, string city, string family, string tag)
        {
            SetPatientInfoInternal(patient, city, family, tag, "1970-01-01");
        }

        private void SetPatientInfo(Patient patient, string city, List<string> familyNames, string tag)
        {
            SetPatientInfoInternal(patient, city, familyNames, tag, "1970-01-01");
        }

        private void SetPatientInfo(Patient patient, string city, string family, string tag, DateTime birthDate)
        {
            // format according as expected
            SetPatientInfoInternal(patient, city, family, tag, birthDate.ToString("yyyy-MM-dd"));
        }

        private void SetPatientInfoInternal(Patient patient, string city, string family, string tag, string birthDate)
        {
            SetPatientInfoInternal(patient, city, new List<string>() { family }, tag, birthDate);
        }

        private void SetPatientInfoInternal(Patient patient, string city, List<string> family, string tag, string birthDate)
        {
            patient.Meta = new Meta { Tag = new List<Coding> { new Coding(null, tag) }, };

            patient.Address = new List<Address>
            {
                new Address
                {
                    City = city,
                },
            };

            var familyNames = new List<HumanName>();
            foreach (string name in family)
            {
                familyNames.Add(new HumanName { Family = name });
            }

            patient.Name = familyNames;
            patient.BirthDate = birthDate;
        }

        private void SetObservationInfo(Observation observation, string date, string tag, Patient patient = null)
        {
            observation.Status = ObservationStatus.Final;
            observation.Code = new CodeableConcept
            {
                Coding = new List<Coding> { new Coding(null, tag) },
            };
            observation.Meta = new Meta { Tag = new List<Coding> { new Coding(null, tag) }, };
            observation.Effective = new FhirDateTime(date);
            if (patient != null)
            {
                observation.Subject = new ResourceReference($"Patient/{patient.Id}");
            }
        }

        private async Task<Observation[]> AddObservationToPatient(Patient patient, string observationDate, string tag)
        {
            return await Client.CreateResourcesAsync<Observation>(
                o => SetObservationInfo(o, observationDate, tag, patient));
        }

        private static class SortTestsAssert
        {
            public static void AssertNumberOfResources<T1, T2>(IEnumerable<T1> expected, IEnumerable<T2> current)
            {
                Assert.True(expected.Count() == current.Count(), $"The excepted number of records between both collections is different. Expected {expected.Count()}. Current {current.Count()}");
            }

            /// <summary>
            /// Assert if collections have the same sequence of family names.
            /// </summary>
            public static void AssertPatientFamilyNamesAreEqualInRange(int count, IReadOnlyList<Resource> expected, IReadOnlyList<Resource> current)
            {
                for (int i = 0; i < count; i++)
                {
                    Patient expectedAsPatient = expected[i] as Patient;
                    Assert.True(expectedAsPatient != null, "Expected resource is not a Patient.");

                    Patient currentAsPatient = current[i] as Patient;
                    Assert.True(currentAsPatient != null, "Current resource is not a Patient.");

                    Assert.Equal(expectedAsPatient.Name.First().Family, currentAsPatient.Name.First().Family);
                }
            }

            /// <summary>
            /// Assert if collections have the same sequence of organization names.
            /// </summary>
            public static void AssertOrganizationNamesAreEqualInRange(int count, IReadOnlyList<Resource> expected, IReadOnlyList<Resource> current)
            {
                for (int i = 0; i < count; i++)
                {
                    Organization expectedAsOrganization = expected[i] as Organization;
                    Assert.True(expectedAsOrganization != null, "Expected resource is not an Organization.");

                    Organization currentAsOrganization = current[i] as Organization;
                    Assert.True(currentAsOrganization != null, "Current resource is not an Organization.");

                    Assert.Equal(expectedAsOrganization.Name, currentAsOrganization.Name);
                }
            }

            public static void AssertResourcesAreInAscendingOrderByLastUpdateInRange(int start, int end, IReadOnlyList<Resource> current)
            {
                DateTimeOffset? lastUpdated = current[start].Meta.LastUpdated;
                for (int i = start + 1; i < end; i++)
                {
                    DateTimeOffset? currentLastUpdated = current[i].Meta.LastUpdated;

                    Assert.True(currentLastUpdated >= lastUpdated, $"Results were supposed to be returned in asc order. Last updated is lower than the expected. '{lastUpdated}' was supposed to be returned after than '{currentLastUpdated}'.");
                    lastUpdated = currentLastUpdated;
                }
            }

            public static void AssertResourcesAreInDescendingOrderByLastUpdateInRange(int start, int end, IReadOnlyList<Resource> current)
            {
                DateTimeOffset? lastUpdated = current[start].Meta.LastUpdated;
                for (int i = start + 1; i < end; i++)
                {
                    DateTimeOffset? currentLastUpdated = current[i].Meta.LastUpdated;

                    Assert.True(currentLastUpdated <= lastUpdated, $"Results were supposed to be returned in desc order. Last updated is greater than the expected. '{lastUpdated}' was supposed to be returned first than '{currentLastUpdated}'.");
                    lastUpdated = currentLastUpdated;
                }
            }

            public static void AssertPatientBirthDateAscendingOrderInRange(int start, int end, IReadOnlyList<Resource> current)
            {
                const string invalidCastErrorMessage = "Current resource is not a Patient.";

                Patient patient = current[start] as Patient;
                Assert.True(patient != null, invalidCastErrorMessage);

                DateTime birthDate = DateTime.Parse(patient.BirthDate);
                for (int i = start + 1; i < end; i++)
                {
                    Patient currentPatient = current[i] as Patient;
                    Assert.True(patient != null, invalidCastErrorMessage);

                    DateTime currentBirthDate = DateTime.Parse(currentPatient.BirthDate);

                    Assert.True(currentBirthDate >= birthDate, $"Patient // Results were supposed to be returned in asc order. Birth date is lower than the expected. '{birthDate}' was supposed to be returned after than '{currentBirthDate}'.");
                    birthDate = currentBirthDate;
                }
            }

            public static void AssertPatientBirthDateDescendingOrderInRange(int start, int end, IReadOnlyList<Resource> current)
            {
                const string invalidCastErrorMessage = "Current resource is not a Patient.";

                Patient patient = current[start] as Patient;
                Assert.True(patient != null, invalidCastErrorMessage);

                DateTime birthDate = DateTime.Parse(patient.BirthDate);
                for (int i = start + 1; i < end; i++)
                {
                    Patient currentPatient = current[i] as Patient;
                    Assert.True(patient != null, invalidCastErrorMessage);

                    DateTime currentBirthDate = DateTime.Parse(currentPatient.BirthDate);

                    Assert.True(currentBirthDate <= birthDate, $"Patient // Results were supposed to be returned in desc order. Birth date is greater than the expected. '{birthDate}' was supposed to be returned first than '{currentBirthDate}'.");
                    birthDate = currentBirthDate;
                }
            }

            public static void AssertRangeOfCitiesInPatientsCollections(IReadOnlyList<Patient> expectedPatients, IReadOnlyList<Patient> currentPatients)
            {
                var expectedCities = expectedPatients.Select(p => p.Address.First().City).OrderBy(c => c);
                var resultedCities = currentPatients.Select(p => p.Address.First().City).OrderBy(c => c);
                Assert.Equal(expectedCities, resultedCities);
            }

            public static void AssertObservationEffectiveDateAscendingOrderInRange(int start, int end, IReadOnlyList<Resource> current)
            {
                const string invalidCastErrorMessage = "Current resource is not an Observation.";

                Observation observation = current[start] as Observation;
                Assert.True(observation != null, invalidCastErrorMessage);

                FhirDateTime date = (FhirDateTime)observation.Effective;
                for (int i = start + 1; i < end; i++)
                {
                    Observation currentObservation = current[i] as Observation;
                    Assert.True(observation != null, invalidCastErrorMessage);

                    FhirDateTime currentDate = (FhirDateTime)currentObservation.Effective;

                    Assert.True(currentDate >= date, $"Observation // Results were supposed to be returned in desc order. Effective date is greater than the expected. '{date}' was supposed to be returned first than '{currentDate}'.");
                    date = currentDate;
                }
            }

            public static void AssertObservationEffectiveDateDescendingOrderInRange(int start, int end, IReadOnlyList<Resource> current)
            {
                const string invalidCastErrorMessage = "Current resource is not an Observation.";

                Observation observation = current[start] as Observation;
                Assert.True(observation != null, invalidCastErrorMessage);

                FhirDateTime date = (FhirDateTime)observation.Effective;
                for (int i = start + 1; i < end; i++)
                {
                    Observation currentObservation = current[i] as Observation;
                    Assert.True(observation != null, invalidCastErrorMessage);

                    FhirDateTime currentDate = (FhirDateTime)currentObservation.Effective;

                    Assert.True(currentDate <= date, $"Observation // Results were supposed to be returned in desc order. Effective date is greater than the expected. '{date}' was supposed to be returned first than '{currentDate}'.");
                    date = currentDate;
                }
            }

            public static void AssertResourceTypeInRange<T>(int start, int end, System.Collections.Generic.IReadOnlyList<Resource> current)
                where T : class
            {
                for (int i = start; i < end; i++)
                {
                    T resourceAsType = current[i] as T;
                    Assert.True(resourceAsType != null, $"Resource was supposed to be of type '{nameof(T)}'.");
                }
            }
        }
    }
}
