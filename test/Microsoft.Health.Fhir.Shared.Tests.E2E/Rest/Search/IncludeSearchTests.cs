// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Web;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Shared.Tests.E2E.Rest.Search;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
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
        public async Task GivenAnIncludeSearchExpressionWithMultipleDenormalizedParameters_WhenSearched_ThenCorrectBundleShouldBeReturned()
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
        }

        [Fact]
        public async Task GivenAnIncludeSearchExpressionWithMultipleDenormalizedParametersAndTableParameters_WhenSearched_ThenCorrectBundleShouldBeReturned()
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
                Fixture.Practitioner,
                Fixture.Organization);

            ValidateSearchEntryMode(bundle, ResourceType.Observation);
        }

        // Fails due to issue https://github.com/microsoft/fhir-server/issues/1236
        /*
        [Fact]
        public async Task GivenAnIncludeSearchExpressionWithIncludeResourceTypeNotAsEntryMode_WhenSearched_ThenCorrectBundleShouldBeReturnedAndIncludeIsIgnored()
        {
            string query = $"_include=MedicationDispense:patient&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationRequest, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.TrumanMedicationRequest);
        }*/

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

        /*
         * Commented out due to bug: https://github.com/microsoft/fhir-server/issues/1243
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
        }*/

        [Fact]
        public async Task GivenARevIncludeSearchExpressionWithMultipleDenormalizedParametersAndTableParameters_WhenSearched_ThenCorrectBundleShouldBeReturned()
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
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.AdamsPatient);

            ValidateSearchEntryMode(bundle, ResourceType.Patient);
        }

        // Include Iterate

        [Fact]
        public async Task GivenANonRecursiveIncludeIterateSearchExpressionWithSingleIteration_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration- Single iteration (_include:iterate)
            string query = $"_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:patient&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispense,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.TrumanMedicationRequest,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);
        }

        [Fact]
        public async Task GivenANonRecursiveIncludeRecurseSearchExpressionWithSingleIteration_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration- Single iteration (_include:recurse)
            string query = $"_include=MedicationDispense:prescription&_include:recurse=MedicationRequest:patient&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispense,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.TrumanMedicationRequest,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);
        }

        [Fact]
        public async Task GivenANonRecursiveIncludeIterateSearchExpressionWithAdditionalParameters_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Single iteration (_include:iterate)
            // _tag parameter is not used due to issue https://github.com/microsoft/fhir-server/issues/1235
            string query = $"_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:patient&_id={Fixture.AdamsMedicationDispense.Id}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.AdamsMedicationRequest,
                Fixture.AdamsPatient);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);
        }

        /*[Fact]
        public async Task GivenANonRecursiveIncludeIterateSearchExpressionWithOrphanIncludeeIterate_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            string query = $"_include=MedicationDispense:prescription&_include:iterate=Patient:general-practitioner&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispense,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.TrumanMedicationRequest);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);
        }*/

        [Fact]
        public async Task GivenANonRecursiveIncludeIterateSearchExpressionWithMultipleIterations_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Multiple iterations
            string query = $"_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:patient&_include:iterate=Patient:general-practitioner&_include:iterate=Patient:organization&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispense,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.TrumanMedicationRequest,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner,
                Fixture.TaylorPractitioner,
                Fixture.Organization);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);
        }

        [Fact]
        public async Task GivenANonRecursiveIncludeIterateSearchExpressionWithIncludeIterateParametersBeforeIncludeParameters_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Iteration order doesn't matter
            string query = $"_include:iterate=Patient:organization&_include:iterate=Patient:general-practitioner&_include:iterate=MedicationRequest:patient&_include=MedicationDispense:prescription&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispense,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.TrumanMedicationRequest,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner,
                Fixture.TaylorPractitioner,
                Fixture.Organization);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);
        }

        [Fact]
        public async Task GivenANonRecursiveIncludeIterateSearchExpressionWithMultitypeReference_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Single reference to multiple target types: MedicationRequest:subject could be Patient or Group
            string query = $"_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:subject&_include:iterate=Patient:general-practitioner&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispense,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.TrumanMedicationRequest,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner,
                Fixture.TaylorPractitioner);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);
        }

        [Fact]
        public async Task GivenANonRecursiveIncludeIterateSearchExpressionWithMultitypeArrayReference_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
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
        public async Task GivenANonRecursiveIncludeIterateSearchExpressionWithSpecificTargetType_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
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
        public async Task GivenANonRecursiveIncludeIterateSearchExpressionWithMultitypeTargetReferenceWithOverlap_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Multi-type target reference type already included: MedicationDispense:patient and MedicationRequest:subject
            string query = $"_include=MedicationDispense:patient&_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:subject&_include:iterate=Patient:general-practitioner&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispense,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.TrumanMedicationRequest,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner,
                Fixture.TaylorPractitioner);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);
        }

        [Fact]
        public async Task GivenANonRecursiveIncludeIterateSearchExpressionWithMultipleResultsSets_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Multiple result sets: Patient:general-practitioner and MedicationDispense:performer
            string query = $"_include=MedicationDispense:performer&_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:patient&_include:iterate=Patient:general-practitioner&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispense,
                Fixture.Practitioner,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.TrumanMedicationRequest,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner,
                Fixture.TaylorPractitioner);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);
        }

        // TEMP
        [Fact]
        public async Task GivenANonRecursiveIncludeMedication_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // If R5 MedicationDispense:medication is a codable reference. Otherwise, It's a codeable concept.
            // string query = $"_include=MedicationDispense:*&_include:iterate=Patient:general-practitioner&_tag={Fixture.Tag}";
            string query = $"_include=MedicationDispense:medication&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
