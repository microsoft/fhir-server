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
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
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
    public class BasicSearchTests : SearchTestsBase<HttpIntegrationTestFixture>, IAsyncLifetime
    {
        public BasicSearchTests(HttpIntegrationTestFixture fixture)
            : base(fixture)
        {
        }

        public async Task InitializeAsync()
        {
            // Delete all patients before starting the test.
            await Client.DeleteAllResources(ResourceType.Patient);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResourceWithVariousValues_WhenSearchedWithMultipleParams_ThenOnlyResourcesMatchingAllSearchParamsShouldBeReturned()
        {
            // Create various resources.
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(
                p => SetPatientInfo(p, "Seattle", "Robinson"),
                p => SetPatientInfo(p, "Portland", "Williamas"),
                p => SetPatientInfo(p, "Seattle", "Jones"));

            await ExecuteAndValidateBundle("Patient?address-city=seattle&family=Jones", patients[2]);

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
        public async Task GivenResourceWithVariousValues_WhenSearchedWithCityParam_ThenOnlyResourcesMatchingAllSearchParamsShouldBeReturned()
        {
            var tag = Guid.NewGuid().ToString();

            // Create various resources.
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(
                p => SetPatientInfo(p, "seattle", "Robinson"), // match
                p => SetPatientInfo(p, "Portland", "Williamas"),
                p => SetPatientInfo(p, "SEATTLE", "Skouras"), // match
                p => SetPatientInfo(p, "Sea", "Luecke"),
                p => SetPatientInfo(p, "Seattle", "Jones"), // match
                p => SetPatientInfo(p, "New York", "Cook"),
                p => SetPatientInfo(p, "Amsterdam", "Hill"));

            await ExecuteAndValidateBundle("Patient?address-city=Seattle", patients[0], patients[2], patients[4]);

            void SetPatientInfo(Patient patient, string city, string family)
            {
                patient.Meta = new Meta();
                patient.Meta.Tag.Add(new Coding(null, tag));
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
            await Client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());
            await Client.CreateAsync(Samples.GetDefaultOrganization().ToPoco<Organization>());

            await ExecuteAndValidateBundle("Patient", patients);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResourcesWithVariousValues_WhenSearchedWithTheMissingModifer_ThenOnlyTheResourcesWithMissingOrPresentParametersAreReturned()
        {
            Patient femalePatient = (await Client.CreateResourcesAsync<Patient>(p => p.Gender = AdministrativeGender.Female)).Single();
            Patient unspecifiedPatient = (await Client.CreateResourcesAsync<Patient>(p => { })).Single();

            await ExecuteAndValidateBundle("Patient?gender:missing=true", unspecifiedPatient);
            await ExecuteAndValidateBundle("Patient?gender:missing=false", femalePatient);

            await ExecuteAndValidateBundle("Patient?_type:missing=true");
            await ExecuteAndValidateBundle("Patient?_type:missing=false", femalePatient, unspecifiedPatient);
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
            Patient patientWithReference = (await Client.CreateResourcesAsync<Patient>(p =>
            {
                p.Gender = AdministrativeGender.Female;
                p.ManagingOrganization = new ResourceReference("Organization/123");
            })).Single();
            Patient femalePatient = (await Client.CreateResourcesAsync<Patient>(p => p.Gender = AdministrativeGender.Female)).Single();
            Patient unspecifiedPatient = (await Client.CreateResourcesAsync<Patient>(p => { })).Single();

            await ExecuteAndValidateBundle("Patient?gender=female&organization:missing=true", femalePatient);
            await ExecuteAndValidateBundle("Patient?gender=female&organization:missing=false", patientWithReference);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenVariousTypesOfResources_WhenSearchingAcrossAllResourceTypes_ThenOnlyResourcesMatchingTypeParameterShouldBeReturned()
        {
            // Create various resources.
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(3);
            Observation observation = (await Client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>())).Resource;
            Organization organization = (await Client.CreateAsync(Samples.GetDefaultOrganization().ToPoco<Organization>())).Resource;

            await ExecuteAndValidateBundle("?_type=Patient", patients);

            Bundle bundle = await Client.SearchPostAsync(null, default, ("_type", "Patient"));
            ValidateBundle(bundle, "?_type=Patient", patients);

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
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(3);
            string[] expectedDiagnostics = { string.Format(Core.Resources.InvalidTypeParameter, "'Patient1'") };
            OperationOutcome.IssueType[] expectedCodeTypes = { OperationOutcome.IssueType.NotSupported };
            OperationOutcome.IssueSeverity[] expectedIssueSeverities = { OperationOutcome.IssueSeverity.Warning };

            Bundle bundle = await Client.SearchAsync("?_type=Patient,Patient1");
            Assert.Contains("_type=Patient,Patient1", bundle.Link[0].Url);
            OperationOutcome outcome = GetAndValidateOperationOutcome(bundle);
            ValidateBundle(bundle, patients.AsEnumerable<Resource>().Append(outcome).ToArray());
            ValidateOperationOutcome(expectedDiagnostics, expectedIssueSeverities, expectedCodeTypes, outcome);

            bundle = await Client.SearchPostAsync(null, default, ("_type", "Patient1,Patient"));
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
            var suffix = Guid.NewGuid().ToString("N").Substring(0, 4);
            Patient nonMatchingPatient = (await Client.CreateAsync(new Patient() { Name = new List<HumanName>() { new HumanName() { Family = $"Adams{suffix}" } } })).Resource;
            Practitioner nonMatchingPractitioner = (await Client.CreateAsync(new Practitioner() { Name = new List<HumanName>() { new HumanName() { Family = $"Wilson{suffix}" } } })).Resource;
            Patient matchingPatient = (await Client.CreateAsync(new Patient() { Name = new List<HumanName>() { new HumanName() { Family = $"Smith{suffix}" } } })).Resource;
            Practitioner matchingPractitioner = (await Client.CreateAsync(new Practitioner() { Name = new List<HumanName>() { new HumanName() { Family = $"Smith{suffix}" } } })).Resource;

            var query = $"?_type=Patient,Practitioner&family={matchingPatient.Name[0].Family}";
            await ExecuteAndValidateBundle(query, matchingPatient, matchingPractitioner);
            Bundle bundle = await Client.SearchPostAsync(null, default, ("_type", "Patient,Practitioner"), ("family", matchingPatient.Name[0].Family));
            ValidateBundle(bundle, query, matchingPatient, matchingPractitioner);

            query = $"?_type=Patient,Practitioner&family={nonMatchingPatient.Name[0].Family}";
            await ExecuteAndValidateBundle(query, nonMatchingPatient);
            bundle = await Client.SearchPostAsync(null, default, ("_type", "Patient,Practitioner"), ("family", nonMatchingPatient.Name[0].Family));
            ValidateBundle(bundle, query, nonMatchingPatient);

            query = $"?_type=Patient,Practitioner&family={nonMatchingPractitioner.Name[0].Family}";
            await ExecuteAndValidateBundle(query, nonMatchingPractitioner);
            bundle = await Client.SearchPostAsync(null, default, ("_type", "Patient,Practitioner"), ("family", nonMatchingPractitioner.Name[0].Family));
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
            // Create the resources
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(10);

            Bundle results = new Bundle();

            // Search with count = 2, which should results in 5 pages.
            string url = "Patient?_count=2";
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
            // Create the resources
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(4);
            var pageSize = 2;
            Bundle bundle = await Client.SearchPostAsync(null, default, ("_type", "Patient"), ("_count", pageSize.ToString()));

            var expectedFirstBundle = patients.Length > pageSize ? patients.ToList().GetRange(0, pageSize).ToArray() : patients;
            ValidateBundle(bundle, "?_type=Patient&_count=2", expectedFirstBundle);

            var nextLink = bundle.NextLink?.ToString();
            if (nextLink != null)
            {
                FhirResponse<Bundle> secondBundle = await Client.SearchAsync(nextLink);

                // Truncating host and appending continuation token
                nextLink = "?_type=Patient&_count=2" + nextLink.Substring(nextLink.IndexOf("&ct"));
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
        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResources_WhenSearchedWithIncorrectSummaryParams_ThenExceptionShouldBeThrown(string key, string val)
        {
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(3);
            using FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.SearchAsync($"Patient?{key}={val}"));

            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenListOfResources_WhenSearchedWithElements_ThenOnlySpecifiedPropertiesShouldBeReturned()
        {
            string[] elements = { "gender", "birthDate" };
            var tag = new Coding(string.Empty, Guid.NewGuid().ToString());

            Patient[] patients = await CreatePatientsWithSpecifiedElements(tag, elements);

            Bundle bundle = await Client.SearchAsync($"Patient?_tag={tag.Code}&_elements={string.Join(',', elements)}");

            ValidateBundle(bundle, patients);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenListOfResources_WhenSearchedWithEmptyElements_ThenAllPropertiesShouldBeReturned()
        {
            const int numberOfResources = 3;

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

            Bundle bundle = await Client.SearchAsync($"Patient?_tag={tag.Code}&_elements=");

            ValidateBundle(bundle, patients);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenListOfResources_WhenSearchedWithInvalidElements_ThenOnlyValidSpecifiedPropertiesShouldBeReturned()
        {
            string[] elements = { "gender", "birthDate" };
            var tag = new Coding(string.Empty, Guid.NewGuid().ToString());

            Patient[] patients = await CreatePatientsWithSpecifiedElements(tag, elements);

            Bundle bundle = await Client.SearchAsync($"Patient?_tag={tag.Code}&_elements=invalidProperty,{string.Join(',', elements)}");

            ValidateBundle(bundle, patients);
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

            Assert.Equal(expectedErrorMessage, ex.Message);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenListOfResources_WhenSearchedWithElementsAndSummarySetToFalse_ThenOnlySpecifiedPropertiesShouldBeReturned()
        {
            string[] elements = { "gender", "birthDate" };
            var tag = new Coding(string.Empty, Guid.NewGuid().ToString());

            Patient[] patients = await CreatePatientsWithSpecifiedElements(tag, elements);

            Bundle bundle = await Client.SearchAsync($"Patient?_tag={tag.Code}&_elements={string.Join(',', elements)}&_summary=false");

            ValidateBundle(bundle, patients);
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

            Assert.Equal(expectedErrorMessage, ex.Message);
        }

        [InlineData("_total", "count")]
        [InlineData("_total", "xyz")]
        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenListOfResources_WhenSearchedWithInvalidTotalType_ThenExceptionShouldBeThrown(string key, string val)
        {
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(3);
            using FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.SearchAsync($"Patient?{key}={val}"));

            var expectedStatusCode = HttpStatusCode.BadRequest;
            Assert.Equal(expectedStatusCode, ex.StatusCode);

            var supportedTotalTypes = new string($"'{TotalType.Accurate}', '{TotalType.None}'").ToLower(CultureInfo.CurrentCulture);
            var expectedErrorMessage = $"{expectedStatusCode}: " + string.Format(Core.Resources.InvalidTotalParameter, val, supportedTotalTypes);

            Assert.Equal(expectedErrorMessage, ex.Message);
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

        private async Task<Patient[]> CreatePatientsWithSpecifiedElements(Coding tag, string[] elements)
        {
            const int numberOfResources = 3;

            Patient patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            var patients = new Patient[numberOfResources];

            for (int i = 0; i < numberOfResources; i++)
            {
                patient.Meta = new Meta();
                patient.Meta.Tag.Add(tag);

                FhirResponse<Patient> createdPatient = await Client.CreateAsync(patient);
                patients[i] = MaskingNode.ForElements(new ScopedNode(createdPatient.Resource.ToTypedElement()), elements)
                    .ToPoco<Patient>();
            }

            return patients;
        }
    }
}
