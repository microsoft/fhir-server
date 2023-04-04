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
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=_lastUpdated", false, patients.OrderBy(p => p.Meta.LastUpdated).Cast<Resource>().ToArray());
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatients_WhenSearchedWithSortParamsWithHyphen_ThenPatientsAreReturnedInTheDescendingOrder()
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatients(tag);

            // Due to multiple service instances lastUpdated time could vary, and patients array might not be in the ascending order by lastUpdated. So sort the expected patients array
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=-_lastUpdated", false, patients.OrderByDescending(p => p.Meta.LastUpdated).Cast<Resource>().ToArray());
        }

        [SkippableTheory]
        [InlineData("birthdate")]
        [InlineData("_lastUpdated")]
        public async Task GivenMoreThanTenPatients_WhenSearchedWithSortParam_ThenPatientsAreReturnedInAscendingOrder(string sortParameterName)
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePaginatedPatients(tag);

            if (sortParameterName.Equals("birthdate", StringComparison.OrdinalIgnoreCase))
            {
                // CreatePaginatedPatients - Creates Patients array in the increasing order of BirthDates. Sorting to avoid failures
                patients = patients.OrderBy(p => p.BirthDate).ToArray();
            }
            else if (sortParameterName.Equals("_lastUpdated", StringComparison.OrdinalIgnoreCase))
            {
                // Due to multiple service instances lastUpdated time could vary and patients array might not be in the ascending order by lastUpdated. So sort the expected patients array
                patients = patients.OrderBy(p => p.Meta.LastUpdated).ToArray();
            }

            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort={sortParameterName}", false, patients.Cast<Resource>().ToArray());
        }

        [SkippableTheory]
        [InlineData("birthdate")]
        [InlineData("_lastUpdated")]
        public async Task GivenMoreThanTenPatients_WhenSearchedWithSortParamWithHyphen_ThenPatientsAreReturnedInDescendingOrder(string sortParameterName)
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePaginatedPatients(tag);
            if (sortParameterName.Equals("birthdate", StringComparison.OrdinalIgnoreCase))
            {
                // CreatePaginatedPatients - Creates Patients array in the increasing order of BirthDates. Sorting to avoid failures
                patients = patients.OrderBy(p => p.BirthDate).ToArray();
            }
            else if (sortParameterName.Equals("_lastUpdated", StringComparison.OrdinalIgnoreCase))
            {
                // Due to multiple service instances lastUpdated time could vary and patients array might not be in the ascending order by lastUpdated. So sort the expected patients array
                patients = patients.OrderBy(p => p.Meta.LastUpdated).ToArray();
            }

            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=-{sortParameterName}", false, patients.Reverse().Cast<Resource>().ToArray());
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
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort={sortParameterName}&_count=3", false, pageSize: 3, patients.OrderBy(p => p.Meta.LastUpdated).ToArray());
        }

        [SkippableFact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.CosmosDb)]
        public async Task GivenPatientsWithSameBirthdateAndMultiplePages_WhenSortedByBirthdate_ThenPatientsAreReturnedInAscendingOrder()
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatientsWithSameBirthdate(tag);

            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=birthdate&_count=3", false, pageSize: 3, patients.Cast<Resource>().ToArray());
        }

        [SkippableFact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.CosmosDb)]
        public async Task GivenPatientsWithSameBirthdateAndMultiplePages_WhenSortedByBirthdateWithHyphen_ThenPatientsAreReturnedInDescendingOrder()
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatientsWithSameBirthdate(tag);

            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=-birthdate&_count=3", false, pageSize: 3, patients.Reverse().Cast<Resource>().ToArray());
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

        [SkippableFact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatients_WhenSearchedWithFamilySortParams_ThenPatientsAreReturnedInTheAscendingOrder()
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatients(tag);

            // We are ordering the patients array by Family name
            await ExecuteAndValidateBundle(
                $"Patient?_tag={tag}&_sort=family",
                false,
                patients.OrderBy(x => x.Name.Min(n => n.Family)).Cast<Resource>().ToArray());
        }

        [SkippableFact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatients_WhenSearchedWithFamilySortParamsWithHyphen_ThenPatientsAreReturnedInTheDescendingOrder()
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatients(tag);

            // We are ordering the patients array by Family name
            await ExecuteAndValidateBundle(
                $"Patient?_tag={tag}&_sort=-family",
                false,
                patients.OrderByDescending(x => x.Name.Max(n => n.Family)).Cast<Resource>().ToArray());
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

        [SkippableFact]
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
            await ExecuteAndValidateBundle($"Patient?_lastUpdated=gt{lastUpdated}&_sort=birthdate&_tag={tag}", false, patients.OrderBy(x => x.BirthDate).Cast<Resource>().ToArray());
        }

        [SkippableFact]
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
            await ExecuteAndValidateBundle($"Patient?_lastUpdated=gt{lastUpdated}&_sort=-birthdate&_tag={tag}", false, patients.OrderByDescending(x => x.BirthDate).Cast<Resource>().ToArray());
        }

        [SkippableFact]
        public async Task GivenQueryWithTagFilter_WhenSearchedWithSortParamOnDatetime_ThenResourcesAreReturnedInAscendingOrder()
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();

            var patients = await CreatePatients(tag);

            // Ask to get all patient with specific tag order by birthdate (timestamp)
            // filter and sort are different based on different types

            // CreatePatients - Creates patients with birthdates in Ascending order, sort to avoid failures
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=birthdate", false, patients.OrderBy(x => x.BirthDate).Cast<Resource>().ToArray());
        }

        [SkippableFact]
        public async Task GivenQueryWithTagFilter_WhenSearchedWithHyphenSortParamOnDatetime_ThenResourcesAreReturnedInDescendingOrder()
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();

            // create the resources which will have an timestamp bigger than the 'now' var
            var patients = await CreatePatients(tag);

            // Ask to get all patient with specific tag order by birthdate (timestamp)
            // filter and sort are different based on different types

            // CreatePatients - Creates patients with birthdates in Ascending order, sort to avoid failures
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=-birthdate", false, patients.OrderByDescending(x => x.BirthDate).Cast<Resource>().ToArray());
        }

        [SkippableFact]
        public async Task GivenQueryWithMultipleFilters_WhenSearchedWithSortParamOnDatetime_ThenResourcesAreReturnedInAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            var patients = await CreatePatients(tag);

            // Ask to get all patient with specific tag and family name order by birthdate (timestamp)
            // filter and sort are different based on different types
            var filteredFamilyName = "Williamas";
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&family={filteredFamilyName}&_sort=birthdate", false, patients.Reverse().Where(x => x.Name[0].Family == filteredFamilyName).OrderBy(x => x.BirthDate).Cast<Resource>().ToArray());
        }

        [SkippableFact]
        public async Task GivenQueryWithMultipleFilters_WhenSearchedWithHyphenSortParamOnDatetime_ThenResourcesAreReturnedInDescendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            var patients = await CreatePatients(tag);

            // Ask to get all patient with specific tag and family name order by birthdate (timestamp)
            // filter and sort are different based on different types
            var filteredFamilyName = "Williamas";
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&family={filteredFamilyName}&_sort=-birthdate", false, patients.Where(x => x.Name[0].Family == filteredFamilyName).OrderByDescending(x => x.BirthDate).Cast<Resource>().ToArray());
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
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=birthdate&_revinclude=Observation:subject", false, resources.ToArray());
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
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=-birthdate&_revinclude=Observation:subject", false, resources.ToArray());
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
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=_lastUpdated&_revinclude=Observation:subject", false, resources.ToArray());
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
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=-_lastUpdated&_revinclude=Observation:subject", false, resources.ToArray());
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
            await ExecuteAndValidateBundle($"Observation?_tag={tag}&_sort=date&_include=Observation:subject", false, resources.ToArray());
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
            await ExecuteAndValidateBundle($"Observation?_tag={tag}&_sort=-date&_include=Observation:subject", false, resources.ToArray());
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
            await ExecuteAndValidateBundle($"Observation?_tag={tag}&_sort=date&subject:missing=false", false, expected_resources.ToArray());
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
            await ExecuteAndValidateBundle($"Observation?_tag={tag}&_sort=date&subject:missing=true", false, expected_resources.ToArray());
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
            await ExecuteAndValidateBundle($"Observation?_tag={tag}&_sort=_lastUpdated&subject:missing=true", false, expected_resources.ToArray());
        }

        [SkippableFact]
        public async Task GivenPatientsWithMultipleNames_WhenFilteringAndSortingByFamilyName_ThenResourcesAreReturnedInAscendingOrder()
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatientsWithMultipleFamilyNames(tag);

            // Make sure that [0..5] are the Patients with R in the family name
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&family=R&_sort=family", sort: false, patients[0..5]);
        }

        [SkippableTheory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public async Task GivenPatientsWithMultipleNamesAndPaginated_WhenFilteringAndSortingByFamilyName_ThenResourcesAreReturnedInAscendingOrder(int count)
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatientsWithMultipleFamilyNames(tag);

            // Make sure that [0..5] are the Patients with R in the family name
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&family=R&_sort=family&_count={count}", sort: false, pageSize: count, patients[0..5]);
        }

        [SkippableFact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.CosmosDb)]
        public async Task GivenPatientsWithMultipleNamesForCosmos_WhenFilteringAndSortingByFamilyNameWithHyphen_ThenResourcesAreReturnedInAscendingOrder()
        {
            // Make sure that [0..5] are the Patients with R in the family name
            var tag = Guid.NewGuid().ToString();
            Patient[] patients = await CreatePatientsWithMultipleFamilyNames(tag);

            // Make sure that [0..5] are the Patients with R in the family name
            List<Patient> expectedPatients = new List<Patient>() { patients[4], patients[1], patients[3], patients[2], patients[0], };
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&family=R&_sort=-family", sort: false, expectedPatients.ToArray());
        }

        [SkippableFact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenPatientsWithMultipleNamesForSql_WhenFilteringAndSortingByFamilyNameWithHyphen_ThenResourcesAreReturnedInAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();
            Patient[] patients = await CreatePatientsWithMultipleFamilyNames(tag);

            List<Patient> expectedPatients = new List<Patient>() { patients[4], patients[1], patients[2], patients[3], patients[0], };
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&family=R&_sort=-family", sort: false, expectedPatients.ToArray());
        }

        [SkippableTheory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenPatientsWithMultipleNamesAndPaginatedForSql_WhenFilteringAndSortingByFamilyNameWithHyphen_ThenResourcesAreReturnedInAscendingOrder(int count)
        {
            var tag = Guid.NewGuid().ToString();
            Patient[] patients = await CreatePatientsWithMultipleFamilyNames(tag);

            List<Patient> expectedPatients = new List<Patient>() { patients[4], patients[1], patients[2], patients[3], patients[0], };
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&family=R&_sort=-family&_count={count}", sort: false, pageSize: count, expectedPatients.ToArray());
        }

        /*
         * There is a difference in the way we break ties between Cosmos and SQL.
         * For Cosmos, we choose the resource based on last updated time ordered by overall sort order.
         * For SQL, we always choose the "oldest" resource based on last updated time (irrespective of overall sort order).
         * Hence we see a difference when sorting by Descending order.
        */
        [SkippableTheory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.CosmosDb)]
        public async Task GivenPatientsWithMultipleNamesAndPaginatedForCosmos_WhenFilteringAndSortingByFamilyNameWithHyphen_ThenResourcesAreReturnedInAscendingOrder(int count)
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            Patient[] patients = await CreatePatientsWithMultipleFamilyNames(tag);

            List<Patient> expectedPatients = new List<Patient>() { patients[4], patients[1], patients[3], patients[2], patients[0], };
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&family=R&_sort=-family&_count={count}", sort: false, pageSize: count, expectedPatients.ToArray());
        }

        [SkippableFact]
        public async Task GivenPatientsWithFamilyNameMissing_WhenSortingByFamilyName_ThenThosePatientsAreIncludedInResult()
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            Patient[] patients = await CreatePatientsWithMissingFamilyNames(tag);

            var expectedPatients = patients.OrderBy(x => x.Name.Min(y => y.Family)).ToArray();
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=family", sort: false, expectedPatients);
        }

        [SkippableTheory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public async Task GivenPatientsWithFamilyNameMissingAndPaginated_WhenSortingByFamilyName_ThenThosePatientsAreIncludedInResult(int count)
        {
            // For COSMOS DB - If sort indices are not stored then the sorting order will be incorrect and test will fail
            var tag = Guid.NewGuid().ToString();
            Patient[] patients = await CreatePatientsWithMissingFamilyNames(tag);

            var expectedPatients = patients.OrderBy(x => x.Name.Min(y => y.Family)).ToArray();
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=family&_count={count}", sort: false, pageSize: count, expectedPatients);
        }

        [SkippableTheory]
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

            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=-family&_count={count}", sort: false, pageSize: count, expectedPatients);
        }

        /*
         * There is a difference in the way we break ties between Cosmos and SQL.
         * For Cosmos, we choose the resource based on overall sort order.
         * For SQL, we always choose the oldest resource (irrespective of overall sort order).
         * Hence we see a difference when sorting by Descending order.
         * */

        [SkippableTheory]
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
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=-family&_count={count}", sort: false, pageSize: count, expectedPatients);
        }

        [SkippableTheory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public async Task GivenPatientsWithFamilyNameMissingAndPaginated_WhenSortingByFamilyNameWithTotal_ThenCorrectTotalReturned(int count)
        {
            var tag = Guid.NewGuid().ToString();
            await CreatePatientsWithMissingFamilyNames(tag);

            var response = await Client.SearchAsync($"Patient?_tag={tag}&_sort=family&_count={count}&_total=accurate");
            Assert.Equal(7, response.Resource.Total);
        }

        [SkippableFact]
        public async Task GivenPatientsWithFamilyNameMissing_WhenSortingByFamilyNameWithTotal_ThenCorrectTotalReturned()
        {
            var tag = Guid.NewGuid().ToString();
            await CreatePatientsWithMissingFamilyNames(tag);

            var response = await Client.SearchAsync($"Patient?_tag={tag}&_sort=family&_total=accurate");
            Assert.Equal(7, response.Resource.Total);
        }

        [SkippableFact]
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

            // Uses both chained search + sort
            await ExecuteAndValidateBundle(
                $"Patient?organization.identifier={tag}&_sort=-family",
                false,
                patients.OrderByDescending(x => x.Name.Max(n => n.Family)).Cast<Resource>().ToArray());
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

            // Uses both chained search + sort
            await ExecuteAndValidateBundle(
                $"Patient?organization.identifier={tag}&organization.name=TESTINGCHAINEDSORT&_sort=name",
                false,
                patients.OrderBy(x => x.Name.Max(n => n.Family)).Cast<Resource>().ToArray());

            // Uses both chained search + sort
            await ExecuteAndValidateBundle(
                $"Patient?organization.identifier={tag}&organization.name=TESTINGCHAINEDSORT&_sort=-name",
                false,
                patients.OrderByDescending(x => x.Name.Max(n => n.Family)).Cast<Resource>().ToArray());
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

            // Uses both chained search + sort
            await ExecuteAndValidateBundle(
                $"Organization?partof.identifier={tagP}&partof.name=Parent&_sort=name",
                false,
                orgs.OrderBy(x => x.Name).Cast<Resource>().ToArray());

            // Uses both chained search + sort
            await ExecuteAndValidateBundle(
                $"Organization?partof.identifier={tagP}&partof.name=Parent&_sort=-name",
                false,
                orgs.OrderByDescending(x => x.Name).Cast<Resource>().ToArray());
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
    }
}