#if R5
                Fixture.TramadolMedication,
#endif
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispense);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);
        }

        [Fact]
        public async Task GivenANonRecursiveIncludeIterateSearchExpressionWithIncludeWildCard_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            string query = $"_include=MedicationDispense:*&_include:iterate=Patient:general-practitioner&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
#if R5
                Fixture.TramadolMedication,
#endif
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispense,
                Fixture.Practitioner,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.TrumanMedicationRequest,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner,
                Fixture.TaylorPractitioner);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);
        }

        [Fact]
        public async Task GivenANonRecursiveIncludeIterateSearchExpressionWithIncludeIterateWildCard_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            string query = $"_include=MedicationDispense:patient&_include:iterate=Patient:*&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispense,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner,
                Fixture.TaylorPractitioner,
                Fixture.Organization);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);
        }

        [Fact]
        public async Task GivenANonRecursiveIncludeIterateSearchExpressionWithWildCardWithIncludeirdAndIncludeIterateWildcard_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // string query = $"_include=MedicationDispense:*&_include:iterate=Patient:general-practitioner&_tag={Fixture.Tag}";
            string query = $"_include=MedicationDispense:*&_include:iterate=MedicationRequest:*&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
#if R5
                Fixture.TramadolMedication,
                Fixture.PercocetMedication,
