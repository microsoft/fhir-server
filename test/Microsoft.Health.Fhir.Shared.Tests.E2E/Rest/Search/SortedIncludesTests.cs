// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.Extensions;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using static Hl7.Fhir.Model.Bundle;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    /// <summary>
    /// Regression tests for sorted searches that page their included resources through the $includes operation.
    /// </summary>
    /// <remarks>
    /// Contract under test: for every page of matched resources, the union of the included resources returned
    /// inline and through the "related"/"next" chain must be exactly the set of resources referenced by that
    /// page's matches - no resource of the page may be dropped, and no resource belonging only to another page
    /// may leak in. This must hold independently of the sort order requested.
    /// </remarks>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class SortedIncludesTests : IClassFixture<SortedIncludesTestFixture>
    {
        private const int OuterPageSize = 3;
        private const int PhaseOuterPageSize = 3;
        private const int IncludesCount = 2;
        private const int MaxOuterPages = 20;
        private const int MaxRelatedPages = 50;

        private const string IncludeResultParameter = "_include=DiagnosticReport:result";
#if Stu3
        // STU3 names the DiagnosticReport -> Encounter search parameter 'context'.
        private const string IncludeEncounterParameter = "_include=DiagnosticReport:context";
#else
        private const string IncludeEncounterParameter = "_include=DiagnosticReport:encounter";
#endif

        private readonly SortedIncludesTestFixture _fixture;

        public SortedIncludesTests(SortedIncludesTestFixture fixture)
        {
            _fixture = fixture;
        }

        private TestFhirClient Client => _fixture.TestFhirClient;

        private TestFhirServer Server => _fixture.TestFhirServer;

        /// <summary>
        /// Reproduces the defect reported in work item 207476: with a date sort, the surrogate ID range carried by
        /// the $includes continuation token is derived from the first and last match of a page, which is not the
        /// page's surrogate ID range when the data is not stored in sort order.
        /// </summary>
        /// <param name="sort">The value of the _sort parameter.</param>
        /// <returns>A task executing the test.</returns>
        [SkippableTheory]
        [InlineData("-date")]
        [InlineData("date")]
        public async Task GivenADateSortedSearchWithIncludes_WhenPagingThroughTheIncludes_ThenEachPageReturnsExactlyTheIncludedResourcesOfItsMatches(string sort)
        {
            SkipIfIncludesOperationIsNotSupported();

            var query = $"_tag={_fixture.SortedTag}&_sort={sort}&_count={OuterPageSize}&_includesCount={IncludesCount}&{IncludeResultParameter}&{IncludeEncounterParameter}";

            await VerifyIncludesPagingAsync(
                KnownResourceTypes.DiagnosticReport,
                query,
                _fixture.SortedSetReportReferences,
                _fixture.IncludedReferencesForReport,
                sort);
        }

        /// <summary>
        /// Control case. When the search is ordered by _lastUpdated (or not sorted at all), a page of matches is a
        /// contiguous surrogate ID range, so the surrogate ID range carried by the $includes continuation token is
        /// valid. These cases must pass both before and after the fix; they demonstrate that the defect is specific
        /// to sorting by a search parameter value.
        /// </summary>
        /// <param name="sort">The value of the _sort parameter, or null for no sort.</param>
        /// <returns>A task executing the test.</returns>
        [SkippableTheory]
        [InlineData(null)]
        [InlineData("_lastUpdated")]
        [InlineData("-_lastUpdated")]
        public async Task GivenASearchOrderedBySurrogateIdWithIncludes_WhenPagingThroughTheIncludes_ThenEachPageReturnsExactlyTheIncludedResourcesOfItsMatches(string sort)
        {
            SkipIfIncludesOperationIsNotSupported();

            var sortParameter = string.IsNullOrEmpty(sort) ? string.Empty : $"&_sort={sort}";
            var query = $"_tag={_fixture.SortedTag}{sortParameter}&_count={OuterPageSize}&_includesCount={IncludesCount}&{IncludeResultParameter}&{IncludeEncounterParameter}";

            await VerifyIncludesPagingAsync(
                KnownResourceTypes.DiagnosticReport,
                query,
                _fixture.SortedSetReportReferences,
                _fixture.IncludedReferencesForReport,
                sort: null);
        }

        /// <summary>
        /// The same defect applies to reverse includes, which are resolved from the same matched surrogate ID range.
        /// </summary>
        /// <param name="sort">The value of the _sort parameter.</param>
        /// <returns>A task executing the test.</returns>
        [SkippableTheory]
        [InlineData("-date")]
        [InlineData("date")]
        public async Task GivenADateSortedSearchWithReverseIncludes_WhenPagingThroughTheIncludes_ThenEachPageReturnsExactlyTheIncludedResourcesOfItsMatches(string sort)
        {
            SkipIfIncludesOperationIsNotSupported();

            var query = $"_tag={_fixture.SortedTag}&_sort={sort}&_count={OuterPageSize}&_includesCount={IncludesCount}&_revinclude=DiagnosticReport:result";

            await VerifyIncludesPagingAsync(
                KnownResourceTypes.Observation,
                query,
                _fixture.SortedSetObservationReferences,
                _fixture.ReverseIncludedReferencesForObservation,
                sort);
        }

        /// <summary>
        /// Exercises the two phase sort query, where the first phase returns the matches that have the sort value
        /// and the second phase returns the matches that do not. The data and the page size are chosen so that each
        /// phase spans at least two whole pages of matches and one page falls on the phase boundary, for both sort
        /// directions: a descending sort returns the matches without a sort value last, an ascending sort returns
        /// them first. The continuation token of every page after the first therefore carries phase state, which is
        /// exactly the state the includes continuation token has to preserve.
        /// </summary>
        /// <param name="sort">The value of the _sort parameter.</param>
        /// <returns>A task executing the test.</returns>
        [SkippableTheory]
        [InlineData("-date")]
        [InlineData("date")]
        public async Task GivenADateSortedSearchWhereSomeMatchesHaveNoSortValue_WhenPagingThroughTheIncludes_ThenEachPageReturnsExactlyTheIncludedResourcesOfItsMatches(string sort)
        {
            SkipIfIncludesOperationIsNotSupported();

            var query = $"_tag={_fixture.PhaseTag}&_sort={sort}&_count={PhaseOuterPageSize}&_includesCount={IncludesCount}&{IncludeResultParameter}&{IncludeEncounterParameter}";

            var pages = await VerifyIncludesPagingAsync(
                KnownResourceTypes.DiagnosticReport,
                query,
                _fixture.PhaseSetReportReferences,
                _fixture.IncludedReferencesForReport,
                sort);

            AssertBothSortPhasesWerePaged(pages, query, sort);
        }

        /// <summary>
        /// Control case that isolates the paging of the matches from the paging of the included resources. It runs
        /// a sorted query without any _include or _revinclude parameter, so no includes continuation token is
        /// involved at all. If this fails, the outer result set itself is wrong and the failures of the other tests
        /// are not caused by the includes continuation token.
        /// </summary>
        /// <param name="sort">The value of the _sort parameter.</param>
        /// <param name="useTwoPhaseData">True to query the data set where only some matches have a date.</param>
        /// <param name="pageSize">
        /// The value of the _count parameter. The two phase data set holds 8 matches without a date and 7 with one,
        /// so a page size of 3 puts the boundary between the two sort phases in the middle of a page for both sort
        /// directions, while a page size of 4 aligns the boundary with a page boundary for an ascending sort and
        /// keeps it in the middle of a page for a descending one.
        /// </param>
        /// <returns>A task executing the test.</returns>
        [SkippableTheory]
        [InlineData("date", true, 3)]
        [InlineData("date", true, 4)]
        [InlineData("-date", true, 3)]
        [InlineData("-date", true, 4)]
        [InlineData("date", false, 3)]
        [InlineData("-date", false, 3)]
        public async Task GivenADateSortedSearchWithoutIncludes_WhenPagingThroughTheMatches_ThenEveryMatchIsReturnedExactlyOnce(string sort, bool useTwoPhaseData, int pageSize)
        {
            SkipIfIncludesOperationIsNotSupported();

            var tag = useTwoPhaseData ? _fixture.PhaseTag : _fixture.SortedTag;
            var expectedMatchReferences = useTwoPhaseData ? _fixture.PhaseSetReportReferences : _fixture.SortedSetReportReferences;
            var query = $"_tag={tag}&_sort={sort}&_count={pageSize}";

            var pages = await ReadPagesAsync(KnownResourceTypes.DiagnosticReport, query);
            var allMatchedReferences = pages.SelectMany(page => page.MatchedReferences).ToList();

            var missing = expectedMatchReferences.Except(allMatchedReferences, StringComparer.Ordinal).ToList();
            var duplicated = allMatchedReferences
                .GroupBy(x => x, StringComparer.Ordinal)
                .Where(x => x.Count() > 1)
                .Select(x => x.Key)
                .ToList();

            var failureMessage =
                $"'{query}' did not return every match exactly once." + Environment.NewLine +
                $"  expected ({expectedMatchReferences.Count}), returned ({allMatchedReferences.Count})" + Environment.NewLine +
                $"  pages: {DescribePageShape(pages)}" + Environment.NewLine +
                $"  missing: {Describe(missing)}" + Environment.NewLine +
                $"  missing sort values: {Describe(missing.Select(x => _fixture.SortDayOffsetFor(x)?.ToString() ?? "<no date>"))}" + Environment.NewLine +
                $"  duplicated: {Describe(duplicated)}";

            Assert.True(missing.Count == 0 && duplicated.Count == 0, failureMessage);

            AssertSortWasApplied(allMatchedReferences, query, sort);
        }

        private static string ToReference(EntryComponent entry)
        {
            return $"{entry.Resource.TypeName}/{entry.Resource.Id}";
        }

        private static bool IsOperationOutcome(EntryComponent entry)
        {
            return entry.Resource == null
                || entry.Resource.TypeName.Equals(KnownResourceTypes.OperationOutcome, StringComparison.OrdinalIgnoreCase);
        }

        private static string Describe(IEnumerable<string> references)
        {
            var values = references.ToList();
            return values.Count == 0 ? "<none>" : string.Join(", ", values);
        }

        private void SkipIfIncludesOperationIsNotSupported()
        {
            Skip.IfNot(_fixture.TestFhirServer.Metadata.SupportsOperation("includes"), "$includes not enabled on this server");
        }

        /// <summary>
        /// Renders the pages of matches as their sort values, so a failure message shows how the server split the
        /// result set into pages and where the boundary between the two sort phases fell.
        /// </summary>
        /// <param name="pages">The pages of matches that were read.</param>
        /// <returns>A human readable description of the pages.</returns>
        private string DescribePageShape(IEnumerable<SearchPage> pages)
        {
            return string.Join(
                ", ",
                pages.Select(page => $"[{string.Join("/", page.MatchedReferences.Select(reference => _fixture.SortDayOffsetFor(reference)?.ToString() ?? "<no date>"))}]"));
        }

        private async System.Threading.Tasks.Task<IReadOnlyList<SearchPage>> VerifyIncludesPagingAsync(
            string resourceType,
            string query,
            IReadOnlyList<string> expectedMatchReferences,
            Func<string, IReadOnlyList<string>> expectedIncludesSelector,
            string sort)
        {
            var pages = await ReadPagesAsync(resourceType, query);
            var allMatchedReferences = new List<string>();

            for (var pageNumber = 0; pageNumber < pages.Count; pageNumber++)
            {
                var page = pages[pageNumber];
                allMatchedReferences.AddRange(page.MatchedReferences);

                var expectedIncludes = page.MatchedReferences
                    .SelectMany(expectedIncludesSelector)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToList();
                var actualIncludes = page.IncludedReferences
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToList();

                var missing = expectedIncludes.Except(actualIncludes, StringComparer.Ordinal).ToList();
                var unexpected = actualIncludes.Except(expectedIncludes, StringComparer.Ordinal).ToList();

                var failureMessage =
                    $"Page {pageNumber + 1} of '{query}' returned the wrong included resources after following {page.RelatedPageCount} related page(s)." + Environment.NewLine +
                    $"  matches on this page: {Describe(page.MatchedReferences)}" + Environment.NewLine +
                    $"  expected includes ({expectedIncludes.Count}): {Describe(expectedIncludes)}" + Environment.NewLine +
                    $"  actual includes ({actualIncludes.Count}): {Describe(actualIncludes)}" + Environment.NewLine +
                    $"  missing (dropped, belong to a match on this page): {Describe(missing)}" + Environment.NewLine +
                    $"  unexpected (leaked from other pages): {Describe(unexpected)}";

                Assert.True(missing.Count == 0 && unexpected.Count == 0, failureMessage);

                Assert.Equal(actualIncludes.Count, actualIncludes.Distinct(StringComparer.Ordinal).Count());

                if (page.HasNextPage && expectedIncludes.Count > page.InlineIncludedReferenceCount)
                {
                    Assert.True(
                        page.HadRelatedLink,
                        $"Page {pageNumber + 1} of '{query}' returned {page.InlineIncludedReferenceCount} of {expectedIncludes.Count} included resources inline but no 'related' link.");
                }
            }

            Assert.Equal(allMatchedReferences.Count, allMatchedReferences.Distinct(StringComparer.Ordinal).Count());

            Assert.Equal(
                expectedMatchReferences.OrderBy(reference => reference, StringComparer.Ordinal),
                allMatchedReferences.OrderBy(reference => reference, StringComparer.Ordinal));

            // Every page after the first is reached through an outer continuation token, and that token is what the
            // sort rewriter mutates before the includes continuation token is built. Verifying only the first page
            // would leave that path untested, so require that pages reached through a continuation token really did
            // page their included resources.
            var continuationPagesWithRelatedLink = pages.Skip(1).Count(page => page.HadRelatedLink);
            Assert.True(
                continuationPagesWithRelatedLink >= 2,
                $"'{query}' only paged the included resources of {continuationPagesWithRelatedLink} page(s) reached through a continuation token; the test data no longer exercises the continuation token path.");

            AssertSortWasApplied(allMatchedReferences, query, sort);

            return pages;
        }

        /// <summary>
        /// Asserts that the search really was answered by the two phase sort query and that the test paged through
        /// both phases: at least two whole pages of matches inside each phase, plus a page on the phase boundary.
        /// The boundary page is either a page holding matches from both phases or a short page that ends the first
        /// phase early, depending on how the server terminates a phase; either shape satisfies the assertion.
        /// </summary>
        /// <param name="pages">The pages of matches that were read.</param>
        /// <param name="query">The query, used for the failure messages.</param>
        /// <param name="sort">The requested sort.</param>
        private void AssertBothSortPhasesWerePaged(IReadOnlyList<SearchPage> pages, string query, string sort)
        {
            var descending = sort.StartsWith("-", StringComparison.Ordinal);

            // The first phase returns the matches that have the sort value for a descending sort, and the matches
            // that do not have it for an ascending sort.
            var firstPhaseHasSortValue = descending;

            var wholePagesByPhase = new Dictionary<bool, int> { [true] = 0, [false] = 0 };
            var boundaryPages = 0;

            for (var pageNumber = 0; pageNumber < pages.Count; pageNumber++)
            {
                var page = pages[pageNumber];
                if (page.MatchedReferences.Count == 0)
                {
                    continue;
                }

                var hasSortValue = page.MatchedReferences
                    .Select(reference => _fixture.SortDayOffsetFor(reference) != null)
                    .Distinct()
                    .ToList();

                if (hasSortValue.Count > 1)
                {
                    // The page holds matches from both phases.
                    boundaryPages++;
                    continue;
                }

                if (page.MatchedReferences.Count == PhaseOuterPageSize)
                {
                    wholePagesByPhase[hasSortValue[0]]++;
                }

                if (hasSortValue[0] == firstPhaseHasSortValue
                    && page.HasNextPage
                    && page.MatchedReferences.Count < PhaseOuterPageSize)
                {
                    // The page is a short page that ends the first phase and hands over to the second one.
                    boundaryPages++;
                }
            }

            var shape = DescribePageShape(pages);

            Assert.True(
                wholePagesByPhase[true] >= 2,
                $"'{query}' returned {wholePagesByPhase[true]} whole page(s) of matches that have the sort value; at least 2 are required. Pages: {shape}");
            Assert.True(
                wholePagesByPhase[false] >= 2,
                $"'{query}' returned {wholePagesByPhase[false]} whole page(s) of matches that have no sort value; at least 2 are required. Pages: {shape}");
            Assert.True(
                boundaryPages >= 1,
                $"'{query}' did not return a page on the boundary between the two sort phases. Pages: {shape}");
        }

        /// <summary>
        /// Asserts that the server honored the requested date sort. Without this the include assertions could pass
        /// vacuously on a server that silently ignores the sort and returns the matches in surrogate ID order.
        /// </summary>
        /// <param name="matchedReferencesInOrder">The matched references, in the order they were returned.</param>
        /// <param name="query">The query, used for the failure message.</param>
        /// <param name="sort">The requested sort, or null when no ordering is asserted.</param>
        private void AssertSortWasApplied(IReadOnlyList<string> matchedReferencesInOrder, string query, string sort)
        {
            if (string.IsNullOrEmpty(sort))
            {
                return;
            }

            var descending = sort.StartsWith("-", StringComparison.Ordinal);
            var dayOffsets = matchedReferencesInOrder.Select(_fixture.SortDayOffsetFor).ToList();
            var described = string.Join(", ", dayOffsets.Select(x => x?.ToString() ?? "<no date>"));

            // Resources without the sort value are returned last for a descending sort and first for an ascending one.
            var withoutValueIndexes = dayOffsets
                .Select((dayOffset, index) => (dayOffset, index))
                .Where(x => x.dayOffset == null)
                .Select(x => x.index)
                .ToList();
            var withValueIndexes = dayOffsets
                .Select((dayOffset, index) => (dayOffset, index))
                .Where(x => x.dayOffset != null)
                .Select(x => x.index)
                .ToList();

            if (withoutValueIndexes.Count > 0 && withValueIndexes.Count > 0)
            {
                var phasesAreOrdered = descending
                    ? withValueIndexes.Max() < withoutValueIndexes.Min()
                    : withoutValueIndexes.Max() < withValueIndexes.Min();
                Assert.True(
                    phasesAreOrdered,
                    $"'{query}' did not group the matches without a sort value {(descending ? "last" : "first")}. Order: {described}");
            }

            var orderedValues = withValueIndexes.Select(index => dayOffsets[index].Value).ToList();
            for (var i = 1; i < orderedValues.Count; i++)
            {
                var inOrder = descending ? orderedValues[i] <= orderedValues[i - 1] : orderedValues[i] >= orderedValues[i - 1];
                Assert.True(inOrder, $"'{query}' did not return the matches in the requested order. Order: {described}");
            }
        }

        private async System.Threading.Tasks.Task<IReadOnlyList<SearchPage>> ReadPagesAsync(string resourceType, string query)
        {
            var pages = new List<SearchPage>();
            var url = $"{Server.BaseAddress}{resourceType}?{query}";

            while (!string.IsNullOrEmpty(url))
            {
                Assert.True(pages.Count < MaxOuterPages, $"'{query}' did not stop paging after {MaxOuterPages} pages.");

                var response = await Client.SearchAsync(url);
                var bundle = response.Resource;

                var matchedReferences = bundle.Entry
                    .Where(x => !IsOperationOutcome(x) && x.Search?.Mode == SearchEntryMode.Match)
                    .Select(ToReference)
                    .ToList();
                var includedReferences = bundle.Entry
                    .Where(x => !IsOperationOutcome(x) && x.Search?.Mode == SearchEntryMode.Include)
                    .Select(ToReference)
                    .ToList();
                var inlineIncludedReferenceCount = includedReferences.Count;

                var relatedLink = bundle.Link?
                    .Where(x => x.Relation.Equals("related", StringComparison.Ordinal))
                    .Select(x => x.Url)
                    .FirstOrDefault();
                var hadRelatedLink = !string.IsNullOrEmpty(relatedLink);
                var relatedPageCount = 0;

                while (!string.IsNullOrEmpty(relatedLink))
                {
                    Assert.True(relatedPageCount < MaxRelatedPages, $"'{query}' did not stop paging included resources after {MaxRelatedPages} related pages.");

                    var relatedResponse = await Client.SearchAsync(relatedLink);
                    relatedPageCount++;

                    var includedEntries = relatedResponse.Resource.Entry
                        .Where(x => !IsOperationOutcome(x))
                        .ToList();
                    Assert.InRange(includedEntries.Count, 1, IncludesCount);
                    Assert.All(includedEntries, entry => Assert.Equal(SearchEntryMode.Include, entry.Search?.Mode));
                    includedReferences.AddRange(includedEntries.Select(ToReference));

                    var nextRelatedLink = relatedResponse.Resource.NextLink?.AbsoluteUri;
                    Assert.NotEqual(relatedLink, nextRelatedLink);
                    relatedLink = nextRelatedLink;
                }

                url = bundle.NextLink?.AbsoluteUri;

                pages.Add(new SearchPage(
                    matchedReferences,
                    includedReferences,
                    inlineIncludedReferenceCount,
                    hadRelatedLink,
                    relatedPageCount,
                    !string.IsNullOrEmpty(url)));
            }

            return pages;
        }

        private sealed class SearchPage
        {
            public SearchPage(
                IReadOnlyList<string> matchedReferences,
                IReadOnlyList<string> includedReferences,
                int inlineIncludedReferenceCount,
                bool hadRelatedLink,
                int relatedPageCount,
                bool hasNextPage)
            {
                MatchedReferences = matchedReferences;
                IncludedReferences = includedReferences;
                InlineIncludedReferenceCount = inlineIncludedReferenceCount;
                HadRelatedLink = hadRelatedLink;
                RelatedPageCount = relatedPageCount;
                HasNextPage = hasNextPage;
            }

            public IReadOnlyList<string> MatchedReferences { get; }

            public IReadOnlyList<string> IncludedReferences { get; }

            public int InlineIncludedReferenceCount { get; }

            public bool HadRelatedLink { get; }

            public int RelatedPageCount { get; }

            public bool HasNextPage { get; }
        }
    }
}
