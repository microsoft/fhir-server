// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Web;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Shared.Tests.E2E.Rest.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using static Hl7.Fhir.Model.OperationOutcome;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class IncludeSearchTests : SearchTestsBase<IncludeSearchTestFixture>
    {
        public IncludeSearchTests(IncludeSearchTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        public async Task GivenAnIncludeSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_include=Location:organization:Organization&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Location, query);

            ValidateBundle(
                bundle,
                Fixture.Organization,
                Fixture.Location);

            ValidateSearchEntryMode(bundle, ResourceType.Location);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.Location, query, bundle.Link[0].Url);

            // ensure that the included resources are not counted
            bundle = await Client.SearchAsync(ResourceType.Location, $"{query}&_summary=count");
            Assert.Equal(1, bundle.Total);

            // ensure that the included resources are not counted when _total is specified and the results fit in a single bundle.
            bundle = await Client.SearchAsync(ResourceType.Location, $"{query}&_total=accurate");
            Assert.Equal(1, bundle.Total);
        }

        /// <summary>
        /// Ensures that the _id predicate is not applied to the included resources.
        /// </summary>
        [Fact]
        public async Task GivenAnIncludeSearchExpressionWithAPredicateOnId_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_include=Location:organization:Organization&_tag={Fixture.Tag}&_id={Fixture.Location.Id}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Location, query);

            ValidateBundle(
                bundle,
                Fixture.Organization,
                Fixture.Location);

            ValidateSearchEntryMode(bundle, ResourceType.Location);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.Location, query, bundle.Link[0].Url);

            // ensure that the included resources are not counted
            bundle = await Client.SearchAsync(ResourceType.Location, $"{query}&_summary=count");
            Assert.Equal(1, bundle.Total);

            // ensure that the included resources are not counted when _total is specified and the results fit in a single bundle.
            bundle = await Client.SearchAsync(ResourceType.Location, $"{query}&_total=accurate");
            Assert.Equal(1, bundle.Total);
        }

        [Fact]
        public async Task GivenAnIncludeSearchExpression_WhenSearchedWithPost_ThenCorrectBundleShouldBeReturned()
        {
            Bundle bundle = await Client.SearchPostAsync(ResourceType.Location.ToString(), default, ("_include", "Location:organization:Organization"), ("_tag", Fixture.Tag));

            ValidateBundle(
                bundle,
                Fixture.Organization,
                Fixture.Location);

            ValidateSearchEntryMode(bundle, ResourceType.Location);
        }

        [Fact]
        public async Task GivenAnIncludeSearchExpressionWithOnlyResourceTablePredicates_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string lastUpdatedString = Uri.EscapeDataString(Fixture.PatientGroup.Meta.LastUpdated.Value.ToString("o"));

            FhirResponse<Bundle> results = await Client.SearchAsync(ResourceType.Group, $"_lastUpdated={lastUpdatedString}&_include=Group:member:Patient");
            Assert.Contains(results.Resource.Entry, e => e.Search.Mode == Bundle.SearchEntryMode.Match && e.Resource.Id == Fixture.PatientGroup.Id);
            Assert.Contains(results.Resource.Entry, e => e.Search.Mode == Bundle.SearchEntryMode.Include && e.Resource.Id == Fixture.AdamsPatient.Id);
        }

        [Fact]
        public async Task GivenAnIncludeSearchExpressionWithMultipleResourceTableParameters_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            var guid = Guid.NewGuid().ToString();
            var organizationResponse = await Client.CreateAsync(new Organization());

            var locationResponse = await Client.CreateAsync(new Location
            {
                ManagingOrganization = new ResourceReference($"Organization/{organizationResponse.Resource.Id}"),
                Meta = new Meta { Tag = new List<Coding> { new Coding("testTag", guid) } },
            });

            // Make sure that the second location ends up in a different bucket of resource surrogate ids
            await Task.Delay(100);

            var locationResponse2 = await Client.CreateAsync(new Location
            {
                ManagingOrganization = new ResourceReference($"Organization/{organizationResponse.Resource.Id}"),
                Meta = new Meta { Tag = new List<Coding> { new Coding("testTag", guid) } },
            });

            // Format the time to fit yyyy-MM-ddTHH:mm:ss.fffffffzzz, and encode its special characters.
            string lastUpdated = HttpUtility.UrlEncode($"{locationResponse2.Resource.Meta.LastUpdated:o}");
            var query = $"_include=Location:organization:Organization&_lastUpdated=lt{lastUpdated}&_tag={guid}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Location, query);

            ValidateBundle(
                bundle,
                organizationResponse.Resource,
                locationResponse.Resource);

            ValidateSearchEntryMode(bundle, ResourceType.Location);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.Location, query, bundle.Link[0].Url);
        }

        [Fact]
        public async Task GivenAnIncludeSearchExpressionWithSimpleSearch_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_include=DiagnosticReport:patient:Patient&code=429858000";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(
                bundle,
                Fixture.SmithSnomedDiagnosticReport,
                Fixture.SmithPatient,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanPatient);

            ValidateSearchEntryMode(bundle, ResourceType.DiagnosticReport);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.DiagnosticReport, query, bundle.Link[0].Url);
        }

        [Fact]
        public async Task GivenAnIncludeSearchExpressionWithMissingModifier_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_include=DiagnosticReport:patient:Patient&code=429858000&specimen:missing=true";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(
                bundle,
                Fixture.SmithSnomedDiagnosticReport,
                Fixture.SmithPatient,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanPatient);

            ValidateSearchEntryMode(bundle, ResourceType.DiagnosticReport);
        }

        [Fact]
        public async Task GivenAnIncludeSearchExpressionWithSimpleSearchAndCount_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_include=DiagnosticReport:patient:Patient&code=429858000&_count=1";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(
                bundle,
                Fixture.SmithSnomedDiagnosticReport,
                Fixture.SmithPatient);

            ValidateSearchEntryMode(bundle, ResourceType.DiagnosticReport);

            bundle = await Client.SearchAsync(bundle.NextLink.ToString());

            ValidateBundle(
                bundle,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanPatient);

            ValidateSearchEntryMode(bundle, ResourceType.DiagnosticReport);
        }

        [Fact]
        public async Task GivenAnIncludeSearchExpressionWithWildcard_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_include=DiagnosticReport:*&code=429858000";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(
                bundle,
                Fixture.SmithSnomedDiagnosticReport,
                Fixture.SmithPatient,
                Fixture.SmithSnomedObservation,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanPatient,
                Fixture.TrumanSnomedObservation);

            ValidateSearchEntryMode(bundle, ResourceType.DiagnosticReport);
        }

        [Fact]
        public async Task GivenAnIncludeSearchExpressionWithMultipleIncludes_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_include=DiagnosticReport:patient:Patient&_include=DiagnosticReport:result:Observation&code=429858000";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(
                bundle,
                Fixture.SmithSnomedDiagnosticReport,
                Fixture.SmithPatient,
                Fixture.SmithSnomedObservation,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanPatient,
                Fixture.TrumanSnomedObservation);

            ValidateSearchEntryMode(bundle, ResourceType.DiagnosticReport);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.DiagnosticReport, query, bundle.Link[0].Url);
        }

        [Fact]
        public async Task GivenAnIncludeSearchExpressionWithMultipleResourceTableParametersAndTableParameters_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            var newDiagnosticReportResponse = await Fixture.TestFhirClient.CreateAsync(
                new DiagnosticReport
                {
                    Meta = new Meta { Tag = new List<Coding> { new Coding("testTag", Fixture.Tag) } },
                    Status = DiagnosticReport.DiagnosticReportStatus.Final,
                    Code = new CodeableConcept("http://snomed.info/sct", "429858000"),
                    Subject = new ResourceReference($"Patient/{Fixture.TrumanPatient.Id}"),
                    Result = new List<ResourceReference> { new ResourceReference($"Observation/{Fixture.TrumanSnomedObservation.Id}") },
                });

            // Format the time to fit yyyy-MM-ddTHH:mm:ss.fffffffzzz, and encode its special characters.
            string lastUpdated = HttpUtility.UrlEncode($"{Fixture.PatientGroup.Meta.LastUpdated:o}");
            string query = $"_tag={Fixture.Tag}&_include=DiagnosticReport:patient:Patient&_include=DiagnosticReport:result:Observation&code=429858000&_lastUpdated=lt{lastUpdated}";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(
                bundle,
                Fixture.SmithSnomedDiagnosticReport,
                Fixture.SmithPatient,
                Fixture.SmithSnomedObservation,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanPatient,
                Fixture.TrumanSnomedObservation);

            ValidateSearchEntryMode(bundle, ResourceType.DiagnosticReport);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.DiagnosticReport, query, bundle.Link[0].Url);

            // delete the extra entry added
            await Fixture.TestFhirClient.DeleteAsync(newDiagnosticReportResponse.Resource);
        }

        [Fact]
        public async Task GivenAnIncludeSearchExpressionWithNoTargetType_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_include=Observation:performer";

            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsLoincObservation,
                Fixture.SmithLoincObservation,
                Fixture.SmithSnomedObservation,
                Fixture.TrumanLoincObservation,
                Fixture.TrumanSnomedObservation,
                Fixture.ObservationWithUntypedReferences,
                Fixture.Practitioner,
                Fixture.Organization);

            ValidateSearchEntryMode(bundle, ResourceType.Observation);
        }

        [Fact]
        public async Task GivenAnIncludeSearchExpression_WhenSearched_DoesNotIncludeUntypedReferences()
        {
            string query = $"_id={Fixture.ObservationWithUntypedReferences.Id}&_include=Observation:*";

            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, query);

            ValidateBundle(
                bundle,
                Fixture.ObservationWithUntypedReferences);

            ValidateSearchEntryMode(bundle, ResourceType.Observation);
        }

        [Fact]
        public async Task GivenAnIncludeSearchExpression_WhenSearched_DoesnotIncludeDeletedOrUpdatedResources()
        {
            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, $"_tag={Fixture.Tag}&_include=Patient:organization");

            ValidateSearchEntryMode(bundle, ResourceType.Patient);
            ValidateBundle(
                bundle,
                Fixture.PatiPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.AdamsPatient,
                Fixture.PatientWithDeletedOrganization,
                Fixture.Organization);
        }

        [Fact]
        public async Task GivenAnRevIncludeSearchExpression_WhenSearched_DoesnotIncludeDeletedResources()
        {
            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, $"_tag={Fixture.Tag}&_revinclude=Device:patient");

            ValidateSearchEntryMode(bundle, ResourceType.Patient);
            ValidateBundle(
                bundle,
                Fixture.PatiPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.AdamsPatient,
                Fixture.PatientWithDeletedOrganization);
        }

        // RevInclude
        [Fact]
        public async Task GivenARevIncludeSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            // Ask for reverse include to get all Locations which reference an org
            string query = $"_revinclude=Location:organization&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Organization, query);

            ValidateBundle(
                bundle,
                Fixture.Location,
                Fixture.Organization,
                Fixture.LabAOrganization,
                Fixture.LabBOrganization,
                Fixture.LabCOrganization,
                Fixture.LabDOrganization,
                Fixture.LabEOrganization,
                Fixture.LabFOrganization);

            ValidateSearchEntryMode(bundle, ResourceType.Organization);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.Organization, query, bundle.Link[0].Url);

            // ensure that the included resources are not counted
            bundle = await Client.SearchAsync(ResourceType.Organization, $"{query}&_summary=count");
            Assert.Equal(7, bundle.Total);

            // ensure that the included resources are not counted when _total is specified and the results fit in a single bundle.
            bundle = await Client.SearchAsync(ResourceType.Organization, $"{query}&_total=accurate");
            Assert.Equal(7, bundle.Total);
        }

        [Fact]
        public async Task GivenARevIncludeSearchExpression_WhenSearchedWithPost_ThenCorrectBundleShouldBeReturned()
        {
            Bundle bundle = await Client.SearchPostAsync(ResourceType.Organization.ToString(), default, ("_revinclude", "Location:organization"), ("_tag", Fixture.Tag));

            ValidateBundle(
                bundle,
                Fixture.Location,
                Fixture.Organization,
                Fixture.LabAOrganization,
                Fixture.LabBOrganization,
                Fixture.LabCOrganization,
                Fixture.LabDOrganization,
                Fixture.LabEOrganization,
                Fixture.LabFOrganization);

            ValidateSearchEntryMode(bundle, ResourceType.Organization);
        }

        [Fact]
        public async Task GivenARevIncludeSearchExpressionWithSimpleSearch_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_revinclude=DiagnosticReport:result&code=429858000";

            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, query);

            ValidateBundle(
                bundle,
                Fixture.SmithSnomedDiagnosticReport,
                Fixture.SmithSnomedObservation,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanSnomedObservation);

            ValidateSearchEntryMode(bundle, ResourceType.Observation);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.Observation, query, bundle.Link[0].Url);
        }

        [Fact]
        public async Task GivenARevIncludeSearchExpressionWithSimpleSearchAndCount_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_revinclude=DiagnosticReport:result&code=429858000&_count=1";

            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, query);

            ValidateBundle(
                bundle,
                Fixture.SmithSnomedDiagnosticReport,
                Fixture.SmithSnomedObservation);

            ValidateSearchEntryMode(bundle, ResourceType.Observation);

            bundle = await Client.SearchAsync(bundle.NextLink.ToString());

            ValidateBundle(
                bundle,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanSnomedObservation);

            ValidateSearchEntryMode(bundle, ResourceType.Observation);
        }

        [Fact]
        public async Task GivenARevIncludeSearchExpressionWithWildcard_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_revinclude=DiagnosticReport:*&code=429858000";

            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, query);

            ValidateBundle(
                bundle,
                Fixture.SmithSnomedDiagnosticReport,
                Fixture.SmithSnomedObservation,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanSnomedObservation);

            ValidateSearchEntryMode(bundle, ResourceType.Observation);
        }

        [Fact]
        public async Task GivenARevIncludeSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturnedAndNothingElse()
        {
            string query = $"_tag={Fixture.Tag}&_revinclude=Observation:patient&family=Truman";

            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query);

            ValidateBundle(
                bundle,
                Fixture.TrumanPatient,
                Fixture.TrumanSnomedObservation,
                Fixture.TrumanLoincObservation);

            ValidateSearchEntryMode(bundle, ResourceType.Patient);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.Patient, query, bundle.Link[0].Url);
        }

        [Fact]
        public async Task GivenARevIncludeSearchExpressionWithMultipleIncludes_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_revinclude=DiagnosticReport:patient&_revinclude=Observation:patient&family=Truman";

            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query);

            ValidateBundle(
                bundle,
                Fixture.TrumanPatient,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanSnomedObservation,
                Fixture.TrumanLoincDiagnosticReport,
                Fixture.TrumanLoincObservation);

            ValidateSearchEntryMode(bundle, ResourceType.Patient);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.Patient, query, bundle.Link[0].Url);
        }

        [InlineData("_include")]
        [InlineData("_revinclude")]
        [Theory]
        public async Task GivenAnIncludeSearchExpressionWithLocationLinkedToItself_WhenSearched_ThenCorrectBundleShouldBeReturned(string includeType)
        {
            string query = $"_id={Fixture.LocationPartOfSelf.Id}&{includeType}=Location:partof";

            Bundle bundle = await Client.SearchAsync(ResourceType.Location, query);

            // The matched resource shouldn't be returned as an include
            ValidateBundle(
                bundle,
                Fixture.LocationPartOfSelf);

            ValidateSearchEntryMode(bundle, ResourceType.Location);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.Location, query, bundle.Link[0].Url);
        }

        [Fact]
        public async Task GivenARevIncludeSearchExpressionWithMultipleResourceTableParametersAndTableParameters_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            var newDiagnosticReportResponse = await Fixture.TestFhirClient.CreateAsync(
                new DiagnosticReport
                {
                    Meta = new Meta { Tag = new List<Coding> { new Coding("testTag", Fixture.Tag) } },
                    Status = DiagnosticReport.DiagnosticReportStatus.Final,
                    Code = new CodeableConcept("http://snomed.info/sct", "429858000"),
                    Subject = new ResourceReference($"Patient/{Fixture.TrumanPatient.Id}"),
                    Result = new List<ResourceReference> { new ResourceReference($"Observation/{Fixture.TrumanSnomedObservation.Id}") },
                });

            // Format the time to fit yyyy-MM-ddTHH:mm:ss.fffffffzzz, and encode its special characters.
            string lastUpdated = HttpUtility.UrlEncode($"{Fixture.PatientGroup.Meta.LastUpdated:o}");
            string query = $"_tag={Fixture.Tag}&_revinclude=DiagnosticReport:result&code=429858000&_lastUpdated=lt{lastUpdated}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, query);

            ValidateBundle(
                bundle,
                Fixture.SmithSnomedDiagnosticReport,
                Fixture.SmithSnomedObservation,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanSnomedObservation,
                newDiagnosticReportResponse.Resource);

            ValidateSearchEntryMode(bundle, ResourceType.Observation);

            // delete the extra entry added
            await Fixture.TestFhirClient.DeleteAsync(newDiagnosticReportResponse.Resource);
        }

        [Fact]
        public async Task GivenARevIncludeSearchExpressionWithNoReferences_WhenSearched_ThenCorrectBundleWithOnlyMatchesShouldBeReturned()
        {
            // looking for an appointment referencing a Patient, however this kind of reference was
            // not created in this fixture.
            string query = $"_tag={Fixture.Tag}&_revinclude=Appointment:actor";

            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query);

            ValidateBundle(
                bundle,
                Fixture.PatiPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.AdamsPatient,
                Fixture.PatientWithDeletedOrganization);

            ValidateSearchEntryMode(bundle, ResourceType.Patient);
        }

        // Include Iterate

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenAnIncludeIterateSearchExpressionWithSingleIteration_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration- Single iteration (_include:iterate)
            string query = $"_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:patient&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispenseWithoutRequest,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.AdamsPatient,
                Fixture.SmithPatient);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.MedicationDispense, query, bundle.Link[0].Url);

            // ensure that the included resources are not counted
            bundle = await Client.SearchAsync(ResourceType.MedicationDispense, $"{query}&_summary=count");
            Assert.Equal(3, bundle.Total);

            // ensure that the included resources are not counted when _total is specified and the results fit in a single bundle.
            bundle = await Client.SearchAsync(ResourceType.MedicationDispense, $"{query}&_total=accurate");
            Assert.Equal(3, bundle.Total);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenAnIncludeRecurseSearchExpressionWithSingleIteration_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration- Single iteration (_include:recurse)
            string query = $"_include=MedicationDispense:prescription&_include:recurse=MedicationRequest:patient&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispenseWithoutRequest,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.AdamsPatient,
                Fixture.SmithPatient);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.MedicationDispense, query, bundle.Link[0].Url);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenAnIncludeIterateSearchExpressionWithAdditionalParameters_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Single iteration (_include:iterate)
            string query = $"_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:patient&_id={Fixture.AdamsMedicationDispense.Id}&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.AdamsMedicationRequest,
                Fixture.AdamsPatient);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.MedicationDispense, query, bundle.Link[0].Url);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenAnIncludeIterateSearchExpressionWithMultipleIterations_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Multiple iterations
            string query = $"_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:patient&_include:iterate=Patient:general-practitioner&_include:iterate=Patient:organization&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispenseWithoutRequest,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner,
                Fixture.Organization);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.MedicationDispense, query, bundle.Link[0].Url);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenAnIncludeIterateSearchExpressionWithIncludeIterateParametersBeforeIncludeParameters_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Iteration order doesn't matter
            string query = $"_include:iterate=Patient:organization&_include:iterate=Patient:general-practitioner&_include:iterate=MedicationRequest:patient&_include=MedicationDispense:prescription&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispenseWithoutRequest,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner,
                Fixture.Organization);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.MedicationDispense, query, bundle.Link[0].Url);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenAnIncludeIterateSearchExpressionWithMultitypeReference_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Single reference to multiple target types: MedicationRequest:subject could be Patient or Group
            string query = $"_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:subject&_include:iterate=Patient:general-practitioner&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispenseWithoutRequest,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenAnIncludeIterateSearchExpressionWithMultitypeArrayReference_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Reference array of multiple target types: CareTeam:participant of type Patient, Practitioner, Organization, etc.
            string query = $"_include=CareTeam:participant:Patient&_include:iterate=Patient:general-practitioner&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.CareTeam, query);

            ValidateBundle(
                bundle,
                Fixture.CareTeam,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner,
                Fixture.TaylorPractitioner);

            ValidateSearchEntryMode(bundle, ResourceType.CareTeam);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenAnIncludeIterateSearchExpressionWithSpecificTargetType_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Specific target type: CareTeam:participant:Patient
            string query = $"_include=CareTeam:participant:Patient&_include:iterate=Patient:general-practitioner&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.CareTeam, query);

            ValidateBundle(
                bundle,
                Fixture.CareTeam,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner,
                Fixture.TaylorPractitioner);

            ValidateSearchEntryMode(bundle, ResourceType.CareTeam);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenAnIncludeIterateSearchExpressionWithMultitypeTargetReferenceWithOverlap_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Multi-type target reference type already included: MedicationDispense:patient and MedicationRequest:subject
            string query = $"_include=MedicationDispense:patient&_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:subject&_include:iterate=Patient:general-practitioner&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispenseWithoutRequest,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner,
                Fixture.TaylorPractitioner);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenAnIncludeIterateSearchExpressionWithMultipleResultsSets_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Multiple result sets: MedicationDispense:patient and MedicationRequest:patient
            string query = $"_include=MedicationDispense:patient&_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:patient&_include:iterate=Patient:general-practitioner&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispenseWithoutRequest,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner,
                Fixture.TaylorPractitioner);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenAnIncludeSearchExpressionWithWildcardAndIncludeIterate_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_include=MedicationRequest:*&_include:iterate=Patient:general-practitioner";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationRequest, query);

            ValidateBundle(
                bundle,
#if R5
                Fixture.PercocetMedication,
#endif
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationRequest);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenAnIncludeSearchExpressionWithIncludeWildcardAndIncludeIterateWildcard_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_include=MedicationRequest:*&_include:iterate=Patient:*";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationRequest, query);

            ValidateBundle(
                bundle,
#if R5
                Fixture.PercocetMedication,
#endif
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner,
                Fixture.Organization);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationRequest);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenAnIncludeSearchExpressionWithIncludeIterateWildcard_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_include=MedicationRequest:patient&_include:iterate=Patient:*";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationRequest, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner,
                Fixture.Organization);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationRequest);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenAnIncludeMedication_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            string query = $"_include=MedicationDispense:medication&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
