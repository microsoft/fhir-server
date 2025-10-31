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
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    /// <summary>
    /// Service for handling includes/revinclude searches with granular SMART v2 scopes.
    /// When granular scopes are present, includes must be processed separately from the main search
    /// because scope filters require union'd expressions that are difficult to combine in a single query.
    /// </summary>
    internal class GranularScopeIncludesService
    {
        private readonly ILogger<GranularScopeIncludesService> _logger;
        private readonly ISqlServerFhirModel _model;

        public GranularScopeIncludesService(ISqlServerFhirModel model, ILogger<GranularScopeIncludesService> logger)
        {
            _model = EnsureArg.IsNotNull(model, nameof(model));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        /// <summary>
        /// Performs include/revinclude queries for granular scope searches using a two-query approach.
        ///
        /// The match results are already obtained from the main search query.
        /// This method executes a separate query to find resources referenced by (or referencing) the matches,
        /// applying any scope restrictions to the included results.
        ///
        /// Key insight: We don't need to build complex union expressions. We simply use the match resource IDs
        /// that were already found in the first query and execute the include/revinclude expressions against those IDs.
        /// The scope filters are automatically applied to the included results.
        /// </summary>
        /// <param name="matchResults">The resources that matched the main search criteria (from first query)</param>
        /// <param name="includeExpressions">The include expressions (_include parameters)</param>
        /// <param name="revIncludeExpressions">The revinclude expressions (_revinclude parameters)</param>
        /// <param name="sqlSearchOptions">The search options containing scope and compartment restrictions</param>
        /// <param name="searchServiceDelegate">Delegate to execute the includes SQL query</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A tuple of (included resources, whether more results exist, continuation token)</returns>
        public async Task<(IList<SearchResultEntry> includes, bool includesTruncated, string includesContinuationToken)>
            PerformIncludeQueriesAsync(
                IEnumerable<SearchResultEntry> matchResults,
                IReadOnlyCollection<IncludeExpression> includeExpressions,
                IReadOnlyCollection<IncludeExpression> revIncludeExpressions,
                SqlSearchOptions sqlSearchOptions,
                Func<SqlSearchOptions, CancellationToken, Task<SearchResult>> searchServiceDelegate,
                CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(matchResults, nameof(matchResults));
            EnsureArg.IsNotNull(includeExpressions, nameof(includeExpressions));
            EnsureArg.IsNotNull(revIncludeExpressions, nameof(revIncludeExpressions));
            EnsureArg.IsNotNull(sqlSearchOptions, nameof(sqlSearchOptions));
            EnsureArg.IsNotNull(searchServiceDelegate, nameof(searchServiceDelegate));

            // If no matches or no includes/revinclude, return empty results
            var matchResultsList = matchResults.ToList();
            if (matchResultsList.Count == 0 || (includeExpressions.Count == 0 && revIncludeExpressions.Count == 0))
            {
                _logger.LogInformation("No matches or no includes/revinclude - returning empty include results");
                return (
                    includes: new List<SearchResultEntry>(),
                    includesTruncated: false,
                    includesContinuationToken: null);
            }

            // Extract the match resource IDs - these are the resources we found in the first query
            // We'll use these IDs as the source/target for includes/revinclude
            var matchResourceKeys = ExtractMatchResourceKeys(matchResultsList);

            _logger.LogInformation(
                "Executing granular scope includes query with {MatchCount} match resources, {IncludeCount} includes, {RevIncludeCount} revinclude expressions",
                matchResourceKeys.Count,
                includeExpressions.Count,
                revIncludeExpressions.Count);

            // Build list of trusted resource IDs (already filtered by the first query)
            var trustedResourceIds = matchResourceKeys
                .Select(key => new TrustedResourceIdListExpression.ResourceId(
                    GetResourceTypeId(key.ResourceTypeName),
                    key.ResourceSurrogateId))
                .ToList();

            // Create a TrustedResourceIdListExpression that represents the match results
            // This expression will bypass compartment/scope/smart compartment filters
            var trustedIdListExpression = new TrustedResourceIdListExpression(trustedResourceIds);

            _logger.LogDebug("Created TrustedResourceIdListExpression with {TrustedIdCount} resource IDs", trustedResourceIds.Count);

            // Build the includes search expression: (TrustedIdList) AND (includes OR revinclude expressions)
            Expression includesExpression = BuildIncludesExpression(
                trustedIdListExpression,
                includeExpressions,
                revIncludeExpressions);

            // Create a clone of search options with the includes expression
            var includesSearchOptions = CloneSearchOptionsForIncludes(sqlSearchOptions, includesExpression);

            _logger.LogInformation("Executing includes query with {IncludesExpressionCount} expressions", 1);

            // Execute the includes query using the search service delegate
            try
            {
                var includesSearchResult = await searchServiceDelegate(includesSearchOptions, cancellationToken);

                // Wrap included resources to mark them with the Include flag
                var includedEntries = includesSearchResult.Results
                    .Select(entry => new SearchResultEntry(entry.Resource, SearchEntryMode.Include))
                    .ToList();

                _logger.LogInformation(
                    "Includes query returned {IncludeCount} resources, truncated: {IsTruncated}",
                    includedEntries.Count,
                    includesSearchResult.UnsupportedSearchParameters?.Count > 0);

                return (
                    includes: includedEntries,
                    includesTruncated: !string.IsNullOrEmpty(includesSearchResult.IncludesContinuationToken),
                    includesContinuationToken: includesSearchResult.IncludesContinuationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing includes query for granular scope search");
                throw;
            }
        }

        /// <summary>
        /// Extracts the resource keys (type and ID) from match results.
        /// These keys identify which resources we're finding includes/revinclude for.
        ///
        /// For example, if the match query found patients with IDs: 123, 456, 789
        /// Then this method returns those IDs so we can find:
        /// - _include: resources referenced by these patients
        /// - _revinclude: resources that reference these patients
        /// </summary>
        private static List<(string ResourceTypeName, string ResourceId, long ResourceSurrogateId)> ExtractMatchResourceKeys(
            IEnumerable<SearchResultEntry> matches)
        {
            return matches
                .Select(m => (
                    m.Resource.ResourceTypeName,
                    m.Resource.ResourceId,
                    m.Resource.ResourceSurrogateId))
                .ToList();
        }

        /// <summary>
        /// Converts a resource type name (e.g., "Patient", "Observation") to its internal resource type ID.
        /// This mapping is used to populate the TrustedResourceIdListExpression.
        /// Uses ISqlServerFhirModel to dynamically resolve resource type IDs based on server configuration.
        /// </summary>
        private short GetResourceTypeId(string resourceTypeName)
        {
            if (_model.TryGetResourceTypeId(resourceTypeName, out short resourceTypeId))
            {
                return resourceTypeId;
            }

            throw new InvalidOperationException($"Unknown resource type: {resourceTypeName}");
        }

        /// <summary>
        /// Builds the search expression for finding included resources.
        /// Combines the trusted resource ID list with include and revinclude expressions.
        /// </summary>
        private static Expression BuildIncludesExpression(
            TrustedResourceIdListExpression trustedIdListExpression,
            IReadOnlyCollection<IncludeExpression> includeExpressions,
            IReadOnlyCollection<IncludeExpression> revIncludeExpressions)
        {
            var allIncludeExpressions = new List<Expression> { trustedIdListExpression };

            // Add all include expressions
            if (includeExpressions.Count > 0)
            {
                allIncludeExpressions.AddRange(includeExpressions);
            }

            // Add all revinclude expressions
            if (revIncludeExpressions.Count > 0)
            {
                allIncludeExpressions.AddRange(revIncludeExpressions);
            }

            // If only trusted IDs, return just that
            if (allIncludeExpressions.Count == 1)
            {
                return trustedIdListExpression;
            }

            // Combine with AND: (trusted IDs) AND (includes OR revinclude)
            var includesOrRevIncludes = new MultiaryExpression(
                MultiaryOperator.Or,
                allIncludeExpressions.Skip(1).ToList());

            return new MultiaryExpression(
                MultiaryOperator.And,
                new List<Expression> { trustedIdListExpression, includesOrRevIncludes });
        }

        /// <summary>
        /// Creates a clone of the search options for executing the includes query.
        /// </summary>
        private static SqlSearchOptions CloneSearchOptionsForIncludes(
            SqlSearchOptions originalOptions,
            Expression includesExpression)
        {
            var clonedOptions = new SqlSearchOptions(originalOptions)
            {
                // Keep the search expression for finding includes
                Expression = includesExpression,

                // Reset pagination to get first page of includes
                ContinuationToken = null,

                // Keep scope restrictions - they should apply to included resources
                // Keep compartment restrictions - they should apply to included resources

                // Mark as includes query
                HasGranularScopesWithIncludes = false, // Don't recursively apply two-query approach
            };

            return clonedOptions;
        }
    }
}
