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
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class SortTests : SearchTestsBase<HttpIntegrationTestFixture>
    {
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

            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=_lastUpdated", false, patients.Cast<Resource>().ToArray());
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatients_WhenSearchedWithSortParamsWithHyphen_ThenPatientsAreReturnedInTheDescendingOrder()
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatients(tag);

            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=-_lastUpdated", false, patients.Reverse().Cast<Resource>().ToArray());
        }

        [Fact]
        public async Task GivenMoreThanTenPatients_WhenSearchedWithSortParam_ThenPatientsAreReturnedInAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePaginatedPatients(tag);

            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=_lastUpdated", false, patients.Cast<Resource>().ToArray());
        }

        [Fact]
        public async Task GivenMoreThanTenPatients_WhenSearchedWithSortParamWithHyphen_ThenPatientsAreReturnedInDescendingOrder()
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePaginatedPatients(tag);

            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=-_lastUpdated", false, patients.Reverse().Cast<Resource>().ToArray());
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
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatients(tag);

            await ExecuteAndValidateBundle(
                $"Patient?_tag={tag}&_sort=family",
                false,
                patients.OrderBy(x => x.Name.Min(n => n.Family)).Cast<Resource>().ToArray());
        }

        [SkippableFact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenPatients_WhenSearchedWithFamilySortParamsWithHyphen_ThenPatientsAreReturnedInTheDescendingOrder()
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreatePatients(tag);

            await ExecuteAndValidateBundle(
                $"Patient?_tag={tag}&_sort=-family",
                false,
                patients.OrderByDescending(x => x.Name.Max(n => n.Family)).Cast<Resource>().ToArray());
        }

        [SkippableFact]
        public async Task GivenQueryWithDatetimeFilter_WhenSearchedWithSortParamOnDatetime_ThenResourcesAreReturnedInAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            // save the timestamp prior to creating the resources
            var now = DateTime.Now;
            var time = now.AddSeconds(-10);

            // create the resources which will have an timestamp bigger than the 'now' var
            var patients = await CreatePatients(tag);

            // Ask to get all patient with datetime filter
            // Note: this might result in getting Patients that were created on other tests (concurrent tests),
            // e.g. previous runs which were not cleaned yet, or concurrent tests.
            // we overcome this issue by not looking for specific results, rather just make sure they are
            // sorted.

            // Format the time to fit yyyy-MM-ddTHH:mm:ss.fffffffzzz, and encode its special characters.
            // sort and filter are based on same type (datetime)
            string lastUpdated = HttpUtility.UrlEncode($"{time:o}");
            await ExecuteAndValidateBundle($"Patient?_lastUpdated=gt{lastUpdated}&_sort=birthdate&_tag={tag}", false, patients.Cast<Resource>().ToArray());
        }

        [SkippableFact]
        public async Task GivenQueryWitDatetimeFilter_WhenSearchedWithHyphenSortParamOnDatetime_ThenResourcesAreReturnedInDescendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            // save the timestamp prior to creating the resources
            var now = DateTime.Now;
            var time = now.AddSeconds(-10);

            // create the resources which will have an timestamp bigger than the 'now' var
            var patients = await CreatePatients(tag);

            // Ask to get all patient with datetime filter
            // Note: this might result in getting Patients that were created on other tests (concurrent tests),
            // e.g. previous runs which were not cleaned yet, or concurrent tests.
            // we overcome this issue by not looking for specific results, rather just make sure they are
            // sorted.

            // Format the time to fit yyyy-MM-ddTHH:mm:ss.fffffffzzz, and encode its special characters.
            // sort and filter are based on same type (datetime)
            string lastUpdated = HttpUtility.UrlEncode($"{time:o}");
            await ExecuteAndValidateBundle($"Patient?_lastUpdated=gt{lastUpdated}&_sort=-birthdate&_tag={tag}", false, patients.OrderByDescending(x => x.BirthDate).Cast<Resource>().ToArray());
        }

        [SkippableFact]
        public async Task GivenQueryWithTagFilter_WhenSearchedWithSortParamOnDatetime_ThenResourcesAreReturnedInAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            // create the resources which will have an timestamp bigger than the 'now' var
            var patients = await CreatePatients(tag);

            // Ask to get all patient with specific tag order by birthdate (timestamp)
            // filter and sort are different based on different types
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=birthdate", false, patients.Cast<Resource>().ToArray());
        }

        [SkippableFact]
        public async Task GivenQueryWithTagFilter_WhenSearchedWithHyphenSortParamOnDatetime_ThenResourcesAreReturnedInDescendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            // create the resources which will have an timestamp bigger than the 'now' var
            var patients = await CreatePatients(tag);

            // Ask to get all patient with specific tag order by birthdate (timestamp)
            // filter and sort are different based on different types
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

            foreach (Patient p in patients)
            {
                var obs = await AddObservationToPatient(p, "1990-01-01", tag);
                resources.AddRange(obs);
            }

            resources.AddRange(patients);

            // Ask to get all patient with specific tag order by birthdate (timestamp)
            // filter and sort are different based on different types
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=birthdate&_revinclude=Observation:subject", false, resources.ToArray());
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenQueryWithRevinclude_WhenSearchedWithSortParamOnLastupdated_ThenResourcesAreReturnedInAscendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            var resources = new List<Resource>();

            // create the resources which will have an timestamp bigger than the 'now' var
            var patients = await CreatePatients(tag);

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

            var observations = new List<Observation>();
            for (int i = 0; i < patients.Length; i++)
            {
                var obs = await AddObservationToPatient(patients[i], "1990-01-01", tag);
                observations.Add(obs.First());
            }

            observations.Reverse();
            resources.AddRange(observations);
            resources.AddRange(patients.Reverse());

            // Ask to get all patient with specific tag order by birthdate (timestamp)
            // filter and sort are different based on different types
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=-_lastUpdated&_revinclude=Observation:subject", false, resources.ToArray());
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenQueryWithRevinclude_WhenSearchedWithSortParamOnDatetimeWithHyphen_ThenResourcesAreReturnedInDescendingOrder()
        {
            var tag = Guid.NewGuid().ToString();

            var resources = new List<Resource>();

            // create the resources which will have an timestamp bigger than the 'now' var
            var patients = await CreatePatients(tag);

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

            resources.AddRange(patients);
            resources.AddRange(observations);

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

        private async Task<Observation[]> CreateObservations(string tag)
        {
            Observation[] observations = await Client.CreateResourcesAsync<Observation>(
                o => SetObservationInfo(o, "1979-12-31", tag),
                o => SetObservationInfo(o, "1989-12-31", tag),
                o => SetObservationInfo(o, "1999-12-31", tag));

            return observations;
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
