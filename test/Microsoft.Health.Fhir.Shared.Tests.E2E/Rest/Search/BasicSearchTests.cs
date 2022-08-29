// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class BasicSearchTests : SearchTestsBase<HttpIntegrationTestFixture>
    {
        public BasicSearchTests(HttpIntegrationTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResourceWithVariousValues_WhenSearchedWithMultipleParams_ThenOnlyResourcesMatchingAllSearchParamsShouldBeReturned()
        {
            // Create various resources.
            string tag = Guid.NewGuid().ToString();
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(
                p => SetPatientInfo(p, "Seattle", "Robinson", tag),
                p => SetPatientInfo(p, "Portland", "Williamas", tag),
                p => SetPatientInfo(p, "Seattle", "Jones", tag));

            await ExecuteAndValidateBundle($"Patient?address-city=seattle&family=Jones&_tag={tag}", patients[2]);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResourceWithVariousValues_WhenSearchedWithCityParam_ThenOnlyResourcesMatchingAllSearchParamsShouldBeReturned()
        {
            var tag = Guid.NewGuid().ToString();

            // Create various resources.
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(
                p => SetPatientInfo(p, "seattle", "Robinson", tag), // match
                p => SetPatientInfo(p, "Portland", "Williamas", tag),
                p => SetPatientInfo(p, "SEATTLE", "Skouras", tag), // match
                p => SetPatientInfo(p, "Sea", "Luecke", tag),
                p => SetPatientInfo(p, "Seattle", "Jones", tag), // match
                p => SetPatientInfo(p, "New York", "Cook", tag),
                p => SetPatientInfo(p, "Amsterdam", "Hill", tag));

            await ExecuteAndValidateBundle($"Patient?address-city=Seattle&_tag={tag}", patients[0], patients[2], patients[4]);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenVariousTypesOfResources_WhenSearchedByResourceType_ThenOnlyResourcesMatchingTheResourceTypeShouldBeReturned()
        {
            var tag = Guid.NewGuid().ToString();

            // Create various resources.
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(3, tag);
            await Client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());
            await Client.CreateAsync(Samples.GetDefaultOrganization().ToPoco<Organization>());
            Bundle bundle = await Client.SearchAsync("Patient");
            foreach (var entity in bundle.Entry.Where(x => x.Search.Mode == Bundle.SearchEntryMode.Match))
            {
                Assert.Equal("Patient", entity.Resource.TypeName);
            }
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResourcesWithVariousValues_WhenSearchedWithTheMissingModifer_ThenOnlyTheResourcesWithMissingOrPresentParametersAreReturned()
        {
            var tag = Guid.NewGuid().ToString();
            Patient femalePatient = (await Client.CreateResourcesAsync<Patient>(p =>
            {
                p.Gender = AdministrativeGender.Female;
                p.Meta = new Meta
                {
                    Tag = new List<Coding>
                    {
                        new Coding("testTag", tag),
                    },
                };
            })).Single();
            Patient unspecifiedPatient = (await Client.CreateResourcesAsync<Patient>(p =>
            {
                p.Meta = new Meta
                {
                    Tag = new List<Coding>
                    {
                        new Coding("testTag", tag),
                    },
                };
            })).Single();

            await ExecuteAndValidateBundle($"Patient?gender:missing=true&_tag={tag}", unspecifiedPatient);
            await ExecuteAndValidateBundle($"Patient?gender:missing=false&_tag={tag}", femalePatient);

            await ExecuteAndValidateBundle($"Patient?address:missing=false&_tag={tag}");
            await ExecuteAndValidateBundle($"Patient?address:missing=true&_tag={tag}", femalePatient, unspecifiedPatient);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResourcesWithReference_WhenSearchedWithReferenceAndIdParameter_ThenOnlyResourcesMatchingAllSearchParamsShouldBeReturned()
        {
            Patient patientWithMatchingReference = (await Client.CreateResourcesAsync<Patient>(p =>
            {
                p.Gender = AdministrativeGender.Female;
                p.ManagingOrganization = new ResourceReference("Organization/123");
            })).Single();
            Patient patientWithNonMatchingReference = (await Client.CreateResourcesAsync<Patient>(p =>
            {
                p.Gender = AdministrativeGender.Female;
                p.ManagingOrganization = new ResourceReference("Organization/234");
            })).Single();

            await ExecuteAndValidateBundle($"Patient?_id={patientWithMatchingReference.Id}&organization=Organization/123", patientWithMatchingReference);
            await ExecuteAndValidateBundle($"Patient?_id={patientWithMatchingReference.Id}&organization=Organization/234");
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResourcesWithMissingReference_WhenSearchedWithTheMissingModiferAndOtherParameter_ThenOnlyMatchingResourcesWithMissingOrPresentReferenceAreReturned()
        {
            var tag = Guid.NewGuid().ToString();
            Patient patientWithReference = (await Client.CreateResourcesAsync<Patient>(p =>
            {
                p.Gender = AdministrativeGender.Female;
                p.ManagingOrganization = new ResourceReference("Organization/123");
                p.Meta = new Meta
                {
                    Tag = new List<Coding>
                    {
                        new Coding("testTag", tag),
                    },
                };
            })).Single();
            Patient femalePatient = (await Client.CreateResourcesAsync<Patient>(p =>
            {
                p.Gender = AdministrativeGender.Female;
                p.Meta = new Meta
                {
                    Tag = new List<Coding>
                    {
                        new Coding("testTag", tag),
                    },
                };
            })).Single();
            Patient unspecifiedPatient = (await Client.CreateResourcesAsync<Patient>(p =>
            {
                p.Meta = new Meta
                {
                    Tag = new List<Coding>
                    {
                        new Coding("testTag", tag),
                    },
                };
            })).Single();

            await ExecuteAndValidateBundle($"Patient?gender=female&organization:missing=true&_tag={tag}", femalePatient);
            await ExecuteAndValidateBundle($"Patient?gender=female&organization:missing=false&_tag={tag}", patientWithReference);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb)]
        public async Task GivenTooBigPostRequest_WhenSearching_ThenDontCrashServer()
        {
            var sb = new StringBuilder();

            for (int i = 0; i < 100_000; i++)
            {
                sb.Append('a');
            }

            await Client.SearchPostAsync("Patient", default, ("name", sb.ToString()));
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenVariousTypesOfResources_WhenSearchingAcrossAllResourceTypes_ThenOnlyResourcesMatchingTypeParameterShouldBeReturned()
        {
            var tag = Guid.NewGuid().ToString();

            // Create various resources.
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(3, tag);
            Observation observation = (await Client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>())).Resource;
            Organization organization = (await Client.CreateAsync(Samples.GetDefaultOrganization().ToPoco<Organization>())).Resource;

            await ExecuteAndValidateBundle($"?_type=Patient&_tag={tag}", patients);

            Bundle bundle = await Client.SearchPostAsync(null, default, ("_type", "Patient"), ("_tag", tag));
            ValidateBundle(bundle, $"?_type=Patient&_tag={tag}", patients);

            bundle = await Client.SearchAsync("?_type=Observation,Patient");
            Assert.True(bundle.Entry.Count > patients.Length);
            bundle = await Client.SearchPostAsync(null, default, ("_type", "Patient,Observation"));
            Assert.True(bundle.Entry.Count > patients.Length);

            await ExecuteAndValidateBundle($"?_type=Observation,Patient&_id={observation.Id}", observation);

            bundle = await Client.SearchPostAsync(null, default, ("_type", "Patient,Observation"), ("_id", observation.Id));
            ValidateBundle(bundle, $"?_type=Patient,Observation&_id={observation.Id}", observation);

            await ExecuteAndValidateBundle($"?_type=Observation,Patient&_id={organization.Id}");
            bundle = await Client.SearchPostAsync(null, default, ("_type", "Patient,Observation"), ("_id", organization.Id));
            ValidateBundle(bundle, $"?_type=Patient,Observation&_id={organization.Id}");
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        public async Task GivenMultiplePagesOfVariousTypesOfResourcesInSql_WhenUsingTypeParameterToSearchForMultipleResourceTypes_ThenCorrectResourcesAreReturned()
        {
            // Create various resources.
            string tag = Guid.NewGuid().ToString();
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(
                p => SetPatientInfo(p, "city1", "name1", tag),
                p => SetPatientInfo(p, "city2", "name2", tag),
                p => SetPatientInfo(p, "city3", "name3", tag),
                p => SetPatientInfo(p, "city4", "name4", tag));

            Observation[] observations = await Client.CreateResourcesAsync<Observation>(
                o => SetObservationInfo(o, tag),
                o => SetObservationInfo(o, tag));

            List<Resource> expectedResources = new List<Resource>();
            foreach (Observation observation in observations)
            {
                expectedResources.Add(observation);
            }

            foreach (Patient patient in patients)
            {
                expectedResources.Add(patient);
            }

            await ExecuteAndValidateBundle($"?_type=Patient,Observation&_tag={tag}&_count=3", sort: false, pageSize: 3, expectedResources.ToArray());
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.CosmosDb)]
        public async Task GivenMultiplePagesOfVariousTypesOfResourcesInCosmos_WhenUsingTypeParameterToSearchForMultipleResourceTypes_ThenCorrectResourcesAreReturned()
        {
            // Create various resources.
            string tag = Guid.NewGuid().ToString();
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(
                p => SetPatientInfo(p, "city1", "name1", tag),
                p => SetPatientInfo(p, "city2", "name2", tag),
                p => SetPatientInfo(p, "city3", "name3", tag),
                p => SetPatientInfo(p, "city4", "name4", tag));

            Observation[] observations = await Client.CreateResourcesAsync<Observation>(
                o => SetObservationInfo(o, tag),
                o => SetObservationInfo(o, tag));

            List<Resource> expectedResources = new List<Resource>();
            foreach (Patient patient in patients)
            {
                expectedResources.Add(patient);
            }

            foreach (Observation observation in observations)
            {
                expectedResources.Add(observation);
            }

            await ExecuteAndValidateBundle($"?_type=Patient,Observation&_tag={tag}&_count=3", sort: false, pageSize: 3, expectedResources.ToArray());
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAllInvalidTypeOfResources_WhenSearching_ThenEmptyBundleAndOperationOutcomeIssue()
        {
            string[] expectedDiagnosticsOneWrongType = { string.Format(Core.Resources.InvalidTypeParameter, "'Patient1'") };
            string[] expectedDiagnosticsMultipleWrongTypes = { string.Format(Core.Resources.InvalidTypeParameter, string.Join(',', "'Patient1'", "'Patient2'")) };
            OperationOutcome.IssueType[] expectedCodeTypes = { OperationOutcome.IssueType.NotSupported };
            OperationOutcome.IssueSeverity[] expectedIssueSeverities = { OperationOutcome.IssueSeverity.Warning };

            Bundle bundle = await Client.SearchAsync("?_type=Patient1");
            Assert.Contains("_type=Patient1", bundle.Link[0].Url);
            OperationOutcome outcome = GetAndValidateOperationOutcome(bundle);
            ValidateOperationOutcome(expectedDiagnosticsOneWrongType, expectedIssueSeverities, expectedCodeTypes, outcome);

            bundle = await Client.SearchAsync("?_type=Patient1,Patient2");
            Assert.Contains("_type=Patient1,Patient2", bundle.Link[0].Url);
            outcome = GetAndValidateOperationOutcome(bundle);
            ValidateOperationOutcome(expectedDiagnosticsMultipleWrongTypes, expectedIssueSeverities, expectedCodeTypes, outcome);

            bundle = await Client.SearchPostAsync(null, default, ("_type", "Patient1"));
            Assert.Contains("_type=Patient1", bundle.Link[0].Url);
            outcome = GetAndValidateOperationOutcome(bundle);
            ValidateOperationOutcome(expectedDiagnosticsOneWrongType, expectedIssueSeverities, expectedCodeTypes, outcome);

            bundle = await Client.SearchPostAsync(null, default, ("_type", "Patient1,Patient2"));
            Assert.Contains("_type=Patient1,Patient2", bundle.Link[0].Url);
            outcome = GetAndValidateOperationOutcome(bundle);
            ValidateOperationOutcome(expectedDiagnosticsMultipleWrongTypes, expectedIssueSeverities, expectedCodeTypes, outcome);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenEmptyTypeOfResource_WhenSearching_ThenBadRequestShouldBeReturned()
        {
            using FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.SearchAsync($"?_type="));

            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenSomeInvalidTypeOfResources_WhenSearchingAcrossAllResourceTypes_ThenSearchHasProperOutcome()
        {
            var tag = Guid.NewGuid().ToString();
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(3, tag);
            string[] expectedDiagnostics = { string.Format(Core.Resources.InvalidTypeParameter, "'Patient1'") };
            OperationOutcome.IssueType[] expectedCodeTypes = { OperationOutcome.IssueType.NotSupported };
            OperationOutcome.IssueSeverity[] expectedIssueSeverities = { OperationOutcome.IssueSeverity.Warning };

            Bundle bundle = await Client.SearchAsync($"?_type=Patient,Patient1&_tag={tag}");
            Assert.Contains("_type=Patient,Patient1", bundle.Link[0].Url);
            OperationOutcome outcome = GetAndValidateOperationOutcome(bundle);
            ValidateBundle(bundle, patients.AsEnumerable<Resource>().Append(outcome).ToArray());
            ValidateOperationOutcome(expectedDiagnostics, expectedIssueSeverities, expectedCodeTypes, outcome);

            bundle = await Client.SearchPostAsync(null, default, ("_type", "Patient1,Patient"), ("_tag", tag));
            Assert.Contains("_type=Patient1,Patient", bundle.Link[0].Url);
            outcome = GetAndValidateOperationOutcome(bundle);
            ValidateBundle(bundle, patients.AsEnumerable<Resource>().Append(outcome).ToArray());
            ValidateOperationOutcome(expectedDiagnostics, expectedIssueSeverities, expectedCodeTypes, outcome);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenVariousTypesOfResources_WhenSearchingAcrossAllResourceTypesUsingCommonSearchParameter_ThenOnlyResourcesMatchingTypeAndCommonSearchParameterShouldBeReturned()
        {
            // Create various resources.
            var tag = Guid.NewGuid().ToString();
            var suffix = Guid.NewGuid().ToString("N").Substring(0, 4);
            Patient nonMatchingPatient = (await Client.CreateAsync(new Patient() { Meta = new Meta { Tag = new List<Coding>() { new Coding("testTag", tag), } }, Name = new List<HumanName>() { new HumanName() { Family = $"Adams{suffix}" } } })).Resource;
            Practitioner nonMatchingPractitioner = (await Client.CreateAsync(new Practitioner() { Meta = new Meta { Tag = new List<Coding>() { new Coding("testTag", tag), } }, Name = new List<HumanName>() { new HumanName() { Family = $"Wilson{suffix}" } } })).Resource;
            Patient matchingPatient = (await Client.CreateAsync(new Patient() { Meta = new Meta { Tag = new List<Coding>() { new Coding("testTag", tag), } }, Name = new List<HumanName>() { new HumanName() { Family = $"Smith{suffix}" } } })).Resource;
            Practitioner matchingPractitioner = (await Client.CreateAsync(new Practitioner() { Meta = new Meta { Tag = new List<Coding>() { new Coding("testTag", tag), } }, Name = new List<HumanName>() { new HumanName() { Family = $"Smith{suffix}" } } })).Resource;

            var query = $"?_type=Patient,Practitioner&family={matchingPatient.Name[0].Family}&_tag={tag}";
            await ExecuteAndValidateBundle(query, matchingPatient, matchingPractitioner);
            Bundle bundle = await Client.SearchPostAsync(null, default, ("_type", "Patient,Practitioner"), ("family", matchingPatient.Name[0].Family), ("_tag", tag));
            ValidateBundle(bundle, query, matchingPatient, matchingPractitioner);

            query = $"?_type=Patient,Practitioner&family={nonMatchingPatient.Name[0].Family}&_tag={tag}";
            await ExecuteAndValidateBundle(query, nonMatchingPatient);
            bundle = await Client.SearchPostAsync(null, default, ("_type", "Patient,Practitioner"), ("family", nonMatchingPatient.Name[0].Family), ("_tag", tag));
            ValidateBundle(bundle, query, nonMatchingPatient);

            query = $"?_type=Patient,Practitioner&family={nonMatchingPractitioner.Name[0].Family}&_tag={tag}";
            await ExecuteAndValidateBundle(query, nonMatchingPractitioner);
            bundle = await Client.SearchPostAsync(null, default, ("_type", "Patient,Practitioner"), ("family", nonMatchingPractitioner.Name[0].Family), ("_tag", tag));
            ValidateBundle(bundle, query, nonMatchingPractitioner);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenVariousTypesOfResources_WhenSearchingAcrossAllResourceTypesUsingNonCommonSearchParameter_ThenExceptionShouldBeThrown()
        {
            using FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.SearchAsync($"?_type=Encounter,Procedure&subject=Patient/123"));

            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResources_WhenSearchedWithCount_ThenNumberOfResourcesReturnedShouldNotExceedCount()
        {
            const int numberOfResources = 5;
            const int count = 2;

            // Create the resources
            var tag = new Coding(string.Empty, Guid.NewGuid().ToString());

            Patient patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            var patients = new Patient[numberOfResources];

            for (int i = 0; i < numberOfResources; i++)
            {
                patient.Meta = new Meta();
                patient.Meta.Tag.Add(tag);

                FhirResponse<Patient> createdPatient = await Client.CreateAsync(patient);
                patients[i] = createdPatient.Resource;
            }

            Bundle results = new Bundle();

            // Search with count = 2, which should results in 5 pages.
            string url = $"Patient?_count={count}&_tag={tag.Code}";
            string baseUrl = Fixture.GenerateFullUrl(url);

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
            var tag = Guid.NewGuid().ToString();

            // Create the resources
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(10, tag);

            Bundle results = new Bundle();

            // Search with count = 2, which should results in 5 pages.
            string url = $"Patient?_count=2&_tag={tag}";
            string baseUrl = Fixture.GenerateFullUrl(url);

            int loop = 1;

            while (!string.IsNullOrEmpty(url))
            {
                // There should not be more than 5 loops.
                // For some reason, we're getting one more continuation token which returns 0 results, which is why we're checking for 6 instead of 5.
                Assert.True(loop <= 6, url);

                Bundle bundle = await Client.SearchAsync(url);

                Assert.NotNull(bundle);

                results.Entry.AddRange(bundle.Entry);

                url = bundle.NextLink?.ToString();

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
        public async Task GivenPostSearchWithCount_WhenSearched_ThenNextLinkUrlWouldYeildMoreResults()
        {
            var tag = Guid.NewGuid().ToString();

            // Create the resources
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(4, tag);
            var pageSize = 2;
            Bundle bundle = await Client.SearchPostAsync(null, default, ("_type", "Patient"), ("_count", pageSize.ToString()), ("_tag", tag));

            var expectedFirstBundle = patients.Length > pageSize ? patients.ToList().GetRange(0, pageSize).ToArray() : patients;
            ValidateBundle(bundle, $"?_type=Patient&_count={pageSize}&_tag={tag}", expectedFirstBundle);

            var nextLink = bundle.NextLink?.ToString();
            if (nextLink != null)
            {
                FhirResponse<Bundle> secondBundle = await Client.SearchAsync(nextLink);

                // Truncating host and appending continuation token
                nextLink = $"?_type=Patient&_count={pageSize}&_tag={tag}" + nextLink.Substring(nextLink.IndexOf("&ct"));
                ValidateBundle(secondBundle, nextLink, patients.ToList().GetRange(pageSize, patients.Length - pageSize).ToArray());
            }
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResourceWithHistory_WhenSearchedWithParams_ThenOnlyCurrentVersionShouldBeReturned()
        {
            // Create a coding that can be used to limit search to only the items within this test
            var testCoding = new Coding("https://testSystem", Guid.NewGuid().ToString());

            // Add the coding and set the observation status
            var originalObservation = Samples.GetDefaultObservation().ToPoco<Observation>();
            originalObservation.Status = ObservationStatus.Preliminary;
            originalObservation.Code.Coding.Add(testCoding);

            // Create the original resource
            var createdResource = await Client.CreateAsync(originalObservation);

            // Update the status and submit the update to the server
            var resourceToUpdate = createdResource.Resource;
            resourceToUpdate.Status = ObservationStatus.Corrected;
            var updatedResource = await Client.UpdateAsync(resourceToUpdate);

            // Search for the given coding. The only thing returned should be the updated resource
            await ExecuteAndValidateBundle($"Observation?code={testCoding.System}|{testCoding.Code}", updatedResource);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenListOfResources_WhenSearchedWithCountSummary_ThenTotalCountShouldBeReturned()
        {
            const int numberOfResources = 5;

            var tag = new Coding(string.Empty, Guid.NewGuid().ToString());

            Patient patient = Samples.GetDefaultPatient().ToPoco<Patient>();

            for (int i = 0; i < numberOfResources; i++)
            {
                patient.Meta = new Meta();
                patient.Meta.Tag.Add(tag);

                await Client.CreateAsync(patient);
            }

            Bundle bundle = await Client.SearchAsync($"Patient?_tag={tag.Code}&_summary=count");

            Assert.NotNull(bundle);
            Assert.Equal(numberOfResources, bundle.Total);
            Assert.Empty(bundle.Entry);
        }

        [InlineData("_summary", "_count")]
        [InlineData("_summary", "xyz")]
        [InlineData("_count", "abc")]
        [InlineData("_elements", "")]
        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResources_WhenSearchedWithIncorrectFormatParams_ThenExceptionShouldBeThrown(string key, string val)
        {
            using FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.SearchAsync($"Patient?{key}={val}"));

            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenListOfResources_WhenSearchedWithElements_ThenOnlySpecifiedPropertiesShouldBeReturned()
        {
            string[] elements = { "gender", "birthDate" };
            var tag = new Coding(string.Empty, Guid.NewGuid().ToString());

            Observation[] observations = await CreateObservationWithSpecifiedElements(tag, elements);

            Bundle bundle = await Client.SearchAsync($"Observation?_tag={tag.Code}&_elements={string.Join(',', elements)}");

            ValidateBundle(bundle, observations);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenListOfResources_WhenSearchedWithInvalidElements_ThenOnlyValidSpecifiedPropertiesShouldBeReturned()
        {
            string[] elements = { "gender", "birthDate" };
            var tag = new Coding(string.Empty, Guid.NewGuid().ToString());

            Observation[] observations = await CreateObservationWithSpecifiedElements(tag, elements);

            Bundle bundle = await Client.SearchAsync($"Observation?_tag={tag.Code}&_elements=invalidProperty,{string.Join(',', elements)}");

            ValidateBundle(bundle, observations);
        }

        [InlineData("id", "count")]
        [InlineData("id,active", "true")]
        [InlineData("id,active", "data")]
        [InlineData("id,active", "text")]
        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenListOfResources_WhenSearchedWithElementsAndSummaryNotSetToFalse_ThenExceptionShouldBeThrown(string elementsValues, string summaryValue)
        {
            using FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.SearchAsync($"Patient?_elements={elementsValues}&_summary={summaryValue}"));

            const HttpStatusCode expectedStatusCode = HttpStatusCode.BadRequest;
            Assert.Equal(expectedStatusCode, ex.StatusCode);

            var expectedErrorMessage = $"{expectedStatusCode}: " + string.Format(Core.Resources.ElementsAndSummaryParametersAreIncompatible, KnownQueryParameterNames.Summary, KnownQueryParameterNames.Elements);

            Assert.StartsWith(expectedErrorMessage, ex.Message);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenListOfResources_WhenSearchedWithElementsAndSummarySetToFalse_ThenOnlySpecifiedPropertiesShouldBeReturned()
        {
            string[] elements = { "gender", "birthDate" };
            var tag = new Coding(string.Empty, Guid.NewGuid().ToString());

            Observation[] observations = await CreateObservationWithSpecifiedElements(tag, elements);

            Bundle bundle = await Client.SearchAsync($"Observation?_tag={tag.Code}&_elements={string.Join(',', elements)}&_summary=false");

            ValidateBundle(bundle, observations);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenListOfResources_WhenSearchedSummarySetToData_ThenOnlySpecifiedPropertiesShouldBeReturned()
        {
            var tag = new Coding(string.Empty, Guid.NewGuid().ToString());

            Patient patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            patient.Meta = new Meta();
            patient.Meta.Tag.Add(tag);
            await Client.CreateAsync(patient);

            Bundle bundle = await Client.SearchAsync($"Patient?_tag={tag.Code}&{KnownQueryParameterNames.Summary}=data");
            Assert.NotEmpty(bundle.Entry);
            var returnedPatient = bundle.Entry[0].Resource as Patient;
            Assert.NotEmpty(returnedPatient.Contact);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenListOfResources_WhenSearchedSummarySetToText_ThenOnlySpecifiedPropertiesShouldBeReturned()
        {
            var tag = new Coding(string.Empty, Guid.NewGuid().ToString());

            Patient patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            patient.Meta = new Meta();
            patient.Meta.Tag.Add(tag);
            await Client.CreateAsync(patient);

            Bundle bundle = await Client.SearchAsync($"Patient?_tag={tag.Code}&{KnownQueryParameterNames.Summary}=text");
            Assert.NotEmpty(bundle.Entry);
            var returnedPatient = bundle.Entry[0].Resource as Patient;
            Assert.NotNull(returnedPatient.Text);
            Assert.NotNull(returnedPatient.Meta);
            Assert.NotNull(returnedPatient.Id);
            Assert.Empty(returnedPatient.Contact);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenListOfResources_WhenSearchedSummarySetToTrue_ThenOnlySpecifiedPropertiesShouldBeReturned()
        {
            var tag = new Coding(string.Empty, Guid.NewGuid().ToString());

            Patient patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            patient.Meta = new Meta();
            patient.Meta.Tag.Add(tag);
            await Client.CreateAsync(patient);

            Bundle bundle = await Client.SearchAsync($"Patient?_tag={tag.Code}&{KnownQueryParameterNames.Summary}=true");
            Assert.NotEmpty(bundle.Entry);
            var returnedPatient = bundle.Entry[0].Resource as Patient;
            Assert.NotNull(returnedPatient.BirthDate);
            Assert.Empty(returnedPatient.Contact);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenListOfResources_WhenSearchedWithTotalTypeAccurate_ThenTotalCountShouldBeIncludedInReturnedBundle()
        {
            const int numberOfResources = 5;

            var tag = new Coding(string.Empty, Guid.NewGuid().ToString());

            Patient patient = Samples.GetDefaultPatient().ToPoco<Patient>();

            for (int i = 0; i < numberOfResources; i++)
            {
                patient.Meta = new Meta();
                patient.Meta.Tag.Add(tag);

                await Client.CreateAsync(patient);
            }

            Bundle bundle = await Client.SearchAsync($"Patient?_tag={tag.Code}&_total=accurate");

            Assert.NotNull(bundle);
            Assert.NotEmpty(bundle.Entry);
            Assert.Equal(numberOfResources, bundle.Total);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenListOfResources_WhenSearchedWithTotalTypeNone_ThenTotalCountShouldBeIncludedInReturnedBundle()
        {
            const int numberOfResources = 5;

            var tag = new Coding(string.Empty, Guid.NewGuid().ToString());

            Patient patient = Samples.GetDefaultPatient().ToPoco<Patient>();

            for (int i = 0; i < numberOfResources; i++)
            {
                patient.Meta = new Meta();
                patient.Meta.Tag.Add(tag);

                await Client.CreateAsync(patient);
            }

            Bundle bundle = await Client.SearchAsync($"Patient?_tag={tag.Code}&_total=none");

            Assert.NotNull(bundle);
            Assert.NotEmpty(bundle.Entry);
            Assert.Null(bundle.Total);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenListOfResources_WhenSearchedWithTotalTypeAccurate_ThenTotalCountShouldNotBeIncludedInReturnedBundleForSubsequentPages()
        {
            const int numberOfResources = 5;

            var tag = new Coding(string.Empty, Guid.NewGuid().ToString());

            Patient patient = Samples.GetDefaultPatient().ToPoco<Patient>();

            for (int i = 0; i < numberOfResources; i++)
            {
                patient.Meta = new Meta();
                patient.Meta.Tag.Add(tag);

                await Client.CreateAsync(patient);
            }

            Bundle bundle = await Client.SearchAsync($"Patient?_tag={tag.Code}&_total=accurate&_count=2");

            Assert.NotNull(bundle);
            Assert.NotEmpty(bundle.Entry);
            Assert.Equal(numberOfResources, bundle.Total);
            Assert.DoesNotContain("_total", bundle.NextLink.ToString(), StringComparison.OrdinalIgnoreCase);

            bundle = await Client.SearchAsync(bundle.NextLink.ToString());

            Assert.NotNull(bundle);
            Assert.NotEmpty(bundle.Entry);
            Assert.Null(bundle.Total);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenListOfResources_WhenSearchedWithTotalTypeEstimate_ThenExceptionShouldBeThrown()
        {
            const int numberOfResources = 5;

            var tag = new Coding(string.Empty, Guid.NewGuid().ToString());

            Patient patient = Samples.GetDefaultPatient().ToPoco<Patient>();

            for (int i = 0; i < numberOfResources; i++)
            {
                patient.Meta = new Meta();
                patient.Meta.Tag.Add(tag);

                await Client.CreateAsync(patient);
            }

            var totalType = TotalType.Estimate.ToString();

            using FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.SearchAsync($"Patient?_total={totalType}"));

            var expectedStatusCode = HttpStatusCode.Forbidden;
            Assert.Equal(expectedStatusCode, ex.StatusCode);

            var supportedTotalTypes = new string($"'{TotalType.Accurate}', '{TotalType.None}'").ToLower(CultureInfo.CurrentCulture);
            var expectedErrorMessage = $"{expectedStatusCode}: " + string.Format(Core.Resources.UnsupportedTotalParameter, totalType, supportedTotalTypes);

            Assert.StartsWith(expectedErrorMessage, ex.Message);
        }

        [InlineData("_total", "count")]
        [InlineData("_total", "xyz")]
        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenListOfResources_WhenSearchedWithInvalidTotalType_ThenExceptionShouldBeThrown(string key, string val)
        {
            using FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.SearchAsync($"Patient?{key}={val}"));

            var expectedStatusCode = HttpStatusCode.BadRequest;
            Assert.Equal(expectedStatusCode, ex.StatusCode);

            var supportedTotalTypes = new string($"'{TotalType.Accurate}', '{TotalType.None}'").ToLower(CultureInfo.CurrentCulture);
            var expectedErrorMessage = $"{expectedStatusCode}: " + string.Format(Core.Resources.InvalidTotalParameter, val, supportedTotalTypes);

            Assert.StartsWith(expectedErrorMessage, ex.Message);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResourceWithTypeValue_WhenSearchedWithTypeParam_ThenOnlyResourcesMatchingAllSearchParamsShouldBeReturned()
        {
            var code = Guid.NewGuid().ToString();
            NamingSystem library = await Client.CreateAsync(new NamingSystem
            {
                Name = "test",
                Status = PublicationStatus.Draft,
                Kind = NamingSystem.NamingSystemType.Codesystem,
                Date = "2019",
                UniqueId = new List<NamingSystem.UniqueIdComponent> { new NamingSystem.UniqueIdComponent { Type = NamingSystem.NamingSystemIdentifierType.Uri, Value = "https://localhost" } },
                Type = new CodeableConcept("https://localhost/", code),
            });

            await ExecuteAndValidateBundle($"NamingSystem?type={code}", library);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenASearchRequest_WhenExceedingMaxCount_ThenAnOperationOutcomeWarningIsReturnedInTheBundle()
        {
            Bundle bundle = await Client.SearchAsync("?_count=" + int.MaxValue);

            Assert.Equal(KnownResourceTypes.OperationOutcome, bundle.Entry.First().Resource.TypeName);
            Assert.Contains("exceeds limit", (string)bundle.Scalar("entry.resource.issue.diagnostics"));
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenASearchRequest_WhenExceedingMaxCount_ThenResourcesAreSerializedInBundle()
        {
            var tag = new Coding(string.Empty, Guid.NewGuid().ToString());

            Patient patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            patient.Meta = new Meta();
            patient.Meta.Tag.Add(tag);
            await Client.CreateAsync(patient);

            Bundle bundle = await Client.SearchAsync($"Patient?_tag={tag.Code}&_count=" + int.MaxValue);

            Assert.Equal(KnownResourceTypes.OperationOutcome, bundle.Entry.First().Resource.TypeName);
            Assert.NotNull(bundle.Entry.Skip(1).First().Resource);
        }

        [Theory]
        [InlineData("RiskAssessment?probability=gt55555555555555555555555555555555555555555555555")]
        [InlineData("RiskAssessment?probability=gt")]
        [InlineData("RiskAssessment?probability=foo")]
        [InlineData("Observation?date=05")]
        [InlineData("Observation?code=a|b|c|d")]
        [InlineData("Observation?value-quantity=5.40e-3|http://unitsofmeasure.org|g|extra")]
        [InlineData("Observation?ct=YWJj")]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenASearchRequestWithInvalidValues_WhenHandled_ReturnsABadRequestError(string uri)
        {
            System.Net.Http.HttpResponseMessage httpResponseMessage = await Client.HttpClient.GetAsync(uri);
            Assert.Equal(HttpStatusCode.BadRequest, httpResponseMessage.StatusCode);
        }

        [Fact]
        public async Task GivenASearchRequestWithInvalidParameters_WhenHandled_ReturnsSearchResults()
        {
            string[] expectedDiagnostics =
            {
                string.Format(Core.Resources.SearchParameterNotSupported, "Cookie", "Patient"),
                string.Format(Core.Resources.SearchParameterNotSupported, "Ramen", "Patient"),
            };
            OperationOutcome.IssueType[] expectedCodeTypes = { OperationOutcome.IssueType.NotSupported, OperationOutcome.IssueType.NotSupported };
            OperationOutcome.IssueSeverity[] expectedIssueSeverities = { OperationOutcome.IssueSeverity.Warning, OperationOutcome.IssueSeverity.Warning };

            Bundle bundle = await Client.SearchAsync("Patient?Cookie=Chip&Ramen=Spicy");
            OperationOutcome outcome = GetAndValidateOperationOutcome(bundle);
            ValidateOperationOutcome(expectedDiagnostics, expectedIssueSeverities, expectedCodeTypes, outcome);
        }

        [Fact]
        public async Task GivenAPostSearchRequestWithInvalidParameters_WhenHandled_ReturnsSearchResults()
        {
            string[] expectedDiagnostics =
            {
                string.Format(Core.Resources.SearchParameterNotSupported, "entry:[{", "Patient"),
                string.Format(Core.Resources.SearchParameterNotSupported, "Ramen", "Patient"),
            };
            OperationOutcome.IssueType[] expectedCodeTypes = { OperationOutcome.IssueType.NotSupported, OperationOutcome.IssueType.NotSupported };
            OperationOutcome.IssueSeverity[] expectedIssueSeverities = { OperationOutcome.IssueSeverity.Warning, OperationOutcome.IssueSeverity.Warning };

            Bundle bundle = await Client.SearchPostAsync("Patient", default, ("entry:[{", string.Empty), ("Ramen", "Spicy"));
            OperationOutcome outcome = GetAndValidateOperationOutcome(bundle);
            ValidateOperationOutcome(expectedDiagnostics, expectedIssueSeverities, expectedCodeTypes, outcome);
        }

        [Fact]
        public async Task GivenASearchRequestWithInvalidParametersAndLenientHandling_WhenHandled_ReturnsSearchResults()
        {
            string[] expectedDiagnostics =
            {
                string.Format(Core.Resources.SearchParameterNotSupported, "Cookie", "Patient"),
                string.Format(Core.Resources.SearchParameterNotSupported, "Ramen", "Patient"),
            };
            OperationOutcome.IssueType[] expectedCodeTypes = { OperationOutcome.IssueType.NotSupported, OperationOutcome.IssueType.NotSupported };
            OperationOutcome.IssueSeverity[] expectedIssueSeverities = { OperationOutcome.IssueSeverity.Warning, OperationOutcome.IssueSeverity.Warning };

            Bundle bundle = await Client.SearchAsync("Patient?Cookie=Chip&Ramen=Spicy", Tuple.Create(KnownHeaders.Prefer, "handling=lenient"));
            OperationOutcome outcome = GetAndValidateOperationOutcome(bundle);
            ValidateOperationOutcome(expectedDiagnostics, expectedIssueSeverities, expectedCodeTypes, outcome);
        }

        [Fact]
        public async Task GivenASearchRequestWithInvalidParametersAndStrictHandling_WhenHandled_ReturnsBadRequest()
        {
            using FhirException ex = await Assert.ThrowsAsync<FhirException>(() =>
                Client.SearchAsync("Patient?Cookie=Chip&Ramen=Spicy", Tuple.Create(KnownHeaders.Prefer, "handling=strict")));
            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [Fact]
        public async Task GivenASearchRequestWithValidParametersAndStrictHandling_WhenHandled_ReturnsSearchResults()
        {
            var response = await Client.SearchAsync("Patient?name=ronda", Tuple.Create(KnownHeaders.Prefer, "handling=strict"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task GivenACompositeTokenNumberNumberSearchParameter_WhenSearching_ReturnsSearchResults()
        {
            var sequenceType = ModelInfoProvider.Version == FhirSpecification.Stu3 ? "Sequence" : "MolecularSequence";
            var response = await Client.SearchAsync($"{sequenceType}?referenceseqid-window-coordinate=NT_007592.15$18130918$18143955");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task GivenASearchRequestWithInvalidHandling_WhenHandled_ReturnsBadRequest()
        {
            using FhirException ex = await Assert.ThrowsAsync<FhirException>(() =>
                Client.SearchAsync("Patient?Cookie=Chip&Ramen=Spicy", Tuple.Create(KnownHeaders.Prefer, "handling=foo")));
            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [Fact]
        public async Task GivenSetOfChunkyResources_WhenIteratingOverThem_ThenAllResourcesReturned()
        {
            int n = 30;
            var tag = Guid.NewGuid().ToString();
            var sb = new StringBuilder();
            for (int i = 0; i < 500_000; i++)
            {
                sb.Append('A');
            }

            var text = "<div>" + sb.ToString() + "</div>";

            for (int i = 0; i < n; i++)
            {
                var observation = await Client.CreateResourcesAsync<Observation>(() => new Observation()
                {
                    Meta = new Meta { Tag = new List<Coding> { new Coding(null, tag) } },
                    Status = ObservationStatus.Final,
                    Text = new Narrative() { Div = text },
                    Code = new CodeableConcept("http://loinc.org", "11557-6"),
                    Value = new Quantity(i, "kPa"),
                });
            }

            List<int> values = new();
            int count = 0;
            string nextLink = $"Observation?_count=20&_tag={tag}";
            do
            {
                Bundle firstBundle = await Client.SearchAsync(nextLink);
                foreach (var entity in firstBundle.Entry)
                {
                    values.Add((int)((Quantity)((Observation)entity.Resource).Value).Value);
                }

                count += firstBundle.Entry.Count;
                nextLink = firstBundle.NextLink?.ToString();
            }
            while (nextLink != null);
            Assert.Equal(n, count);
            Assert.Equal(Enumerable.Range(0, n), values.OrderBy(x => x));
        }

        [Fact]
        public async Task GivenSetOfChunkyResources_WhenIteratingOverThemWithSort_ThenAllResourcesReturned()
        {
            int n = 30;
            var tag = Guid.NewGuid().ToString();
            var sb = new StringBuilder();
            for (int i = 0; i < 500_000; i++)
            {
                sb.Append('A');
            }

            var text = "<div>" + sb.ToString() + "</div>";
            var date = new DateTime(2000, 01, 01);
            for (int i = 0; i < n; i++)
            {
                var observation = await Client.CreateResourcesAsync<Patient>(() => new Patient()
                {
                    Meta = new Meta { Tag = new List<Coding> { new Coding(null, tag) } },
                    BirthDate = date.AddDays(i).ToString("yyyy-MM-dd"),
                    Text = new Narrative() { Div = text },
                });
            }

            List<int> values = new();
            int count = 0;
            string nextLink = $"Patient?_count=20&_tag={tag}&_sort=-_lastUpdated";
            do
            {
                Bundle firstBundle = await Client.SearchAsync(nextLink);
                foreach (var entity in firstBundle.Entry)
                {
                    values.Add(DateTime.Parse(((Patient)entity.Resource).BirthDate).Subtract(date).Days);
                }

                count += firstBundle.Entry.Count;
                nextLink = firstBundle.NextLink?.ToString();
            }
            while (nextLink != null);
            Assert.Equal(n, count);
            Assert.Equal(Enumerable.Range(0, n), values.OrderBy(x => x));
        }

        [Fact]
        public async Task GivenASearchRequestWithParameter_TextAndLenientHandling_WhenHandled_ReturnsSearchResultsWithWarning()
        {
            string[] expectedDiagnostics =
            {
                string.Format(Core.Resources.SearchParameterNotSupported, "_text", "Patient"),
            };
            OperationOutcome.IssueType[] expectedCodeTypes = { OperationOutcome.IssueType.NotSupported};
            OperationOutcome.IssueSeverity[] expectedIssueSeverities = { OperationOutcome.IssueSeverity.Warning};

            Bundle bundle = await Client.SearchAsync("Patient?_text=mobile", Tuple.Create(KnownHeaders.Prefer, "handling=lenient"));
            OperationOutcome outcome = GetAndValidateOperationOutcome(bundle);
            ValidateOperationOutcome(expectedDiagnostics, expectedIssueSeverities, expectedCodeTypes, outcome);
        }

        [Fact]
        public async Task GivenASearchRequestWithParameter_TextAndNoHandling_WhenHandled_ReturnsSearchResultsWithWarning()
        {
            string[] expectedDiagnostics =
            {
                string.Format(Core.Resources.SearchParameterNotSupported, "_text", "Patient"),
            };
            OperationOutcome.IssueType[] expectedCodeTypes = { OperationOutcome.IssueType.NotSupported };
            OperationOutcome.IssueSeverity[] expectedIssueSeverities = { OperationOutcome.IssueSeverity.Warning };

            Bundle bundle = await Client.SearchAsync("Patient?_text=mobile");
            OperationOutcome outcome = GetAndValidateOperationOutcome(bundle);
            ValidateOperationOutcome(expectedDiagnostics, expectedIssueSeverities, expectedCodeTypes, outcome);
        }

        [Fact]
        public async Task GivenASearchRequestWithParameter_TextAndStrictHandling_WhenHandled_ReturnsBadRequest()
        {
            using FhirException ex = await Assert.ThrowsAsync<FhirException>(() =>
                Client.SearchAsync("Patient?_text=mobile", Tuple.Create(KnownHeaders.Prefer, "handling=strict")));
            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        private async Task<Observation[]> CreateObservationWithSpecifiedElements(Coding tag, string[] elements)
        {
            const int numberOfResources = 3;
            IStructureDefinitionSummaryProvider summaryProvider = new PocoStructureDefinitionSummaryProvider();
            var typeinfo = summaryProvider.Provide("Observation");
            var required = typeinfo.GetElements().Where(e => e.IsRequired).Select(x => x.ElementName).ToList();
            required.Add("meta");
            elements = elements.Union(required).ToArray();

            Observation patient = Samples.GetDefaultObservation().ToPoco<Observation>();
            var patients = new Observation[numberOfResources];

            for (int i = 0; i < numberOfResources; i++)
            {
                patient.Meta = new Meta();
                patient.Meta.Tag.Add(tag);

                FhirResponse<Observation> createdObservation = await Client.CreateAsync(patient);
                patients[i] = MaskingNode.ForElements(new ScopedNode(createdObservation.Resource.ToTypedElement()), elements)
                    .ToPoco<Observation>();

                var system = ModelInfoProvider.Version == FhirSpecification.Stu3 ? "http://hl7.org/fhir/v3/ObservationValue" : "http://terminology.hl7.org/CodeSystem/v3-ObservationValue";
                var subsettedTag = new Coding(system, "SUBSETTED");
                patients[i].Meta.Tag.Add(subsettedTag);
            }

            return patients;
        }

        private void SetPatientInfo(Patient patient, string city, string family, string tag)
        {
            if (tag != null)
            {
                patient.Meta = new Meta();
                patient.Meta.Tag.Add(new Coding(null, tag));
            }

            patient.Address = new List<Address>()
                {
                    new Address() { City = city },
                };

            patient.Name = new List<HumanName>()
                {
                    new HumanName() { Family = family },
                };
        }

        private void SetObservationInfo(Observation observation, string tag)
        {
            observation.Status = ObservationStatus.Final;
            observation.Code = new CodeableConcept
            {
                Coding = new List<Coding> { new Coding(null, tag) },
            };
            observation.Meta = new Meta { Tag = new List<Coding> { new Coding(null, tag) }, };
        }
    }
}