#if R5
                // In R5 Medication is a codeable reference, otherwise, an embedded codebale concept.
                Fixture.TramadolMedication,
#endif
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispenseWithoutRequest);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);
        }

        // RecInclude Iterate
        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenARevIncludeIterateSearchExpressionWithSingleIteration_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Single iteration (_revinclude:iterate)
            string query = $"_revinclude=MedicationRequest:patient&_revinclude:iterate=MedicationDispense:prescription&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query);

            ValidateBundle(
                bundle,
                Fixture.PatiPatient,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.PatientWithDeletedOrganization,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense);

            ValidateSearchEntryMode(bundle, ResourceType.Patient);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.Patient, query, bundle.Link[0].Url);

            // ensure that the included resources are not counted
            bundle = await Client.SearchAsync(ResourceType.Patient, $"{query}&_summary=count");
            Assert.Equal(5, bundle.Total);

            // ensure that the included resources are not counted when _total is specified and the results fit in a single bundle.
            bundle = await Client.SearchAsync(ResourceType.Patient, $"{query}&_total=accurate");
            Assert.Equal(5, bundle.Total);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenARevIncludeIterateSearchExpressionWithSingleIteration_WhenSearchedAndSorted_TheIterativeResultsShouldBeAddedToTheBundleAsc()
        {
            // Non-recursive iteration - Single iteration (_revinclude:iterate)
            string query = $"_revinclude=MedicationRequest:patient&_revinclude:iterate=MedicationDispense:prescription&_tag={Fixture.Tag}&_sort=birthdate";

            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query);

            ValidateBundle(
                bundle,
                Fixture.PatiPatient,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.PatientWithDeletedOrganization,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense);

            ValidateSearchEntryMode(bundle, ResourceType.Patient);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.Patient, query, bundle.Link[0].Url);

            // ensure that the included resources are not counted
            bundle = await Client.SearchAsync(ResourceType.Patient, $"{query}&_summary=count");
            Assert.Equal(5, bundle.Total);

            // ensure that the included resources are not counted when _total is specified and the results fit in a single bundle.
            bundle = await Client.SearchAsync(ResourceType.Patient, $"{query}&_total=accurate");
            Assert.Equal(5, bundle.Total);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenARevIncludeRecurseSearchExpressionWithSingleIteration_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Single iteration (_revinclude:recurse)
            string query = $"_revinclude=MedicationRequest:patient&_revinclude:recurse=MedicationDispense:prescription&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query);

            ValidateBundle(
                bundle,
                Fixture.PatiPatient,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.PatientWithDeletedOrganization,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense);

            ValidateSearchEntryMode(bundle, ResourceType.Patient);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.Patient, query, bundle.Link[0].Url);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenARevIncludeIterateSearchExpressionWithAdditionalParameters_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Single iteration (_revinclude:iterate)
            string query = $"_revinclude=MedicationRequest:patient&_revinclude:iterate=MedicationDispense:prescription&_id={Fixture.AdamsPatient.Id}&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.AdamsMedicationRequest,
                Fixture.AdamsPatient);

            ValidateSearchEntryMode(bundle, ResourceType.Patient);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.Patient, query, bundle.Link[0].Url);
        }

