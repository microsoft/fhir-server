// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Common;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Routing;
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
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class IncludesOperationTests : IClassFixture<IncludesOperationTestFixture>
    {
        private static readonly Regex ContinuationTokenRegex = new Regex($"[?&]{KnownQueryParameterNames.ContinuationToken}");
        private static readonly Regex IncludesContinuationTokenRegex = new Regex($"[?&]{KnownQueryParameterNames.IncludesContinuationToken}");

        private readonly IncludesOperationTestFixture _fixture;

        public IncludesOperationTests(IncludesOperationTestFixture fixture)
        {
            _fixture = fixture;
        }

        private TestFhirClient Client => _fixture.TestFhirClient;

        private TestFhirServer Server => _fixture.TestFhirServer;

        /// <summary>
        /// Runs search operation test cases with _include/_revinclude and _count/_includesCount parameters.
        /// </summary>
        /// <param name="query">The request query string.</param>
        /// <param name="includesCount">The _includesCount parameter value.</param>
        /// <returns>A task executing the test</returns>
        /// <remarks>
        /// - When a number of "include" resources is within _includesCount, a  bundle response should have all "include" resources.
        /// - When a number of "include" resources is within _includesCount, a  bundle response should not have the "related" link.
        /// - When a number of "include" resources is more than _includesCount, a  bundle response should not have any "include" resource.
        /// - When a number of "include" resources is more than _includesCount, a  bundle response should have the "related" link.
        /// </remarks>
        [Theory]
        [InlineData("_include=*:*&_revinclude=*:*", null)]
        [InlineData("_include=*:*&_revinclude=*:*&_includesCount=5", 5)]
        [InlineData("_include=*:*&_revinclude=*:*&_includesCount=1000", 1000)]
        public async Task GivenASearchRequest_WhenIncludeParameterIsSpecified_ThenBundleResponseShouldHaveCorrectResources(
            string query,
            int? includesCount)
        {
            var supportsIncludes = _fixture.TestFhirServer.Metadata.SupportsOperation("includes");

            query = TagQuery(query);
            includesCount = includesCount ?? _fixture.RelatedResources.Count;

            var response = await Client.SearchAsync(ResourceType.Patient, query);
            var patientResources = _fixture.PatientResources;
            var relatedResources = _fixture.RelatedResources;

            ValidateResources(
                response,
                patientResources,
                relatedResources,
                supportsIncludes);
            ValidateLinks(
                response,
                KnownResourceTypes.Patient,
                query,
                patientResources.Count,
                relatedResources.Count,
                supportsIncludes);
        }

        /// <summary>
        /// Runs $includes operation test cases with paging.
        /// </summary>
        /// <param name="query">The request query string.</param>
        /// <param name="resourceTypes">Resource types in the query string.</param>
        /// <param name="count">The _count parameter value.</param>
        /// <param name="includesCount">The _includesCount parameter value.</param>
        /// <returns>A task executing the test</returns>
        [SkippableTheory]
        [InlineData("_include=Patient:general-practitioner&_includesCount=1", new string[] { KnownResourceTypes.Practitioner }, null, 1)]
        [InlineData("_revinclude=Observation:subject&_count=2&_includesCount=2", new string[] { KnownResourceTypes.Observation }, 2, 2)]
        [InlineData("_include=Patient:organization&_revinclude=MedicationDispense:subject&_revinclude=DiagnosticReport:subject&_count=4&_includesCount=2", new string[] { KnownResourceTypes.Organization, KnownResourceTypes.MedicationDispense, KnownResourceTypes.DiagnosticReport }, 2, 2)]
        [InlineData("_include=*:*&_revinclude=*:*&_count=2&_includesCount=10", null, 2, 10)]
        [InlineData("_include=*:*&_revinclude=*:*&_includesCount=2", null, null, 2)]
        [InlineData("_include=*:*&_revinclude=*:*&_count=1&_includesCount=1", null, 1, 1)]
        public async Task GivenAnIncludesRequest_WhenIncludesCountIsSpecified_ThenBundleResponseShouldHaveCorrectResources(
            string query,
            string[] resourceTypes,
            int? count,
            int? includesCount)
        {
            Skip.IfNot(_fixture.TestFhirServer.Metadata.SupportsOperation("includes"), "$includes not enabled on this server");

            query = TagQuery(query);
            resourceTypes = resourceTypes ?? _fixture.KnownRelatedResourceTypes;
            count = count ?? _fixture.PatientResources.Count;
            includesCount = includesCount ?? _fixture.RelatedResources.Count;

            var searchUrl = $"{Server.BaseAddress}{KnownResourceTypes.Patient}?{query}";
            while (!string.IsNullOrEmpty(searchUrl))
            {
                var response = await Client.SearchAsync(searchUrl);
                searchUrl = response.Resource.NextLink?.AbsoluteUri;

                var patientResources = response.Resource.Entry
                    .Select(x => x.Resource)
                    .Where(x => x.TypeName.Equals(KnownResourceTypes.Patient, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var relatedResources = response.Resource.Entry
                    .Select(x => x.Resource)
                    .Where(x => !x.TypeName.Equals(KnownResourceTypes.Patient, StringComparison.OrdinalIgnoreCase)
                        && !x.TypeName.Equals(KnownResourceTypes.OperationOutcome, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var relatedLink = response.Resource.Link?
                    .Where(x => x.Relation.Equals("related", StringComparison.Ordinal))
                    .Select(x => x.Url)
                    .FirstOrDefault();
                var pages = relatedLink != null ? 1 : 0;
                while (!string.IsNullOrEmpty(relatedLink))
                {
                    response = await Client.SearchAsync(relatedLink);
                    relatedResources.AddRange(response.Resource.Entry.Select(x => x.Resource));
                    relatedLink = response.Resource.NextLink?.AbsoluteUri;
                    pages++;

                    ValidateRelatedLinks(
                        response,
                        KnownResourceTypes.Patient,
                        query);
                }

                ValidateRelatedResources(
                    response,
                    patientResources,
                    relatedResources,
                    resourceTypes,
                    includesCount.Value,
                    pages);
            }
        }

        private void ValidateRelatedResources(
            FhirResponse<Bundle> response,
            IList<Resource> patientResources,
            IList<Resource> relatedResources,
            string[] relatedResourceTypes,
            int includesCount,
            int pages)
        {
            var actualRelatedResources = relatedResources
                .GroupBy(x => x.TypeName)
                .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
            var expectedRelatedResources = _fixture.RelatedResourcesFor(
                patientResources,
                relatedResourceTypes);
            Assert.Equal(expectedRelatedResources.Count, actualRelatedResources.Count);
            foreach (var resourceType in expectedRelatedResources.Keys)
            {
                Assert.True(actualRelatedResources.TryGetValue(resourceType, out _));
                Assert.Equal(expectedRelatedResources[resourceType].Count, actualRelatedResources[resourceType].Count);
                Assert.Contains(
                    actualRelatedResources[resourceType],
                    x => expectedRelatedResources[resourceType].Any(y => y.IsExactly(x)));
            }

            if (pages > 0)
            {
                Assert.Equal((int)Math.Ceiling(relatedResources.Count / (double)includesCount), pages);
            }
        }

        private void ValidateResources(
            FhirResponse<Bundle> response,
            IList<Resource> patientResources,
            IList<Resource> relatedResources,
            bool supportsIncludes)
        {
            var expectedResources = patientResources.Concat(relatedResources).ToList();
            var actualResources = response.Resource.Entry
                .Select(x => x.Resource)
                .Where(x => !x.TypeName.Equals(nameof(KnownResourceTypes.OperationOutcome), StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.Contains(
                actualResources,
                x => expectedResources.Any(y => y.IsExactly(x)));
            if (actualResources.Count < expectedResources.Count)
            {
                var operationOutcomeResources = response.Resource.Entry
                    .Select(x => x.Resource)
                    .Where(x => x.TypeName.Equals(nameof(KnownResourceTypes.OperationOutcome), StringComparison.OrdinalIgnoreCase))
                    .Cast<OperationOutcome>().ToList();
                Assert.NotEmpty(operationOutcomeResources);
                Assert.Contains(
                    operationOutcomeResources,
                    x => x.Issue.Any(
                        y => y.Severity == OperationOutcome.IssueSeverity.Warning
                            && y.Code == OperationOutcome.IssueType.Incomplete
                            && (y.Diagnostics?.Equals(supportsIncludes ? Core.Resources.TruncatedIncludeMessageForIncludes : Core.Resources.TruncatedIncludeMessage, StringComparison.OrdinalIgnoreCase) ?? false)));
            }
        }

        private void ValidateRelatedLinks(
            FhirResponse<Bundle> response,
            string resourceType,
            string query)
        {
            Assert.NotNull(response.Resource.SelfLink);
            ValidateUrl($"{Server.BaseAddress}{resourceType}/{KnownRoutes.Includes}?{query}", DecodeUrl(response.Resource.SelfLink.AbsoluteUri));
            Assert.Matches(IncludesContinuationTokenRegex, response.Resource.SelfLink.AbsoluteUri);

            if (response.Resource.Entry.Any(x => x.TypeName.Equals(KnownResourceTypes.OperationOutcome, StringComparison.OrdinalIgnoreCase)))
            {
                Assert.NotNull(response.Resource.NextLink);
                ValidateUrl($"{Server.BaseAddress}{resourceType}/{KnownRoutes.Includes}?{query}", DecodeUrl(response.Resource.NextLink.AbsoluteUri));
                Assert.Matches(IncludesContinuationTokenRegex, response.Resource.NextLink.AbsoluteUri);
            }

            var relatedLinks = response.Resource.Link?.Where(x => x.Relation.Equals("related", StringComparison.Ordinal));
            Assert.Empty(relatedLinks);
        }

        private void ValidateLinks(
            FhirResponse<Bundle> response,
            string resourceType,
            string query,
            int patientResourceCount,
            int relatedResourceCount,
            bool supportsIncludes)
        {
            Assert.NotNull(response.Resource.SelfLink);
            ValidateUrl($"{Server.BaseAddress}{resourceType}?{query}", DecodeUrl(response.Resource.SelfLink.AbsoluteUri));

            if (patientResourceCount < _fixture.PatientResources.Count)
            {
                Assert.NotNull(response.Resource.NextLink);
                ValidateUrl($"{Server.BaseAddress}{resourceType}?{query}", DecodeUrl(response.Resource.NextLink.AbsoluteUri));
                Assert.Matches(ContinuationTokenRegex, response.Resource.NextLink.AbsoluteUri);
            }

            if (supportsIncludes && relatedResourceCount < _fixture.RelatedResources.Count)
            {
                var relatedLink = response.Resource.Link?.Where(x => x.Relation.Equals("related", StringComparison.Ordinal)).FirstOrDefault();
                Assert.NotNull(relatedLink);
                ValidateUrl($"{Server.BaseAddress}{resourceType}/{KnownRoutes.Includes}?{query}", DecodeUrl(relatedLink.Url));
                Assert.Matches(IncludesContinuationTokenRegex, relatedLink.Url);
            }
        }

        private void ValidateUrl(string expectedUrl, string actualUrl, bool ignoreCt = true)
        {
            if (!Uri.TryCreate(expectedUrl, UriKind.RelativeOrAbsolute, out var expectedUri))
            {
                Assert.Fail($"Invalid expected url: {expectedUrl}");
            }

            if (!Uri.TryCreate(actualUrl, UriKind.RelativeOrAbsolute, out var actualUri))
            {
                Assert.Fail($"Invalid actual url: {actualUrl}");
            }

            Assert.Equal(expectedUri.Scheme, actualUri.Scheme);
            Assert.Equal(expectedUri.Host, actualUri.Host);

            var expectedSegments = new HashSet<string>(expectedUri.Segments);
            var actualSegments = new HashSet<string>(actualUri.Segments);
            Assert.Equal(expectedSegments.Count, actualSegments.Count);
            Assert.Contains(expectedSegments, x => actualSegments.Contains(x));

            var expectedQueryParams = expectedUri.Query.Split(new char[] { '&' }).Select(x => x.Trim('?'));
            var actualQueryParams = actualUri.Query.Split(new char[] { '&' }).Select(x => x.Trim('?'));
            if (ignoreCt)
            {
                expectedQueryParams = expectedQueryParams.Where(
                    x => !x.StartsWith(KnownQueryParameterNames.ContinuationToken) && !x.StartsWith(KnownQueryParameterNames.IncludesContinuationToken));
                actualQueryParams = actualQueryParams.Where(
                    x => !x.StartsWith(KnownQueryParameterNames.ContinuationToken) && !x.StartsWith(KnownQueryParameterNames.IncludesContinuationToken));
            }

            var expectedQuery = new HashSet<string>(expectedQueryParams);
            var actualQuery = new HashSet<string>(actualQueryParams);
            Assert.Equal(expectedQuery.Count, actualQuery.Count);
            Assert.Contains(expectedQuery, x => actualQuery.Contains(x));
        }

        private string TagQuery(string query)
        {
            return $"{query}&_tag={_fixture.Tag}";
        }

        private static string DecodeUrl(string url)
        {
            return WebUtility.UrlDecode(url);
        }
    }
}
