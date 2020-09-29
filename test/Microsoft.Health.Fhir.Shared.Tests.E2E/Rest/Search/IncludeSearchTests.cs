// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Web;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
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
        public async Task GivenAnIncludeSearchExpressionWithMissingModifier_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_include=DiagnosticReport:patient:Patient&code=429858000&organization:missing=true";

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
                Fixture.Organization);

            ValidateSearchEntryMode(bundle, ResourceType.Organization);

            // ensure that the included resources are not counted
            bundle = await Client.SearchAsync(ResourceType.Organization, $"{query}&_summary=count");
            Assert.Equal(1, bundle.Total);

            // ensure that the included resources are not counted when _total is specified and the results fit in a single bundle.
            bundle = await Client.SearchAsync(ResourceType.Organization, $"{query}&_total=accurate");
            Assert.Equal(1, bundle.Total);
        }

        [Fact]
        public async Task GivenARevIncludeSearchExpression_WhenSearchedWithPost_ThenCorrectBundleShouldBeReturned()
        {
            Bundle bundle = await Client.SearchPostAsync(ResourceType.Organization.ToString(), default, ("_revinclude", "Location:organization"), ("_tag", Fixture.Tag));

            ValidateBundle(
                bundle,
                Fixture.Location,
                Fixture.Organization);

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
        }

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

        private static void ValidateSearchEntryMode(Bundle bundle, ResourceType matchResourceType)
        {
            foreach (Bundle.EntryComponent entry in bundle.Entry)
            {
                var searchEntryMode = entry.Resource.ResourceType == matchResourceType ? Bundle.SearchEntryMode.Match : Bundle.SearchEntryMode.Include;
                Assert.Equal(searchEntryMode, entry.Search.Mode);
            }
        }
    }
}