#if Stu3
        // The following tests are enabled only on Stu3 version due to this issue: https://github.com/microsoft/fhir-server/issues/1308
        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenARevIncludeIterateSearchExpressionWithMultipleIterations_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Multiple iterations
            string query = $"_revinclude:iterate=MedicationDispense:prescription&_revinclude:iterate=MedicationRequest:patient&_revinclude=Patient:organization&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Organization, query);

            ValidateBundle(
                bundle,
                Fixture.Organization,
                Fixture.LabAOrganization,
                Fixture.LabBOrganization,
                Fixture.LabCOrganization,
                Fixture.LabDOrganization,
                Fixture.LabEOrganization,
                Fixture.LabFOrganization,
                Fixture.PatiPatient,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense);

            ValidateSearchEntryMode(bundle, ResourceType.Organization);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.Organization, query, bundle.Link[0].Url);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenARevIncludeIterateSearchExpressionWithRevIncludeIterateParametersBeforeRevIncludeParameters_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Iteration order doesn't matter
            string query = $"_revinclude:iterate=MedicationDispense:prescription&_revinclude:iterate=MedicationRequest:patient&_revinclude=Patient:organization&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Organization, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.PatiPatient,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.Organization,
                Fixture.LabAOrganization,
                Fixture.LabBOrganization,
                Fixture.LabCOrganization,
                Fixture.LabDOrganization,
                Fixture.LabEOrganization,
                Fixture.LabFOrganization);

            ValidateSearchEntryMode(bundle, ResourceType.Organization);

            ValidateBundleUrl(Client.HttpClient.BaseAddress, ResourceType.Organization, query, bundle.Link[0].Url);
        }