#endif
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispense,
                Fixture.Practitioner,
                Fixture.AdamsMedicationRequest,
                Fixture.SmithMedicationRequest,
                Fixture.TrumanMedicationRequest,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);
        }

        /*[Fact]
        public async Task GivenARecursiveIncludeIterateSearchExpressionWithRecursionLevelEqualToMaxRecursionDepth_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Recursive iteration - Circular reference Organization:partof
            // TODO: Test fails when  &_tag={Fixture.Tag} is used
            // string query = $"_include:iterate=Organization:partof&_id={Fixture.LabAOrganization.Id}&_tag={Fixture.Tag}";
            string query = $"_include:iterate=Organization:partof&_id={Fixture.LabAOrganization.Id}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Organization, query);

            ValidateBundle(
                bundle,
                Fixture.LabAOrganization,
                Fixture.LabBOrganization,
                Fixture.LabCOrganization,
                Fixture.LabDOrganization,
                Fixture.LabEOrganization,
                Fixture.LabFOrganization);

            var expectedSearchEntryModes = new Dictionary<string, Bundle.SearchEntryMode>
            {
                { Fixture.LabAOrganization.Id, Bundle.SearchEntryMode.Match },
                { Fixture.LabBOrganization.Id, Bundle.SearchEntryMode.Include },
                { Fixture.LabCOrganization.Id, Bundle.SearchEntryMode.Include },
                { Fixture.LabDOrganization.Id, Bundle.SearchEntryMode.Include },
                { Fixture.LabEOrganization.Id, Bundle.SearchEntryMode.Include },
                { Fixture.LabFOrganization.Id, Bundle.SearchEntryMode.Include },
            };

            ValidateSearchEntryMode(bundle, expectedSearchEntryModes);
        }

        [Fact]
        public async Task GivenARecursiveIncludeIterateSearchExpressionWithRecursionLevelSmallerThanMaxRecursionDepth_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Recursive iteration - Circular reference Organization:partof
            // TODO: Test fails when  &_tag={Fixture.Tag} is used
            // string query = $"_include:iterate=Organization:partof&_id={Fixture.LabBOrganization.Id}&_tag={Fixture.Tag}";
            string query = $"_include:iterate=Organization:partof&_id={Fixture.LabBOrganization.Id}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Organization, query);

            ValidateBundle(
                bundle,
                Fixture.LabBOrganization,
                Fixture.LabCOrganization,
                Fixture.LabDOrganization,
                Fixture.LabEOrganization,
                Fixture.LabFOrganization);

            var expectedSearchEntryModes = new Dictionary<string, Bundle.SearchEntryMode>
            {
                { Fixture.LabBOrganization.Id, Bundle.SearchEntryMode.Match },
                { Fixture.LabCOrganization.Id, Bundle.SearchEntryMode.Include },
                { Fixture.LabDOrganization.Id, Bundle.SearchEntryMode.Include },
                { Fixture.LabEOrganization.Id, Bundle.SearchEntryMode.Include },
                { Fixture.LabFOrganization.Id, Bundle.SearchEntryMode.Include },
            };

            ValidateSearchEntryMode(bundle, expectedSearchEntryModes);
        }

        [Fact]
        public async Task GivenARecursiveIncludeIterateSearchExpressionWithMultitypeArrayReference_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
        {
            // Non-recursive iteration - Reference array of multiple target types: CareTeam:participant of type Patient, CareTeam, Practitioner, Organization, etc.
            string query = $"_include=CareTeam:participant&_include:iterate=Patient:general-practitioner&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.CareTeam, query);

            ValidateBundle(
                bundle,
                Fixture.CareTeam,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.Organization,
                Fixture.Practitioner,
                Fixture.AndersonPractitioner,
                Fixture.SanchezPractitioner,
                Fixture.TaylorPractitioner);

            ValidateSearchEntryMode(bundle, ResourceType.CareTeam);
       }

       Use case: Organization?_include:iterate=Organization:*
        */

        /*[Fact]
        public async Task GivenAnIncludeIterateSearchExpressionWithOrphanRecursiveIncludeIterate_WhenSearched_TheIterativeExpressionIsIgnored()
        {
            // Include Iterate without relevant result set
            string query = $"_include=MedicationDispense:patient&_include:iterate=Organization:partof&_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.MedicationDispense, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsMedicationDispense,
                Fixture.SmithMedicationDispense,
                Fixture.TrumanMedicationDispense,
                Fixture.AdamsPatient,
                Fixture.SmithPatient,
                Fixture.TrumanPatient);

            ValidateSearchEntryMode(bundle, ResourceType.MedicationDispense);
        }*/

        // This will not work for recursive queries (circular reference)
        private static void ValidateSearchEntryMode(Bundle bundle, ResourceType matchResourceType)
        {
            foreach (Bundle.EntryComponent entry in bundle.Entry)
            {
                var searchEntryMode = entry.Resource.ResourceType == matchResourceType ? Bundle.SearchEntryMode.Match : Bundle.SearchEntryMode.Include;
                Assert.Equal(searchEntryMode, entry.Search.Mode);
            }
        }

        private static void ValidateSearchEntryMode(Bundle bundle, IDictionary<string, Bundle.SearchEntryMode> expectedSearchEntryModes)
        {
            foreach (Bundle.EntryComponent entry in bundle.Entry)
            {
                Assert.Equal(expectedSearchEntryModes[entry.Resource.Id], entry.Search.Mode);
            }
        }
    }
}
