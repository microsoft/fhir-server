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
        private const string LongUriWarning = "The value of URI search parameter(s) '_profile' is 256 characters or longer. Only the first 256 characters of URI values are compared, so the results can include resources whose URI only shares those characters with the requested value.";

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
        public async Task GivenAResourceWithTwoUrisThatShareTheIndexedPrefix_WhenCreated_ThenItIsSavedAndFoundByEitherUri()
        {
            // A separate tag keeps this resource out of the other tests, which use URIs with the same indexed prefix.
            string tag = Guid.NewGuid().ToString();
            string first = Fixture.SharedPrefix + "first";
            string second = Fixture.SharedPrefix + "second";
            var patient = new Patient();
            Fixture.SetTagAndProfiles(patient, tag, first, second);

            using FhirResponse<Patient> created = await Client.CreateAsync(patient);

            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Equal(new[] { first, second }, created.Resource.Meta.Profile);

            foreach (string profile in new[] { first, second })
            {
                Bundle bundle = await SearchByProfileAsync(profile, tag);

                AssertPatients(bundle, created.Resource);
                AssertLongUriWarning(bundle);
            }
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