#endif

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenARevIncludeIterateSearchExpressionWithMultiTypeReferenceSpecifiedTarget_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Single reference to multiple target types: MedicationRequest:subject could be Patient or Group
            string query = $"_revinclude:iterate=MedicationDispense:prescription&_revinclude:iterate=MedicationRequest:subject:Patient&_revinclude=Patient:general-practitioner&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Practitioner, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.PatiPatient,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.PatientWithDeletedOrganization,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner,
                Fixture.TaylorPractitioner,
                Fixture.Practitioner);

            ValidateSearchEntryMode(bundle, ResourceType.Practitioner);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenARevIncludeIterateSearchExpressionWithMultitypeArrayReference_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Reference array of multiple target types: CareTeam:participant of type Patient, Practitioner, Organization, etc.
            // CareTeam:participant is a circular reference, however CareTeam:participant:Patient isn't, so we're not expecting an informational Issue
            string query = $"_revinclude:iterate=CareTeam:participant:Patient&_revinclude=Patient:general-practitioner&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Practitioner, query);

            ValidateBundle(
                bundle,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner,
                Fixture.TaylorPractitioner,
                Fixture.Practitioner,
                Fixture.PatiPatient,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.PatientWithDeletedOrganization,
                Fixture.CareTeam);

            ValidateSearchEntryMode(bundle, ResourceType.Practitioner);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenARevIncludeIterateSearchExpressionWithMultipleResultsSets_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Multiple result sets: MedicationDispense:performer:Practitioner and MedicationRequest:requester:Practitioner
            string query = $"_include=MedicationDispense:performer:Practitioner&_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:requester:Practitioner&_revinclude:iterate=Patient:general-practitioner:Practitioner&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispenseWithoutRequest,
                Fixture.Practitioner,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.PatientWithDeletedOrganization,
                Fixture.PatiPatient);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenARevIncludeIterateSearchExpressionWithMultipleResultsSetsWithoutSpecificRevIncludeIterateTargetType_WhenSearched_ShouldThrowBadRequestExceptionWithIssue()
        {
            // Non-recursive iteration - Multiple result sets: MedicationDispense:performer;Practitioner and MedicationRequest:requester:Practitioner
            string query = $"_include=MedicationDispense:performer:Practitioner&_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:requester:Practitioner&_revinclude:iterate=Patient:general-practitioner&_tag={Fixture.Tag}";

            using var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await Client.SearchAsync(ResourceType.MedicationDispense, query));
            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);

            string[] expectedDiagnostics = { string.Format(Core.Resources.RevIncludeIterateTargetTypeNotSpecified, "Patient:general-practitioner") };
            IssueSeverity[] expectedIssueSeverities = { IssueSeverity.Error };
            IssueType[] expectedCodeTypes = { IssueType.Invalid };
            ValidateOperationOutcome(expectedDiagnostics, expectedIssueSeverities, expectedCodeTypes, fhirException.OperationOutcome);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenARevIncludeMedication_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            string query = $"_revinclude=MedicationDispense:medication&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Medication, query);

            ValidateBundle(
                bundle,
#if R5
                // In R5 Medication is a codeable reference, otherwise, an embedded codebale concept.
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispenseWithoutRequest,
#endif
                Fixture.TramadolMedication,
                Fixture.PercocetMedication);

            ValidateSearchEntryMode(bundle, ResourceType.Medication);
        }

