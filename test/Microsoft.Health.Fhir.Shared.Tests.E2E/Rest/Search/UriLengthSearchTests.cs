// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.Extensions;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    /// <summary>
    /// SQL Server indexes only the first 256 characters of URI search values. These tests pin the documented
    /// behavior: such values are accepted, searches on them return a warning, and values that share the
    /// indexed prefix match each other.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class UriLengthSearchTests : SearchTestsBase<UriLengthSearchTestFixture>
    {
        private const string LongUriWarning = "URI parameter '_profile' exceeds 256 characters. Evaluation is limited to the first 256 characters, which may return broader search results.";

        private const string CollidingUriError = "The resource has more than one value for the URI search parameter '_profile' that are identical in their first 256 characters. URI search values are compared using only their first 256 characters, so values of the same search parameter must differ within the first 256 characters.";

        public UriLengthSearchTests(UriLengthSearchTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        public async Task GivenAUriSearchValueLongerThanTheIndexedLength_WhenSearched_ThenResourcesSharingThePrefixAreReturnedWithAWarning()
        {
            Bundle bundle = await SearchByProfileAsync(Fixture.AllowProfile);

            AssertPatients(bundle, Fixture.AllowPatient, Fixture.PrivatePatient);
            AssertLongUriWarning(bundle);
        }

        [Fact]
        public async Task GivenAUriSearchValueOfExactlyTheIndexedLength_WhenSearched_ThenResourcesSharingThePrefixAreReturnedWithAWarning()
        {
            Assert.Equal(UriLengthSearchTestFixture.IndexedLength, Fixture.SharedPrefix.Length);

            Bundle bundle = await SearchByProfileAsync(Fixture.SharedPrefix);

            AssertPatients(bundle, Fixture.AllowPatient, Fixture.PrivatePatient);
            AssertLongUriWarning(bundle);
        }

        [Fact]
        public async Task GivenAUriSearchValueShorterThanTheIndexedLength_WhenSearched_ThenNoWarningIsReturned()
        {
            Bundle bundle = await SearchByProfileAsync(Fixture.UnrelatedProfile);

            AssertPatients(bundle, Fixture.UnrelatedPatient);
            Assert.DoesNotContain(bundle.Entry, e => e.Resource is OperationOutcome);
        }

        [Fact]
        public async Task GivenAResourceWithTwoUrisThatShareTheIndexedPrefix_WhenCreated_ThenBadRequestIsReturned()
        {
            // A separate tag keeps this resource out of the other tests, which use URIs with the same indexed prefix.
            string tag = Guid.NewGuid().ToString();
            var patient = new Patient();
            Fixture.SetTagAndProfiles(patient, tag, Fixture.SharedPrefix + "first", Fixture.SharedPrefix + "second");

            using FhirClientException ex = await Assert.ThrowsAsync<FhirClientException>(() => Client.CreateAsync(patient));

            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
            Assert.Contains(ex.OperationOutcome.Issue, i => i.Severity == OperationOutcome.IssueSeverity.Error && i.Diagnostics == CollidingUriError);

            Bundle stored = await Client.SearchAsync(ResourceType.Patient, $"_tag={tag}");
            Assert.DoesNotContain(stored.Entry, e => e.Resource is Patient);
        }

        [Fact]
        public async Task GivenABatchWithAResourceWithTwoUrisThatShareTheIndexedPrefix_WhenPosted_ThenOnlyThatEntryFails()
        {
            string tag = Guid.NewGuid().ToString();
            var valid = new Patient();
            Fixture.SetTagAndProfiles(valid, tag, Fixture.SharedPrefix + "valid");
            var colliding = new Patient();
            Fixture.SetTagAndProfiles(colliding, tag, Fixture.SharedPrefix + "first", Fixture.SharedPrefix + "second");
            var batch = new Bundle
            {
                Type = Bundle.BundleType.Batch,
                Entry = new List<Bundle.EntryComponent>
                {
                    new() { Resource = valid, Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.POST, Url = "Patient" } },
                    new() { Resource = colliding, Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.POST, Url = "Patient" } },
                },
            };

            using FhirResponse<Bundle> response = await Client.PostBundleAsync(batch);

            Assert.StartsWith("201", response.Resource.Entry[0].Response.Status, StringComparison.Ordinal);
            Assert.StartsWith("400", response.Resource.Entry[1].Response.Status, StringComparison.Ordinal);
            var outcome = Assert.IsType<OperationOutcome>(response.Resource.Entry[1].Response.Outcome);
            Assert.Contains(outcome.Issue, i => i.Diagnostics == CollidingUriError);
        }

        [Fact]
        public async Task GivenABulkDeleteFilteredByALongUri_WhenRequested_ThenTheJobIsAcceptedAndDeletesTheMatchingResource()
        {
            Assert.SkipUnless(Fixture.TestFhirServer.Metadata.SupportsOperation("bulk-delete"), "$bulk-delete not enabled on this server");

            // The URI is valid, so the warning must not be treated as an invalid search parameter (which returns 400).
            string tag = Guid.NewGuid().ToString();
            string profile = Fixture.SharedPrefix + "bulk-delete";
            var patient = new Patient();
            Fixture.SetTagAndProfiles(patient, tag, profile);
            using FhirResponse<Patient> created = await Client.CreateAsync(patient);

            string path = QueryHelpers.AddQueryString("Patient/$bulk-delete", new Dictionary<string, string> { { "_profile", profile }, { "_tag", tag } });
            using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(Fixture.HttpClient.BaseAddress, path));
            request.Headers.Add(KnownHeaders.Prefer, "respond-async");

            using HttpResponseMessage response = await Fixture.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            using FhirResponse<Parameters> status = await Client.WaitForBulkJobStatus("Bulk delete", response.Content.Headers.ContentLocation);

            var issues = (OperationOutcome)status.Resource.Parameter.Single(p => p.Name == "Issues").Resource;
            Assert.Contains(issues.Issue, i => i.Severity == OperationOutcome.IssueSeverity.Information && i.Code == OperationOutcome.IssueType.Informational && i.Diagnostics == LongUriWarning);

            Bundle remaining = await Client.SearchAsync(ResourceType.Patient, $"_tag={tag}");
            Assert.DoesNotContain(remaining.Entry, e => e.Resource is Patient);
        }

        [Fact]
        public async Task GivenAConditionalCreateWithALongUriAndPreferOperationOutcome_WhenCreated_ThenTheOperationOutcomeContainsTheWarning()
        {
            string tag = Guid.NewGuid().ToString();
            var patient = new Patient();
            Fixture.SetTagAndProfiles(patient, tag);
            var headers = new Dictionary<string, string> { { KnownHeaders.Prefer, "return=OperationOutcome" } };

            using FhirResponse<Resource> response = await Client.CreateAsync<Resource>(
                resource: patient,
                conditionalCreateCriteria: $"_profile={HttpUtility.UrlEncode(Fixture.AllowProfile)}&_tag={tag}",
                additionalHeaders: headers);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var outcome = Assert.IsType<OperationOutcome>(response.Resource);
            Assert.Contains(outcome.Issue, i => i.Severity == OperationOutcome.IssueSeverity.Information);
            Assert.Contains(outcome.Issue, i => i.Severity == OperationOutcome.IssueSeverity.Warning && i.Diagnostics == LongUriWarning);
        }

        [Fact]
        public async Task GivenABatchWithALongUriSearchAndConditionalCreate_WhenPosted_ThenEachEntryCarriesTheWarning()
        {
            string tag = Guid.NewGuid().ToString();
            string encodedProfile = HttpUtility.UrlEncode(Fixture.AllowProfile);
            var patient = new Patient();
            Fixture.SetTagAndProfiles(patient, tag);
            var batch = new Bundle
            {
                Type = Bundle.BundleType.Batch,
                Entry = new List<Bundle.EntryComponent>
                {
                    new()
                    {
                        Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.GET, Url = $"Patient?_profile={encodedProfile}&_tag={Fixture.FixtureTag}" },
                    },
                    new()
                    {
                        Resource = patient,
                        Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.POST, Url = "Patient", IfNoneExist = $"_profile={encodedProfile}&_tag={tag}" },
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
            AssertPatients(searchResult, Fixture.AllowPatient, Fixture.PrivatePatient);
            AssertLongUriWarning(searchResult);

            Assert.StartsWith("201", result.Entry[1].Response.Status, StringComparison.Ordinal);
            var createOutcome = Assert.IsType<OperationOutcome>(result.Entry[1].Response.Outcome);
            Assert.Contains(createOutcome.Issue, i => i.Severity == OperationOutcome.IssueSeverity.Warning && i.Diagnostics == LongUriWarning);
        }

        [Fact]
        public async Task GivenACreateWithALongUri_WhenPreferOperationOutcome_ThenTheWarningIsReturned()
        {
            var patient = new Patient();
            Fixture.SetTagAndProfiles(patient, Guid.NewGuid().ToString(), Fixture.SharedPrefix + "create");

            (HttpStatusCode status, Resource body, HttpResponseMessage response) = await SendAsync(HttpMethod.Post, "Patient", patient, preferOperationOutcome: true);

            Assert.Equal(HttpStatusCode.Created, status);
            Assert.NotNull(response.Headers.Location);
            AssertOnlyLongUriWarning(Assert.IsType<OperationOutcome>(body));
            response.Dispose();

            (status, body, response) = await SendAsync(HttpMethod.Post, "Patient", patient, preferOperationOutcome: false);
            Assert.Equal(HttpStatusCode.Created, status);
            Assert.IsType<Patient>(body);
            response.Dispose();
        }

        [Fact]
        public async Task GivenAConditionalUpdateMatchingOneResourceByALongUri_WhenPreferOperationOutcome_ThenItIsUpdatedAndTheWarningIsReturned()
        {
            string tag = Guid.NewGuid().ToString();
            string profile = Fixture.SharedPrefix + "conditional-update";
            var patient = new Patient();
            Fixture.SetTagAndProfiles(patient, tag, profile);
            using FhirResponse<Patient> created = await Client.CreateAsync(patient);
            created.Resource.Gender = AdministrativeGender.Female;

            (HttpStatusCode status, Resource body, HttpResponseMessage response) = await SendAsync(HttpMethod.Put, ConditionPath(profile, tag), created.Resource, preferOperationOutcome: true);
            using (response)
            {
                Assert.Equal(HttpStatusCode.OK, status);
                AssertOnlyLongUriWarning(Assert.IsType<OperationOutcome>(body));
            }

            using FhirResponse<Patient> read = await Client.ReadAsync<Patient>(ResourceType.Patient, created.Resource.Id);
            Assert.Equal(AdministrativeGender.Female, read.Resource.Gender);
        }

        [Fact]
        public async Task GivenAConditionalUpdateMatchingTwoResourcesByALongUri_WhenSent_ThenPreconditionFailedIncludesTheWarning()
        {
            string tag = Guid.NewGuid().ToString();
            var first = new Patient();
            Fixture.SetTagAndProfiles(first, tag, Fixture.SharedPrefix + "a");
            var second = new Patient();
            Fixture.SetTagAndProfiles(second, tag, Fixture.SharedPrefix + "b");
            using FhirResponse<Patient> createdFirst = await Client.CreateAsync(first);
            using FhirResponse<Patient> createdSecond = await Client.CreateAsync(second);

            (HttpStatusCode status, Resource body, HttpResponseMessage response) = await SendAsync(HttpMethod.Put, ConditionPath(Fixture.SharedPrefix + "a", tag), new Patient { Meta = first.Meta }, preferOperationOutcome: false);
            using (response)
            {
                Assert.Equal(HttpStatusCode.PreconditionFailed, status);
                var outcome = Assert.IsType<OperationOutcome>(body);
                Assert.Contains(outcome.Issue, i => i.Severity == OperationOutcome.IssueSeverity.Error);
                Assert.Contains(outcome.Issue, i => i.Severity == OperationOutcome.IssueSeverity.Warning && i.Diagnostics == LongUriWarning);
            }
        }

        [Fact]
        public async Task GivenAConditionalDeleteMatchingOneResourceByALongUri_WhenPreferOperationOutcome_ThenItIsDeletedAndTheWarningIsReturned()
        {
            string tag = Guid.NewGuid().ToString();
            string profile = Fixture.SharedPrefix + "conditional-delete";
            var patient = new Patient();
            Fixture.SetTagAndProfiles(patient, tag, profile);
            using FhirResponse<Patient> created = await Client.CreateAsync(patient);

            (HttpStatusCode status, Resource body, HttpResponseMessage response) = await SendAsync(HttpMethod.Delete, ConditionPath(profile, tag), null, preferOperationOutcome: true);
            using (response)
            {
                Assert.Equal(HttpStatusCode.OK, status);
                AssertOnlyLongUriWarning(Assert.IsType<OperationOutcome>(body));
            }

            Bundle remaining = await Client.SearchAsync(ResourceType.Patient, $"_tag={tag}");
            Assert.DoesNotContain(remaining.Entry, e => e.Resource is Patient);
        }

        [Fact]
        public async Task GivenATransactionCreatingAResourceWithALongUri_WhenPreferOperationOutcome_ThenTheTransactionStillSucceeds()
        {
            // Parallel bundle entries don't get the long URI write warning, because the data store writes several entries under one request context.
            // The transaction still succeeds.
            var patient = new Patient();
            Fixture.SetTagAndProfiles(patient, Guid.NewGuid().ToString(), Fixture.SharedPrefix + "transaction");
            var transaction = new Bundle
            {
                Type = Bundle.BundleType.Transaction,
                Entry = new List<Bundle.EntryComponent>
                {
                    new() { Resource = patient, Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.POST, Url = "Patient" } },
                },
            };

            (HttpStatusCode status, Resource body, HttpResponseMessage response) = await SendAsync(HttpMethod.Post, string.Empty, transaction, preferOperationOutcome: true);
            using (response)
            {
                Assert.Equal(HttpStatusCode.OK, status);
                Bundle.EntryComponent entry = Assert.Single(Assert.IsType<Bundle>(body).Entry);
                Assert.StartsWith("201", entry.Response.Status, StringComparison.Ordinal);
                Assert.Null(entry.Response.Outcome);
            }
        }

        [Fact]
        public async Task GivenAnUnchangedUpdateOfAResourceWithALongUri_WhenPreferOperationOutcome_ThenTheWarningIsReturned()
        {
            var patient = new Patient();
            Fixture.SetTagAndProfiles(patient, Guid.NewGuid().ToString(), Fixture.SharedPrefix + "unchanged");
            using FhirResponse<Patient> created = await Client.CreateAsync(patient);

            (HttpStatusCode status, Resource body, HttpResponseMessage response) = await SendAsync(HttpMethod.Put, $"Patient/{created.Resource.Id}", created.Resource, preferOperationOutcome: true);
            using (response)
            {
                Assert.Equal(HttpStatusCode.OK, status);
                Assert.Equal(created.Headers.ETag?.Tag, response.Headers.ETag?.Tag);
                AssertOnlyLongUriWarning(Assert.IsType<OperationOutcome>(body));
            }
        }

        [Fact]
        public async Task GivenACreateWithALongUriAndAnInvalidPreferValue_WhenCreated_ThenTheResourceIsReturned()
        {
            var patient = new Patient();
            Fixture.SetTagAndProfiles(patient, Guid.NewGuid().ToString(), Fixture.SharedPrefix + "invalid-prefer");
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Fixture.HttpClient.BaseAddress, "Patient"))
            {
                Content = new StringContent(patient.ToJson(), System.Text.Encoding.UTF8, "application/fhir+json"),
            };
            request.Headers.TryAddWithoutValidation(KnownHeaders.Prefer, "return=invalid");

            using HttpResponseMessage response = await Fixture.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.IsType<Patient>(new FhirJsonParser().Parse<Resource>(await response.Content.ReadAsStringAsync()));
        }

        private static string ConditionPath(string profile, string tag) => $"Patient?_profile={HttpUtility.UrlEncode(profile)}&_tag={tag}";

        private async System.Threading.Tasks.Task<(HttpStatusCode Status, Resource Body, HttpResponseMessage Response)> SendAsync(HttpMethod method, string path, Resource resource, bool preferOperationOutcome)
        {
            using var request = new HttpRequestMessage(method, new Uri(Fixture.HttpClient.BaseAddress, path));
            if (resource != null)
            {
                request.Content = new StringContent(resource.ToJson(), System.Text.Encoding.UTF8, "application/fhir+json");
            }

            if (preferOperationOutcome)
            {
                request.Headers.Add(KnownHeaders.Prefer, "return=OperationOutcome");
            }

            HttpResponseMessage response = await Fixture.HttpClient.SendAsync(request);
            string content = await response.Content.ReadAsStringAsync();
            Resource body = string.IsNullOrWhiteSpace(content) ? null : new FhirJsonParser().Parse<Resource>(content);
            return (response.StatusCode, body, response);
        }

        private static void AssertOnlyLongUriWarning(OperationOutcome outcome)
        {
            OperationOutcome.IssueComponent issue = Assert.Single(outcome.Issue);
            Assert.Equal(OperationOutcome.IssueSeverity.Warning, issue.Severity);
            Assert.Equal(OperationOutcome.IssueType.Informational, issue.Code);
            Assert.Equal(LongUriWarning, issue.Diagnostics);
        }

        private async System.Threading.Tasks.Task<Bundle> SearchByProfileAsync(string profile, string tag = null)
        {
            return await Client.SearchAsync(ResourceType.Patient, $"_profile={HttpUtility.UrlEncode(profile)}&_tag={tag ?? Fixture.FixtureTag}");
        }

        private static void AssertPatients(Bundle bundle, params Patient[] expected)
        {
            string[] actualIds = bundle.Entry.Where(e => e.Resource is Patient).Select(e => e.Resource.Id).OrderBy(id => id).ToArray();
            Assert.Equal(expected.Select(p => p.Id).OrderBy(id => id).ToArray(), actualIds);
        }

        private void AssertLongUriWarning(Bundle bundle)
        {
            OperationOutcome outcome = GetAndValidateOperationOutcome(bundle);
            OperationOutcome.IssueComponent issue = Assert.Single(outcome.Issue);

            Assert.Equal(OperationOutcome.IssueSeverity.Warning, issue.Severity);
            Assert.Equal(OperationOutcome.IssueType.Informational, issue.Code);
            Assert.Equal(LongUriWarning, issue.Diagnostics);
        }
    }
}
