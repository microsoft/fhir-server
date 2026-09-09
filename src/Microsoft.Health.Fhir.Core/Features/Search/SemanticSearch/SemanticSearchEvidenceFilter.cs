// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Authorizes external semantic evidence sources through the normal FHIR search pipeline.
    /// </summary>
    public sealed class SemanticSearchEvidenceFilter : ISemanticSearchEvidenceFilter
    {
        private readonly ISearchService _searchService;
        private readonly IDataResourceFilter _dataResourceFilter;

        /// <summary>
        /// Initializes a new instance of the <see cref="SemanticSearchEvidenceFilter"/> class.
        /// </summary>
        /// <param name="searchService">The authorized FHIR search service.</param>
        /// <param name="dataResourceFilter">The standard resource result filter.</param>
        public SemanticSearchEvidenceFilter(ISearchService searchService, IDataResourceFilter dataResourceFilter)
        {
            _searchService = EnsureArg.IsNotNull(searchService, nameof(searchService));
            _dataResourceFilter = EnsureArg.IsNotNull(dataResourceFilter, nameof(dataResourceFilter));
        }

        /// <inheritdoc />
        public async Task<SearchResult> FilterAsync(SearchResult searchResult, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(searchResult, nameof(searchResult));

            List<SearchResultEntry> results = searchResult.Results.ToList();
            var externalSourcesByResult = new Dictionary<int, IReadOnlyList<SourceIdentity>>();
            var invalidResults = new HashSet<int>();
            var externalSources = new HashSet<SourceIdentity>();

            for (int index = 0; index < results.Count; index++)
            {
                SearchResultEntry result = results[index];
                if (result.SearchEntryMode != SearchEntryMode.Match || !result.Score.HasValue)
                {
                    continue;
                }

                if (result.EvidenceItems.Count == 0)
                {
                    invalidResults.Add(index);
                    continue;
                }

                var resultSources = new HashSet<SourceIdentity>();
                foreach (SemanticSearchEvidence evidence in result.EvidenceItems)
                {
                    if (!TryAddExternalReference(result, evidence.SourceReference, resultSources) ||
                        (!string.IsNullOrWhiteSpace(evidence.WitnessReference) &&
                         !TryAddExternalReference(result, evidence.WitnessReference, resultSources)))
                    {
                        invalidResults.Add(index);
                        break;
                    }
                }

                if (!invalidResults.Contains(index) && resultSources.Count > 0)
                {
                    externalSourcesByResult[index] = resultSources.ToList();
                    externalSources.UnionWith(resultSources);
                }
            }

            if (invalidResults.Count == 0 && externalSources.Count == 0)
            {
                return searchResult;
            }

            HashSet<SourceIdentity> authorizedSources = await GetAuthorizedSourcesAsync(externalSources, cancellationToken);
            var filteredResults = new List<SearchResultEntry>(results.Count);
            for (int index = 0; index < results.Count; index++)
            {
                if (invalidResults.Contains(index) ||
                    (externalSourcesByResult.TryGetValue(index, out IReadOnlyList<SourceIdentity> resultSources) && resultSources.Any(source => !authorizedSources.Contains(source))))
                {
                    continue;
                }

                filteredResults.Add(results[index]);
            }

            AssignEvidenceRanks(filteredResults);
            var filteredSearchResult = new SearchResult(
                filteredResults,
                searchResult.ContinuationToken,
                searchResult.SortOrder,
                searchResult.UnsupportedSearchParameters,
                searchResult.SearchIssues,
                searchResult.IncludesContinuationToken)
            {
                MaxResourceSurrogateId = searchResult.MaxResourceSurrogateId,
                ReindexResult = searchResult.ReindexResult,
                TotalCount = filteredResults.Count == results.Count ? searchResult.TotalCount : null,
            };

            return filteredSearchResult;
        }

        private async Task<HashSet<SourceIdentity>> GetAuthorizedSourcesAsync(
            IReadOnlyCollection<SourceIdentity> externalSources,
            CancellationToken cancellationToken)
        {
            var authorizedSources = new HashSet<SourceIdentity>();
            foreach (IGrouping<string, SourceIdentity> resourceTypeGroup in externalSources.GroupBy(source => source.ResourceType, StringComparer.Ordinal))
            {
                string ids = string.Join(",", resourceTypeGroup.Select(source => source.Id).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal));
                var searchParameters = new[]
                {
                    Tuple.Create(KnownQueryParameterNames.Id, ids),
                };

                SearchResult sourceSearchResult;
                try
                {
                    sourceSearchResult = await _searchService.SearchAsync(resourceTypeGroup.Key, searchParameters, cancellationToken);
                }
                catch (Exception exception) when (exception is UnauthorizedFhirActionException or ResourceNotSupportedException)
                {
                    continue;
                }

                if (sourceSearchResult == null)
                {
                    continue;
                }

                sourceSearchResult = _dataResourceFilter.Filter(sourceSearchResult);
                authorizedSources.UnionWith(sourceSearchResult.Results
                    .Where(result => result.SearchEntryMode == SearchEntryMode.Match)
                    .Select(result => new SourceIdentity(result.Resource.ResourceTypeName, result.Resource.ResourceId)));
            }

            return authorizedSources;
        }

        private static bool TryParseSourceReference(string sourceReference, out SourceIdentity source)
        {
            source = default;
            string[] segments = sourceReference?.Split('/') ?? Array.Empty<string>();
            bool validShape = segments.Length == 2 ||
                (segments.Length == 4 && string.Equals(segments[2], "_history", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(segments[3]));
            if (!validShape ||
                string.IsNullOrWhiteSpace(segments[0]) ||
                string.IsNullOrWhiteSpace(segments[1]))
            {
                return false;
            }

            source = new SourceIdentity(segments[0], segments[1]);
            return true;
        }

        private static bool TryAddExternalReference(
            SearchResultEntry result,
            string reference,
            ISet<SourceIdentity> resultSources)
        {
            if (!TryParseSourceReference(reference, out SourceIdentity source))
            {
                return false;
            }

            if (!IsOwnerSource(result, source))
            {
                resultSources.Add(source);
            }

            return true;
        }

        private static bool IsOwnerSource(SearchResultEntry result, SourceIdentity source)
        {
            return string.Equals(result.Resource.ResourceTypeName, source.ResourceType, StringComparison.Ordinal) &&
                string.Equals(result.Resource.ResourceId, source.Id, StringComparison.Ordinal);
        }

        private static void AssignEvidenceRanks(List<SearchResultEntry> results)
        {
            int[] semanticResultIndexes = results
                .Select((result, index) => (result, index))
                .Where(item => item.result.SearchEntryMode == SearchEntryMode.Match && item.result.EvidenceItems.Count > 0)
                .Select(item => item.index)
                .ToArray();
            IReadOnlyList<IReadOnlyList<SemanticSearchEvidence>> rankedEvidence = SemanticSearchEvidenceRanker.AssignRanks(
                semanticResultIndexes.Select(index => results[index].EvidenceItems).ToList());

            for (int index = 0; index < semanticResultIndexes.Length; index++)
            {
                int resultIndex = semanticResultIndexes[index];
                SearchResultEntry result = results[resultIndex];
                results[resultIndex] = new SearchResultEntry(
                    result.Resource,
                    result.SearchEntryMode,
                    result.Score,
                    evidenceItems: rankedEvidence[index]);
            }
        }

        private readonly record struct SourceIdentity(string ResourceType, string Id);
    }
}