#if Stu3
        // This test is enabled only on Stu3 version due to this issue: https://github.com/microsoft/fhir-server/issues/1308
        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenARevIncludeIterateSearchExpressionWithRevIncludeWildCard_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            string query = $"_revinclude=Patient:*&_revinclude:iterate=MedicationRequest:patient&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Practitioner, query);

            ValidateBundle(
                bundle,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner,
                Fixture.TaylorPractitioner,
                Fixture.Practitioner,
                Fixture.PatiPatient,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.PatientWithDeletedOrganization,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest);

            ValidateSearchEntryMode(bundle, ResourceType.Practitioner);
        }
#endif

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenARevIncludeIterateSearchExpressionWithRevIncludeIterateWildCard_WhenSearched_TheIterateWildcardShouldBeIgnored()
        {
            string query = $"_revinclude:iterate=MedicationRequest:*&_revinclude=Patient:general-practitioner&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Practitioner, query);

            ValidateBundle(
                bundle,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner,
                Fixture.TaylorPractitioner,
                Fixture.Practitioner,
                Fixture.PatiPatient,
                Fixture.PatientWithDeletedOrganization,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient);

            ValidateSearchEntryMode(bundle, ResourceType.Practitioner);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenARevIncludeIterateSearchExpressionWithRevIncludeWildcardAndRevIncludeIterateWildcard_WhenSearched_TheIterateWildcardShouldBeIgnored()
        {
            string query = $"_revinclude:iterate=MedicationDispense:*&_revinclude=MedicationRequest:*&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.PatiPatient,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.PatientWithDeletedOrganization);

            ValidateSearchEntryMode(bundle, ResourceType.Patient);
        }

        // Circular Reference - Iteration executed once

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenAnIncludeIterateSearchExpressionWithCircularReference_WhenSearched_SingleIterationIsExecutedAndInformationalIssueIsAdded()
        {
            // Recursive queries (circular references) are not supported (see https://github.com/microsoft/fhir-server/issues/1310)
            // Here we expect a single iteration of included results
            string query = $"_include:iterate=Organization:partof&_id={Fixture.LabAOrganization.Id}&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Organization, query);

            // Create OperationOutcome with Informational Issue
            var issue = new IssueComponent
            {
                Code = IssueType.Informational,
                Diagnostics = string.Format(Core.Resources.IncludeIterateCircularReferenceExecutedOnce, "_include:iterate", "Organization:partof"),
                Severity = IssueSeverity.Information,
            };

            var operationOutcome = new OperationOutcome
            {
                Id = bundle.Id,
                Issue = new List<OperationOutcome.IssueComponent> { issue },
            };

            ValidateBundle(
                bundle,
                operationOutcome,
                Fixture.LabAOrganization,
                Fixture.LabBOrganization);

            var expectedSearchEntryModes = new Dictionary<string, Bundle.SearchEntryMode>
            {
                { Fixture.LabAOrganization.Id, Bundle.SearchEntryMode.Match },
                { Fixture.LabBOrganization.Id, Bundle.SearchEntryMode.Include },
            };
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenARevIncludeIterateSearchExpressionWithCircularReference_WhenSearched_SingleIterationIsExecutedAndInformationalIssueIsAdded()
        {
            // Recursive include iterate queries (circular references) are not supported (see https://github.com/microsoft/fhir-server/issues/1310)
            // Here we expect a single iteration of included results
            string query = $"_revinclude:iterate=Organization:partof&_id={Fixture.LabBOrganization.Id}&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Organization, query);

            // Create OperationOutcome with Informational Issue
            var issue = new IssueComponent
            {
                Code = IssueType.Informational,
                Diagnostics = string.Format(Core.Resources.IncludeIterateCircularReferenceExecutedOnce, "_revinclude:iterate", "Organization:partof"),
                Severity = IssueSeverity.Information,
            };

            var operationOutcome = new OperationOutcome
            {
                Id = bundle.Id,
                Issue = new List<OperationOutcome.IssueComponent> { issue },
            };

            ValidateBundle(
                bundle,
                operationOutcome,
                Fixture.LabAOrganization,
                Fixture.LabBOrganization);

            var expectedSearchEntryModes = new Dictionary<string, Bundle.SearchEntryMode>
            {
                { Fixture.LabBOrganization.Id, Bundle.SearchEntryMode.Match },
                { Fixture.LabAOrganization.Id, Bundle.SearchEntryMode.Include },
            };
        }

        [Theory]
        [InlineData("_include")]
        [InlineData("_revinclude")]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenAIncludeOrRevIncludeIterateSearchExpressionWithInvalidTargetResourceType_WhenSearched_ShouldThrowResourceNotSupportedException(string include)
        {
            string query = $"{include}=Observation:subject:NotAResourceType";

            using var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await Client.SearchAsync(ResourceType.Patient, query));
            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);

            string[] expectedDiagnostics = { string.Format(Core.Resources.ResourceNotSupported, "NotAResourceType") };
            IssueSeverity[] expectedIssueSeverities = { IssueSeverity.Error };
            IssueType[] expectedCodeTypes = { IssueType.NotSupported };
            ValidateOperationOutcome(expectedDiagnostics, expectedIssueSeverities, expectedCodeTypes, fhirException.OperationOutcome);
        }

        [Theory]
        [InlineData("_include", "")]
        [InlineData("_include", "   ")]
        [InlineData("_revinclude", "")]
        [InlineData("_revinclude", "   ")]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenAIncludeOrRevIncludeIterateSearchExpressionWithEmptyOrWhiteSpaceTargetResourceType_WhenSearched_ShouldThrowBadRequestException(string include, string target)
        {
            string query = $"{include}=Observation:subject:{target}";

            using var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await Client.SearchAsync(ResourceType.Patient, query));
            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);

            string[] expectedDiagnostics = { string.Format(Core.Resources.IncludeRevIncludeInvalidTargetResourceType) };
            IssueSeverity[] expectedIssueSeverities = { IssueSeverity.Error };
            IssueType[] expectedCodeTypes = { IssueType.Invalid };
            ValidateOperationOutcome(expectedDiagnostics, expectedIssueSeverities, expectedCodeTypes, fhirException.OperationOutcome);
        }

        // This will not work for circular reference
        private static void ValidateSearchEntryMode(Bundle bundle, ResourceType matchResourceType)
        {
            foreach (Bundle.EntryComponent entry in bundle.Entry)
            {
                var searchEntryMode = entry.Resource.TypeName == matchResourceType.ToString() ? Bundle.SearchEntryMode.Match : Bundle.SearchEntryMode.Include;
                Assert.Equal(searchEntryMode, entry.Search.Mode);
            }
        }

        // This should be used with circular references
        private static void ValidateSearchEntryMode(Bundle bundle, IDictionary<string, Bundle.SearchEntryMode> expectedSearchEntryModes)
        {
            foreach (Bundle.EntryComponent entry in bundle.Entry)
            {
                Assert.Equal(expectedSearchEntryModes[entry.Resource.Id], entry.Search.Mode);
            }
        }
    }
}
