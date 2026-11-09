// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Web;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.Extensions;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    /// <summary>
    /// Referenced resource ids longer than the maximum resource id length are indexed truncated to that length.
    /// These tests pin the documented behavior for every kind of search: such references are accepted, searches
    /// with such ids return a warning and still run, and a truncated reference matches the valid id it starts with.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class ReferenceIdLengthSearchTests : SearchTestsBase<ReferenceIdLengthSearchTestFixture>
    {
        public ReferenceIdLengthSearchTests(ReferenceIdLengthSearchTestFixture fixture)
            : base(fixture)
        {
        }

        private string Overlong => $"Practitioner/{Fixture.OverlongPractitionerId}";

        private string Valid => $"Practitioner/{Fixture.PractitionerId}";

        private string Unrelated => $"Practitioner/{Fixture.UnrelatedPractitionerId}";

        [Fact]
        public async Task GivenAnOverlongReferenceId_WhenSearched_ThenResourcesReferencingTheTruncatedIdAreReturnedWithAWarning()
        {
            Bundle bundle = await SearchPatientsAsync($"general-practitioner={Encode(Overlong)}");

            AssertResources(bundle, Fixture.AllowPatient, Fixture.PrivatePatient);
            AssertWarnings(bundle, "general-practitioner");
        }

        [Fact]
        public async Task GivenTheValidIdThatAnOverlongReferenceStartsWith_WhenSearched_ThenBothResourcesAreReturnedWithoutAWarning()
        {
            // The requested id is valid, so there is no warning. The collision is logged when the overlong reference is written.
            Bundle bundle = await SearchPatientsAsync($"general-practitioner={Encode(Valid)}");

            AssertResources(bundle, Fixture.AllowPatient, Fixture.PrivatePatient);
            AssertWarnings(bundle);
        }

        [Fact]
        public async Task GivenAnUnrelatedValidId_WhenSearched_ThenOnlyItsResourceIsReturnedWithoutAWarning()
        {
            Bundle bundle = await SearchPatientsAsync($"general-practitioner={Encode(Unrelated)}");

            AssertResources(bundle, Fixture.UnrelatedPatient);
            AssertWarnings(bundle);
        }

        [Fact]
        public async Task GivenAnOverlongReferenceIdAsAnAbsoluteUrlOfThisServer_WhenSearched_ThenItMatchesLikeARelativeReferenceWithAWarning()
        {
            string url = new Uri(Fixture.TestFhirServer.BaseAddress, Overlong).ToString();

            Bundle bundle = await SearchPatientsAsync($"general-practitioner={Encode(url)}");

            AssertResources(bundle, Fixture.AllowPatient, Fixture.PrivatePatient);
            AssertWarnings(bundle, "general-practitioner");
        }

        [Fact]
        public async Task GivenAnOverlongReferenceIdAsAnExternalUrl_WhenSearched_ThenNothingMatchesAndAWarningIsReturned()
        {
            Bundle bundle = await SearchPatientsAsync($"general-practitioner={Encode("https://external.example.org/fhir/" + Overlong)}");

            AssertResources(bundle);
            AssertWarnings(bundle, "general-practitioner");
        }

        [Fact]
        public async Task GivenAnOverlongBareId_WhenSearched_ThenAWarningIsReturned()
        {
            // A bare id isn't truncated in the search value, so it doesn't match the truncated index rows.
            Bundle bundle = await SearchPatientsAsync($"general-practitioner={Fixture.OverlongPractitionerId}");

            AssertResources(bundle);
            AssertWarnings(bundle, "general-practitioner");
        }

        [Fact]
        public async Task GivenAnOverlongBareIdWithATypeModifier_WhenSearched_ThenAWarningIsReturned()
        {
            Bundle bundle = await SearchPatientsAsync($"general-practitioner:Practitioner={Fixture.OverlongPractitionerId}");

            AssertResources(bundle);
            AssertWarnings(bundle, "general-practitioner");
        }

        [Fact]
        public async Task GivenTheValidBareId_WhenSearched_ThenBothResourcesAreReturnedWithoutAWarning()
        {
            Bundle bundle = await SearchPatientsAsync($"general-practitioner={Fixture.PractitionerId}");

            AssertResources(bundle, Fixture.AllowPatient, Fixture.PrivatePatient);
            AssertWarnings(bundle);
        }

        [Fact]
        public async Task GivenAnOrListWithAnOverlongReferenceId_WhenSearched_ThenAllMatchesAreReturnedWithOneWarning()
        {
            Bundle bundle = await SearchPatientsAsync($"general-practitioner={Encode(Overlong)},{Encode(Unrelated)}");

            AssertResources(bundle, Fixture.AllowPatient, Fixture.PrivatePatient, Fixture.UnrelatedPatient);
            AssertWarnings(bundle, "general-practitioner");
        }

        [Fact]
        public async Task GivenOverlongReferenceIdsForTwoParameters_WhenSearched_ThenOneWarningIsReturnedPerParameter()
        {
            Bundle bundle = await SearchPatientsAsync(
                $"general-practitioner={Encode(Overlong)}&organization=Organization/{Fixture.OverlongPractitionerId}");

            AssertResources(bundle);
            AssertWarnings(bundle, "general-practitioner", "organization");
        }

        [Fact]
        public async Task GivenTheSameOverlongReferenceIdTwice_WhenSearched_ThenOneWarningIsReturned()
        {
            Bundle bundle = await SearchPatientsAsync($"general-practitioner={Encode(Overlong)}&general-practitioner={Encode(Overlong)}");

            AssertResources(bundle, Fixture.AllowPatient, Fixture.PrivatePatient);
            AssertWarnings(bundle, "general-practitioner");
        }

        [Fact]
        public async Task GivenAMissingModifier_WhenSearched_ThenNoWarningIsReturned()
        {
            Bundle bundle = await SearchPatientsAsync("general-practitioner:missing=false");

            AssertResources(bundle, Fixture.AllowPatient, Fixture.PrivatePatient, Fixture.UnrelatedPatient);
            AssertWarnings(bundle);
        }

        [Fact]
        public async Task GivenAChainedSearchWithAnOverlongReferenceId_WhenSearched_ThenTheWarningNamesTheChainedParameter()
        {
            Bundle bundle = await SearchObservationsAsync($"subject:Patient.general-practitioner={Encode(Overlong)}");

            AssertResources(bundle, Fixture.AllowObservation, Fixture.PrivateObservation);
            AssertWarnings(bundle, "general-practitioner");
        }

        [Fact]
        public async Task GivenAReverseChainedSearchWithAnOverlongReferenceId_WhenSearched_ThenTheWarningNamesTheReverseChainedParameter()
        {
            Bundle bundle = await SearchPatientsAsync($"_has:Observation:subject:performer={Encode(Overlong)}");

            AssertResources(bundle, Fixture.AllowPatient, Fixture.PrivatePatient);
            AssertWarnings(bundle, "performer");
        }

        [Fact]
        public async Task GivenAnIncludeWithAnOverlongReferenceId_WhenSearched_ThenMatchesAndIncludesAreReturnedWithAWarning()
        {
            Bundle bundle = await SearchObservationsAsync($"performer={Encode(Overlong)}&_include=Observation:subject");

            AssertResources(bundle, Fixture.AllowObservation, Fixture.PrivateObservation, Fixture.AllowPatient, Fixture.PrivatePatient);
            Assert.All(bundle.Entry.Where(e => e.Resource is Patient), e => Assert.Equal(Bundle.SearchEntryMode.Include, e.Search.Mode));
            AssertWarnings(bundle, "performer");
        }

        [Fact]
        public async Task GivenAPostSearchWithAnOverlongReferenceId_WhenSearched_ThenAWarningIsReturned()
        {
            Bundle bundle = await Client.SearchPostAsync(
                "Patient",
                null,
                CancellationToken.None,
                ("general-practitioner", Overlong),
                ("_tag", Fixture.FixtureTag));

            AssertResources(bundle, Fixture.AllowPatient, Fixture.PrivatePatient);
            AssertWarnings(bundle, "general-practitioner");
        }

        [Fact]
        public async Task GivenAPagedSearchWithAnOverlongReferenceId_WhenEachPageIsRead_ThenEachPageHasTheWarning()
        {
            Bundle first = await SearchPatientsAsync($"general-practitioner={Encode(Overlong)}&_count=1");
            Assert.NotNull(first.NextLink);
            Bundle second = await Client.SearchAsync(first.NextLink.ToString());

            AssertWarnings(first, "general-practitioner");
            AssertWarnings(second, "general-practitioner");
            Assert.Equal(
                new[] { Fixture.AllowPatient.Id, Fixture.PrivatePatient.Id }.OrderBy(id => id),
                first.Entry.Concat(second.Entry).Where(e => e.Resource is Patient).Select(e => e.Resource.Id).OrderBy(id => id));
        }

        [Fact]
        public async Task GivenACountOnlySearchWithAnOverlongReferenceId_WhenSearched_ThenTheTotalIsReturnedWithoutEntriesLikeOtherWarnings()
        {
            // _summary=count returns no entries, so no search warning is returned, the same as for existing warnings.
            Bundle bundle = await SearchPatientsAsync($"general-practitioner={Encode(Overlong)}&_summary=count");
            Bundle unsupported = await SearchPatientsAsync("unknown-parameter=x&_summary=count");

            Assert.Equal(2, bundle.Total);
            Assert.Empty(bundle.Entry);
            Assert.Empty(unsupported.Entry);
        }

        [Fact]
        public async Task GivenAResourceWithAnOverlongReference_WhenRead_ThenTheFullReferenceIsReturned()
        {
            using FhirResponse<Patient> read = await Client.ReadAsync<Patient>(ResourceType.Patient, Fixture.PrivatePatient.Id);

            Assert.Equal(Overlong, Assert.Single(read.Resource.GeneralPractitioner).Reference);
        }

        [Fact]
        public async Task GivenAResourceUpdatedWithAnOverlongReference_WhenUpdated_ThenItIsSavedAndFoundByTheTruncatedId()
        {
            string tag = Guid.NewGuid().ToString();
            var patient = new Patient();
            ReferenceIdLengthSearchTestFixture.SetPractitioner(patient, tag, Fixture.UnrelatedPractitionerId);
            using FhirResponse<Patient> created = await Client.CreateAsync(patient);

            created.Resource.GeneralPractitioner = new List<ResourceReference> { new ResourceReference(Overlong) };
            using FhirResponse<Patient> updated = await Client.UpdateAsync(created.Resource);

            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Equal(Overlong, Assert.Single(updated.Resource.GeneralPractitioner).Reference);
            AssertResources(await SearchPatientsAsync($"general-practitioner={Encode(Valid)}", tag), created.Resource);
            AssertResources(await SearchPatientsAsync($"general-practitioner={Encode(Unrelated)}", tag));
        }

        [Fact]
        public async Task GivenAConditionalUpdateWithAnOverlongReferenceId_WhenRepeated_ThenItCreatesAndThenUpdatesTheSameResource()
        {
            string tag = Guid.NewGuid().ToString();
            string criteria = $"general-practitioner={Encode(Overlong)}&_tag={tag}";
            var patient = new Patient();
            ReferenceIdLengthSearchTestFixture.SetPractitioner(patient, tag, Fixture.OverlongPractitionerId);

            using FhirResponse<Patient> created = await Client.ConditionalUpdateAsync(patient, criteria);
            using FhirResponse<Patient> updated = await Client.ConditionalUpdateAsync(patient, criteria);

            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Equal(created.Resource.Id, updated.Resource.Id);
        }

        [Fact]
        public async Task GivenAConditionalPatchWithAnOverlongReferenceId_WhenPatched_ThenTheMatchingResourceIsPatched()
        {
            string tag = Guid.NewGuid().ToString();
            var patient = new Patient();
            ReferenceIdLengthSearchTestFixture.SetPractitioner(patient, tag, Fixture.OverlongPractitionerId);
            using FhirResponse<Patient> created = await Client.CreateAsync(patient);

            using FhirResponse<Patient> patched = await Client.ConditionalJsonPatchAsync<Patient>(
                "Patient",
                $"general-practitioner={Encode(Overlong)}&_tag={tag}",
                "[{\"op\":\"add\",\"path\":\"/gender\",\"value\":\"female\"}]");

            Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
            Assert.Equal(created.Resource.Id, patched.Resource.Id);
            Assert.Equal(AdministrativeGender.Female, patched.Resource.Gender);
        }

        [Fact]
        public async Task GivenAConditionalDeleteWithAnOverlongReferenceId_WhenDeleted_ThenTheMatchingResourceIsDeleted()
        {
            string tag = Guid.NewGuid().ToString();
            var patient = new Patient();
            ReferenceIdLengthSearchTestFixture.SetPractitioner(patient, tag, Fixture.OverlongPractitionerId);
            using FhirResponse<Patient> created = await Client.CreateAsync(patient);

            using FhirResponse deleted = await Client.DeleteAsync($"Patient?general-practitioner={Encode(Overlong)}&_tag={tag}");

            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            AssertResources(await SearchPatientsAsync(null, tag));
        }

        [Fact]
        public async Task GivenABulkDeleteFilteredByAnOverlongReferenceId_WhenRequested_ThenTheJobIsAcceptedAndReturnsTheWarning()
        {
            Assert.SkipUnless(Fixture.TestFhirServer.Metadata.SupportsOperation("bulk-delete"), "$bulk-delete not enabled on this server");

            // The reference is accepted on write, so the warning must not be treated as an invalid search parameter (which returns 400).
            string tag = Guid.NewGuid().ToString();
            var patient = new Patient();
            ReferenceIdLengthSearchTestFixture.SetPractitioner(patient, tag, Fixture.OverlongPractitionerId);
            using FhirResponse<Patient> created = await Client.CreateAsync(patient);

            string path = QueryHelpers.AddQueryString(
                "Patient/$bulk-delete",
                new Dictionary<string, string> { { "general-practitioner", Overlong }, { "_tag", tag } });
            using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(Fixture.HttpClient.BaseAddress, path));
            request.Headers.Add(KnownHeaders.Prefer, "respond-async");

            using HttpResponseMessage response = await Fixture.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            using FhirResponse<Parameters> status = await Client.WaitForBulkJobStatus("Bulk delete", response.Content.Headers.ContentLocation);
            AssertJobWarning(status.Resource);
            AssertResources(await SearchPatientsAsync(null, tag));
        }

        [Fact]
        public async Task GivenABulkUpdateFilteredByAnOverlongReferenceId_WhenRequested_ThenTheJobIsAcceptedAndReturnsTheWarning()
        {
            Assert.SkipUnless(Fixture.TestFhirServer.Metadata.SupportsOperation("bulk-update"), "$bulk-update not enabled on this server");

            string tag = Guid.NewGuid().ToString();
            var patient = new Patient { Gender = AdministrativeGender.Male };
            ReferenceIdLengthSearchTestFixture.SetPractitioner(patient, tag, Fixture.OverlongPractitionerId);
            using FhirResponse<Patient> created = await Client.CreateAsync(patient);

            string path = QueryHelpers.AddQueryString(
                "Patient/$bulk-update",
                new Dictionary<string, string> { { "general-practitioner", Overlong }, { "_tag", tag } });
            Parameters patchRequest = new Parameters().AddReplacePatchParameter("Patient.gender", new Code("female"));

            using HttpResponseMessage response = await Client.BulkUpdateAsync(new Uri(Fixture.HttpClient.BaseAddress, path).ToString(), patchRequest, CancellationToken.None);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            using FhirResponse<Parameters> status = await Client.WaitForBulkJobStatus("Bulk update", response.Content.Headers.ContentLocation);
            AssertJobWarning(status.Resource);

            // The updated resource keeps its overlong reference, so the job also reports it as a write warning with a count.
            var issues = (OperationOutcome)status.Resource.Parameter.Single(p => p.Name == "Issues").Resource;
            Assert.True(
                issues.Issue.Any(i => i.Severity == OperationOutcome.IssueSeverity.Information && i.Diagnostics == Warning("general-practitioner") && i.Details?.Text == "Resource type 'Patient': 1 resource(s) affected."),
                string.Join(" | ", issues.Issue.Select(i => $"{i.Severity}:{i.Diagnostics}:{i.Details?.Text}")));
            using FhirResponse<Patient> read = await Client.ReadAsync<Patient>(ResourceType.Patient, created.Resource.Id);
            Assert.Equal(AdministrativeGender.Female, read.Resource.Gender);
        }

        [Fact]
        public async Task GivenACreateWithAnOverlongReferenceAndPreferOperationOutcome_WhenCreated_ThenTheOperationOutcomeHasTheWarning()
        {
            using HttpResponseMessage response = await SendAsync(HttpMethod.Post, "Patient", NewPatient(Fixture.OverlongPractitionerId), preferOperationOutcome: true);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.NotNull(response.Headers.Location);
            AssertOutcome(await ParseAsync(response), message: null, "general-practitioner");
        }

        [Fact]
        public async Task GivenACreateWithAnOverlongReferenceAndNoPreferHeader_WhenCreated_ThenTheResourceIsReturnedAsBefore()
        {
            using HttpResponseMessage response = await SendAsync(HttpMethod.Post, "Patient", NewPatient(Fixture.OverlongPractitionerId), preferOperationOutcome: false);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var patient = Assert.IsType<Patient>(await ParseAsync(response));
            Assert.Equal(Overlong, Assert.Single(patient.GeneralPractitioner).Reference);
        }

        [Fact]
        public async Task GivenACreateWithValidReferencesAndPreferOperationOutcome_WhenCreated_ThenTheResourceIsReturnedAsBefore()
        {
            using HttpResponseMessage response = await SendAsync(HttpMethod.Post, "Patient", NewPatient(Fixture.PractitionerId), preferOperationOutcome: true);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.IsType<Patient>(await ParseAsync(response));
        }

        [Fact]
        public async Task GivenAnUpdateWithAnOverlongReferenceAndPreferOperationOutcome_WhenUpdated_ThenTheOperationOutcomeHasTheWarning()
        {
            using FhirResponse<Patient> created = await Client.CreateAsync(NewPatient(Fixture.UnrelatedPractitionerId));
            Patient patient = created.Resource;
            patient.GeneralPractitioner = new List<ResourceReference> { new ResourceReference(Overlong) };

            using HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"Patient/{patient.Id}", patient, preferOperationOutcome: true);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(response.Headers.ETag);
            AssertOutcome(await ParseAsync(response), message: null, "general-practitioner");
        }

        [Fact]
        public async Task GivenAPatchAddingAnOverlongReferenceAndPreferOperationOutcome_WhenPatched_ThenTheOperationOutcomeHasTheWarning()
        {
            using FhirResponse<Patient> created = await Client.CreateAsync(NewPatient(practitionerId: null));
            string patch = $"[{{\"op\":\"add\",\"path\":\"/generalPractitioner\",\"value\":[{{\"reference\":\"{Overlong}\"}}]}}]";

            using HttpResponseMessage response = await SendAsync(new HttpMethod("PATCH"), $"Patient/{created.Resource.Id}", patch, "application/json-patch+json", preferOperationOutcome: true);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertOutcome(await ParseAsync(response), message: null, "general-practitioner");
        }

        [Fact]
        public async Task GivenAConditionalUpdateWithAnOverlongReferenceAndPreferOperationOutcome_WhenCreatedThenUpdated_ThenEachOperationOutcomeHasTheWarning()
        {
            string tag = Guid.NewGuid().ToString();
            Patient patient = NewPatient(Fixture.OverlongPractitionerId, tag);
            string path = $"Patient?general-practitioner={Encode(Overlong)}&_tag={tag}";

            using HttpResponseMessage created = await SendAsync(HttpMethod.Put, path, patient, preferOperationOutcome: true);
            using HttpResponseMessage updated = await SendAsync(HttpMethod.Put, path, patient, preferOperationOutcome: true);

            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            AssertOutcome(await ParseAsync(created), message: null, "general-practitioner");
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            AssertOutcome(await ParseAsync(updated), message: null, "general-practitioner");
        }

        [Fact]
        public async Task GivenAConditionalPatchWithAnOverlongReferenceIdAndPreferOperationOutcome_WhenPatched_ThenTheOperationOutcomeHasTheWarning()
        {
            string tag = Guid.NewGuid().ToString();
            await Client.CreateAsync(NewPatient(Fixture.OverlongPractitionerId, tag));

            using HttpResponseMessage response = await SendAsync(
                new HttpMethod("PATCH"),
                $"Patient?general-practitioner={Encode(Overlong)}&_tag={tag}",
                "[{\"op\":\"add\",\"path\":\"/gender\",\"value\":\"female\"}]",
                "application/json-patch+json",
                preferOperationOutcome: true);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertOutcome(await ParseAsync(response), message: null, "general-practitioner");
        }

        [Theory]
        [InlineData(true, HttpStatusCode.OK)]
        [InlineData(false, HttpStatusCode.NoContent)]
        public async Task GivenAConditionalDeleteWithAnOverlongReferenceId_WhenDeleted_ThenTheOperationOutcomeIsReturnedOnlyWhenPreferred(bool preferOperationOutcome, HttpStatusCode expectedStatusCode)
        {
            string tag = Guid.NewGuid().ToString();
            await Client.CreateAsync(NewPatient(Fixture.OverlongPractitionerId, tag));

            using HttpResponseMessage response = await SendAsync(HttpMethod.Delete, $"Patient?general-practitioner={Encode(Overlong)}&_tag={tag}", body: null, contentType: null, preferOperationOutcome);

            Assert.Equal(expectedStatusCode, response.StatusCode);
            if (preferOperationOutcome)
            {
                AssertOutcome(await ParseAsync(response), message: null, "general-practitioner");
            }
            else
            {
                Assert.Empty(await response.Content.ReadAsStringAsync());
            }

            AssertResources(await SearchPatientsAsync(null, tag));
        }

        [Fact]
        public async Task GivenAConditionalCreateWithValidCriteriaAndAnOverlongReferenceInTheBody_WhenCreated_ThenTheOperationOutcomeHasTheWriteWarning()
        {
            string tag = Guid.NewGuid().ToString();
            var headers = new Dictionary<string, string> { { KnownHeaders.Prefer, "return=OperationOutcome" } };

            using FhirResponse<Resource> response = await Client.CreateAsync<Resource>(
                resource: NewPatient(Fixture.OverlongPractitionerId, tag),
                conditionalCreateCriteria: $"_tag={tag}",
                additionalHeaders: headers);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            AssertOutcome(response.Resource, "A new resource was created.", "general-practitioner");
        }

        [Theory]
        [InlineData(Bundle.BundleType.Batch)]
        [InlineData(Bundle.BundleType.Transaction)]
        public async Task GivenABundleCreatingAResourceWithAnOverlongReferenceAndPreferOperationOutcome_WhenPosted_ThenTheEntryOutcomeHasTheWarning(Bundle.BundleType bundleType)
        {
            Bundle result = await PostBundleAsync(
                bundleType,
                preferOperationOutcome: true,
                new Bundle.EntryComponent { Resource = NewPatient(Fixture.OverlongPractitionerId), Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.POST, Url = "Patient" } },
                new Bundle.EntryComponent { Resource = NewPatient(Fixture.PractitionerId), Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.POST, Url = "Patient" } });

            Assert.StartsWith("201", result.Entry[0].Response.Status, StringComparison.Ordinal);
            AssertOutcome(result.Entry[0].Response.Outcome, message: null, "general-practitioner");
            Assert.StartsWith("201", result.Entry[1].Response.Status, StringComparison.Ordinal);
            Assert.IsType<Patient>(result.Entry[1].Resource);
            Assert.Null(result.Entry[1].Response.Outcome);
        }

        [Theory]
        [InlineData(Bundle.BundleType.Batch)]
        [InlineData(Bundle.BundleType.Transaction)]
        public async Task GivenABundleCreatingAResourceWithAnOverlongReferenceAndNoPreferHeader_WhenPosted_ThenEntriesAreUnchanged(Bundle.BundleType bundleType)
        {
            Bundle result = await PostBundleAsync(
                bundleType,
                preferOperationOutcome: false,
                new Bundle.EntryComponent { Resource = NewPatient(Fixture.OverlongPractitionerId), Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.POST, Url = "Patient" } });

            Bundle.EntryComponent entry = Assert.Single(result.Entry);
            Assert.StartsWith("201", entry.Response.Status, StringComparison.Ordinal);
            Assert.IsType<Patient>(entry.Resource);
            Assert.Null(entry.Response.Outcome);
        }

        [Fact]
        public async Task GivenATransactionWithAConditionalCreateAndPreferOperationOutcome_WhenPosted_ThenTheTransactionSucceeds()
        {
            // Before this change, any entry outcome failed the transaction, even a successful one returned for Prefer: return=OperationOutcome.
            string tag = Guid.NewGuid().ToString();
            Bundle result = await PostBundleAsync(
                Bundle.BundleType.Transaction,
                preferOperationOutcome: true,
                new Bundle.EntryComponent { Resource = NewPatient(Fixture.PractitionerId, tag), Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.POST, Url = "Patient", IfNoneExist = $"_tag={tag}" } });

            Bundle.EntryComponent entry = Assert.Single(result.Entry);
            Assert.StartsWith("201", entry.Response.Status, StringComparison.Ordinal);
            var outcome = Assert.IsType<OperationOutcome>(entry.Response.Outcome);
            Assert.All(outcome.Issue, i => Assert.Equal(OperationOutcome.IssueSeverity.Information, i.Severity));
        }

        [Fact]
        public async Task GivenATransactionWithAFailingEntryAndPreferOperationOutcome_WhenPosted_ThenTheTransactionStillFails()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Fixture.HttpClient.BaseAddress)
            {
                Content = new StringContent(
                    new Bundle
                    {
                        Type = Bundle.BundleType.Transaction,
                        Entry = new List<Bundle.EntryComponent>
                        {
                            new() { Resource = NewPatient(Fixture.OverlongPractitionerId), Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.POST, Url = "Patient" } },
                            new() { Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.GET, Url = $"Patient/{Guid.NewGuid()}" } },
                        },
                    }.ToJson(),
                    System.Text.Encoding.UTF8,
                    "application/fhir+json"),
            };
            request.Headers.Add(KnownHeaders.Prefer, "return=OperationOutcome");

            using HttpResponseMessage response = await Fixture.HttpClient.SendAsync(request);

            Assert.False(response.IsSuccessStatusCode);
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("POST")]
        public async Task GivenAResourceWithAnOverlongReference_WhenReindexedSingly_ThenTheResponseHasAnIssuesParameterWithTheWarning(string method)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), $"Patient/{Fixture.PrivatePatient.Id}/$reindex");
            using HttpResponseMessage response = await Fixture.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var parameters = Assert.IsType<Parameters>(await ParseAsync(response));
            var outcome = Assert.IsType<OperationOutcome>(Assert.Single(parameters.Parameter, p => p.Name == "Issues").Resource);
            OperationOutcome.IssueComponent issue = Assert.Single(outcome.Issue);
            Assert.Equal(OperationOutcome.IssueSeverity.Warning, issue.Severity);
            Assert.Equal(Warning("general-practitioner"), issue.Diagnostics);
        }

        [Fact]
        public async Task GivenAResourceWithoutAnOverlongReference_WhenReindexedSingly_ThenTheResponseHasNoIssuesParameter()
        {
            using HttpResponseMessage response = await Fixture.HttpClient.GetAsync($"Patient/{Fixture.AllowPatient.Id}/$reindex");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain(Assert.IsType<Parameters>(await ParseAsync(response)).Parameter, p => p.Name == "Issues");
        }

        [Fact]
        public async Task GivenAConditionalCreateWithAnOverlongReferenceIdAndPreferOperationOutcome_WhenCreated_ThenTheOperationOutcomeContainsTheWarning()
        {
            string tag = Guid.NewGuid().ToString();
            var patient = new Patient();
            ReferenceIdLengthSearchTestFixture.SetPractitioner(patient, tag, practitionerId: null);
            var headers = new Dictionary<string, string> { { KnownHeaders.Prefer, "return=OperationOutcome" } };

            using FhirResponse<Resource> response = await Client.CreateAsync<Resource>(
                resource: patient,
                conditionalCreateCriteria: $"general-practitioner={Encode(Overlong)}&_tag={tag}",
                additionalHeaders: headers);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var outcome = Assert.IsType<OperationOutcome>(response.Resource);
            Assert.Contains(outcome.Issue, i => i.Severity == OperationOutcome.IssueSeverity.Information);
            Assert.Contains(outcome.Issue, i => i.Severity == OperationOutcome.IssueSeverity.Warning && i.Diagnostics == Warning("general-practitioner"));
        }

        [Fact]
        public async Task GivenAConditionalCreateWithAnOverlongReferenceIdAndNoPreferHeader_WhenCreated_ThenTheResourceIsReturned()
        {
            string tag = Guid.NewGuid().ToString();
            var patient = new Patient();
            ReferenceIdLengthSearchTestFixture.SetPractitioner(patient, tag, practitionerId: null);

            using FhirResponse<Patient> response = await Client.CreateAsync(patient, conditionalCreateCriteria: $"general-practitioner={Encode(Overlong)}&_tag={tag}");

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.IsType<Patient>(response.Resource);
        }

        [Fact]
        public async Task GivenABatchWithAnOverlongReferenceIdSearchAndConditionalCreate_WhenPosted_ThenEachEntryCarriesTheWarning()
        {
            string tag = Guid.NewGuid().ToString();
            string reference = Encode(Overlong);
            var patient = new Patient();
            ReferenceIdLengthSearchTestFixture.SetPractitioner(patient, tag, practitionerId: null);
            var batch = new Bundle
            {
                Type = Bundle.BundleType.Batch,
                Entry = new List<Bundle.EntryComponent>
                {
                    new()
                    {
                        Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.GET, Url = $"Patient?general-practitioner={reference}&_tag={Fixture.FixtureTag}" },
                    },
                    new()
                    {
                        Resource = patient,
                        Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.POST, Url = "Patient", IfNoneExist = $"general-practitioner={reference}&_tag={tag}" },
                    },
                    new()
                    {
                        Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.GET, Url = $"Patient?general-practitioner={Encode(Unrelated)}&_tag={Fixture.FixtureTag}" },
                    },
                },
            };

            // The Prefer header is passed on to each entry, so the conditional create returns an OperationOutcome in response.outcome.
            using var request = new HttpRequestMessage(HttpMethod.Post, Fixture.HttpClient.BaseAddress)
            {
                Content = new StringContent(batch.ToJson(), System.Text.Encoding.UTF8, "application/fhir+json"),
            };
            request.Headers.Add(KnownHeaders.Prefer, "return=OperationOutcome");

            using HttpResponseMessage response = await Fixture.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Bundle result = new FhirJsonParser().Parse<Bundle>(await response.Content.ReadAsStringAsync());

            var searchResult = Assert.IsType<Bundle>(result.Entry[0].Resource);
            AssertResources(searchResult, Fixture.AllowPatient, Fixture.PrivatePatient);
            AssertWarnings(searchResult, "general-practitioner");

            Assert.StartsWith("201", result.Entry[1].Response.Status, StringComparison.Ordinal);
            var createOutcome = Assert.IsType<OperationOutcome>(result.Entry[1].Response.Outcome);
            Assert.Contains(createOutcome.Issue, i => i.Severity == OperationOutcome.IssueSeverity.Warning && i.Diagnostics == Warning("general-practitioner"));

            // Warnings are per entry, so the following valid search has none.
            var unrelatedResult = Assert.IsType<Bundle>(result.Entry[2].Resource);
            AssertResources(unrelatedResult, Fixture.UnrelatedPatient);
            AssertWarnings(unrelatedResult);
        }

        [Fact]
        public async Task GivenASmartScopeWithTheValidIdThatAnOverlongReferenceStartsWith_WhenSearchedAndRead_ThenTheCollidingResourceIsAccessibleWithoutAWarning()
        {
            // Pins the documented limitation: the scope value is valid, so there is no warning.
            SkipUnlessSmartIsAvailable();
            using HttpClient client = await CreateSmartClientAsync($"system/Observation.rs?performer={Valid}");

            Bundle bundle = await SmartSearchAsync(client, $"Observation?_tag={Fixture.FixtureTag}");
            AssertResources(bundle, Fixture.AllowObservation, Fixture.PrivateObservation);
            AssertWarnings(bundle);

            using HttpResponseMessage privateRead = await client.GetAsync($"Observation/{Fixture.PrivateObservation.Id}");
            using HttpResponseMessage unrelatedRead = await client.GetAsync($"Observation/{Fixture.UnrelatedObservation.Id}");
            Assert.Equal(HttpStatusCode.OK, privateRead.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, unrelatedRead.StatusCode);
        }

        [Fact]
        public async Task GivenASmartScopeWithAnOverlongReferenceId_WhenSearched_ThenTheWarningIsReturned()
        {
            SkipUnlessSmartIsAvailable();
            using HttpClient client = await CreateSmartClientAsync($"system/Observation.rs?performer={Overlong}");

            Bundle bundle = await SmartSearchAsync(client, $"Observation?_tag={Fixture.FixtureTag}");

            AssertResources(bundle, Fixture.AllowObservation, Fixture.PrivateObservation);
            AssertWarnings(bundle, "performer");
        }

        [Fact]
        public async Task GivenASmartUserSearchingWithAnOverlongReferenceId_WhenSearched_ThenTheWarningIsReturned()
        {
            SkipUnlessSmartIsAvailable();
            using HttpClient client = await CreateSmartClientAsync("system/Patient.rs");

            Bundle bundle = await SmartSearchAsync(client, $"Patient?general-practitioner={Encode(Overlong)}&_tag={Fixture.FixtureTag}");

            AssertResources(bundle, Fixture.AllowPatient, Fixture.PrivatePatient);
            AssertWarnings(bundle, "general-practitioner");
        }

        private static string Encode(string value) => HttpUtility.UrlEncode(value);

        private static string Warning(string searchParameterCode) => string.Format(
            CultureInfo.InvariantCulture,
            "Reference parameter '{0}' ID exceeds {1} characters. Index truncation applied after {1} characters, which may yield over-inclusive results",
            searchParameterCode,
            ReferenceIdLengthSearchTestFixture.MaxResourceIdLength);

        private static void AssertResources(Bundle bundle, params Resource[] expected)
        {
            string[] actual = bundle.Entry
                .Where(e => e.Resource is not OperationOutcome)
                .Select(e => $"{e.Resource.TypeName}/{e.Resource.Id}")
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expected.Select(r => $"{r.TypeName}/{r.Id}").OrderBy(id => id, StringComparer.Ordinal).ToArray(), actual);
        }

        private static void AssertWarnings(Bundle bundle, params string[] searchParameterCodes)
        {
            string[] actual = bundle.Entry
                .Select(e => e.Resource)
                .OfType<OperationOutcome>()
                .SelectMany(o => o.Issue)
                .Select(i =>
                {
                    Assert.Equal(OperationOutcome.IssueSeverity.Warning, i.Severity);
                    Assert.Equal(OperationOutcome.IssueType.Informational, i.Code);
                    return i.Diagnostics;
                })
                .OrderBy(d => d, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(searchParameterCodes.Select(Warning).OrderBy(d => d, StringComparer.Ordinal).ToArray(), actual);
        }

        private static void AssertJobWarning(Parameters status)
        {
            var issues = (OperationOutcome)status.Parameter.Single(p => p.Name == "Issues").Resource;
            Assert.Contains(issues.Issue, i => i.Severity == OperationOutcome.IssueSeverity.Information && i.Code == OperationOutcome.IssueType.Informational && i.Diagnostics == Warning("general-practitioner"));
        }

        private static void AssertOutcome(Resource resource, string message, params string[] searchParameterCodes)
        {
            var outcome = Assert.IsType<OperationOutcome>(resource);
            List<OperationOutcome.IssueComponent> warnings = outcome.Issue;
            if (message != null)
            {
                OperationOutcome.IssueComponent first = outcome.Issue.First();
                Assert.Equal(OperationOutcome.IssueSeverity.Information, first.Severity);
                Assert.Equal(message, first.Diagnostics);
                warnings = outcome.Issue.Skip(1).ToList();
            }

            Assert.All(warnings, w =>
            {
                Assert.Equal(OperationOutcome.IssueSeverity.Warning, w.Severity);
                Assert.Equal(OperationOutcome.IssueType.Informational, w.Code);
            });
            Assert.Equal(
                searchParameterCodes.Select(Warning).OrderBy(d => d, StringComparer.Ordinal).ToArray(),
                warnings.Select(w => w.Diagnostics).OrderBy(d => d, StringComparer.Ordinal).ToArray());
        }

        private static async System.Threading.Tasks.Task<Resource> ParseAsync(HttpResponseMessage response)
        {
            return new FhirJsonParser().Parse<Resource>(await response.Content.ReadAsStringAsync());
        }

        private static async System.Threading.Tasks.Task<Bundle> SmartSearchAsync(HttpClient client, string path)
        {
            using HttpResponseMessage response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return new FhirJsonParser().Parse<Bundle>(await response.Content.ReadAsStringAsync());
        }

        private async System.Threading.Tasks.Task<Bundle> SearchPatientsAsync(string query, string tag = null)
        {
            string tagQuery = $"_tag={tag ?? Fixture.FixtureTag}";
            return await Client.SearchAsync(ResourceType.Patient, query == null ? tagQuery : $"{query}&{tagQuery}");
        }

        private async System.Threading.Tasks.Task<Bundle> SearchObservationsAsync(string query)
        {
            return await Client.SearchAsync(ResourceType.Observation, $"{query}&_tag={Fixture.FixtureTag}");
        }

        private Patient NewPatient(string practitionerId, string tag = null)
        {
            var patient = new Patient();
            ReferenceIdLengthSearchTestFixture.SetPractitioner(patient, tag ?? Guid.NewGuid().ToString(), practitionerId);
            return patient;
        }

        private System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, Resource resource, bool preferOperationOutcome)
        {
            return SendAsync(method, path, resource.ToJson(), "application/fhir+json", preferOperationOutcome);
        }

        private async System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string body, string contentType, bool preferOperationOutcome)
        {
            using var request = new HttpRequestMessage(method, new Uri(Fixture.HttpClient.BaseAddress, path));
            if (body != null)
            {
                request.Content = new StringContent(body, System.Text.Encoding.UTF8);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            }

            if (preferOperationOutcome)
            {
                request.Headers.Add(KnownHeaders.Prefer, "return=OperationOutcome");
            }

            return await Fixture.HttpClient.SendAsync(request);
        }

        private async System.Threading.Tasks.Task<Bundle> PostBundleAsync(Bundle.BundleType bundleType, bool preferOperationOutcome, params Bundle.EntryComponent[] entries)
        {
            var bundle = new Bundle { Type = bundleType, Entry = entries.ToList() };
            using HttpResponseMessage response = await SendAsync(HttpMethod.Post, string.Empty, bundle, preferOperationOutcome);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return Assert.IsType<Bundle>(await ParseAsync(response));
        }

        private void SkipUnlessSmartIsAvailable()
        {
            Assert.SkipWhen(!Fixture.IsUsingInProcTestServer, "Requires in-proc development identity provider to issue SMART scopes.");
            Assert.SkipWhen(
                ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
                "SMART v2 scopes with search parameters are only covered for R4 and R4B.");
        }

        private async System.Threading.Tasks.Task<HttpClient> CreateSmartClientAsync(string scope)
        {
            TestApplication application = TestApplications.SmartUserClient;
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", application.GrantType },
                { "client_id", application.ClientId },
                { "client_secret", application.ClientSecret },
                { "scope", scope },
                { "resource", AuthenticationSettings.Resource },
            });
            using var authClient = new HttpClient(Fixture.TestFhirServer.CreateMessageHandler());
            using HttpResponseMessage tokenResponse = await authClient.PostAsync(Fixture.TestFhirServer.TokenUri, content);
            tokenResponse.EnsureSuccessStatusCode();
            using JsonDocument token = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());

            var client = new HttpClient(Fixture.TestFhirServer.CreateMessageHandler())
            {
                BaseAddress = Fixture.TestFhirServer.BaseAddress,
            };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.RootElement.GetProperty("access_token").GetString());
            return client;
        }
    }
}
