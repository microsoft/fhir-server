// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer.Features.Client;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    /// <summary>
    /// SQL Server search service implementation.
    /// </summary>
    internal partial class SqlServerSearchService
    {
        /// <summary>
        /// Feature flag that gates creation of the 3-column reference-type filtered statistic on the
        /// ReferenceSearchParam table (the stat that additionally filters on ReferenceResourceTypeId,
        /// i.e. <c>WHERE ResourceTypeId = .. AND SearchParamId = .. AND ReferenceResourceTypeId = ..</c>).
        /// Off by default: unless a row exists in the Parameters table with this Id set to an enabled
        /// value, these stats are not created and reference searches fall back to the broader 2-column
        /// filtered statistic (ResourceTypeId + SearchParamId).
        /// </summary>
        internal const string ReferenceResourceTypeFilteredStatsParameterId = "Search.ReferenceResourceTypeFilteredStats.IsEnabled";
        private static CachedParameter<SqlServerSearchService> _referenceResourceTypeFilteredStats;

        /// <summary>
        /// Returns the column names used for filtered statistics on the given search parameter table.
        /// Only tables that benefit from per-resource-type filtered statistics are included.
        /// </summary>
        /// <param name="table">The fully-qualified table name (e.g. <c>dbo.TokenSearchParam</c>).</param>
        /// <returns>The set of column names, or an empty set when the table does not support filtered stats.</returns>
        internal static HashSet<string> GetKeyColumns(string table)
        {
            var results = new HashSet<string>();
            if (table == VLatest.StringSearchParam.TableName)
            {
                results.Add(VLatest.StringSearchParam.Text.Metadata.Name);
            }
            else if (table == VLatest.TokenSearchParam.TableName)
            {
                results.Add(VLatest.TokenSearchParam.Code.Metadata.Name);
            }
            else if (table == VLatest.DateTimeSearchParam.TableName)
            {
                results.Add(VLatest.DateTimeSearchParam.StartDateTime.Metadata.Name);
                results.Add(VLatest.DateTimeSearchParam.EndDateTime.Metadata.Name);
            }
            else if (table == VLatest.NumberSearchParam.TableName)
            {
                results.Add(VLatest.NumberSearchParam.LowValue.Metadata.Name);
                results.Add(VLatest.NumberSearchParam.HighValue.Metadata.Name);
            }
            else if (table == VLatest.QuantitySearchParam.TableName)
            {
                results.Add(VLatest.QuantitySearchParam.LowValue.Metadata.Name);
                results.Add(VLatest.QuantitySearchParam.HighValue.Metadata.Name);
            }
            else if (table == VLatest.ReferenceSearchParam.TableName)
            {
                results.Add(VLatest.ReferenceSearchParam.ReferenceResourceId.Metadata.Name);
            }

            return results;
        }

        /*
        private async Task CreateStats(SqlRootExpression expression, CancellationToken cancel)
        {
            if (_resourceSearchParamStats == null)
            {
                lock (_locker)
                {
                    _resourceSearchParamStats ??= new ResourceSearchParamStats(_sqlRetryService, _logger, cancel);
                }
            }

            await _resourceSearchParamStats.Create(expression, _sqlRetryService, _logger, (SqlServerFhirModel)_model, cancel);
        }
        */

        internal static ICollection<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId)> GetStatsFromCache()
        {
            return Array.Empty<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId)>();
        }

        /// <summary>
        /// Forces the next read of the reference-resource-type filtered statistics feature flag to bypass the
        /// in-memory cache and re-query the Parameters table. Intended for integration tests that toggle the flag.
        /// </summary>
        internal static void ResetReferenceResourceTypeFilteredStatsCache()
        {
            _referenceResourceTypeFilteredStats?.Reset();
        }

        internal async Task<IReadOnlyList<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId)>> GetStatsFromDatabase(CancellationToken cancel)
        {
            return await GetStatsFromDatabase(_sqlRetryService, _logger, cancel);
        }

        private static async Task<IReadOnlyList<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId)>> GetStatsFromDatabase(ISqlRetryService sqlRetryService, ILogger<SqlServerSearchService> logger, CancellationToken cancel)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.GetResourceSearchParamStats", CommandType = CommandType.StoredProcedure };
            return await cmd.ExecuteReaderAsync(
                            sqlRetryService,
                            (reader) =>
                            {
                                // ST_Code_WHERE_ResourceTypeId_28_SearchParamId_202
                                // ST_ReferenceResourceId_WHERE_ResourceTypeId_40_SearchParamId_1219_ReferenceResourceTypeId_2
                                var table = reader.GetString(0);
                                var stats = reader.GetString(1);
                                var split = stats.Split("_");
                                var column = split[1];
                                var resourceTypeId = short.Parse(split[4]);
                                var searchParamId = short.Parse(split[6]);
                                short? referenceResourceTypeId = split.Length > 8 ? short.Parse(split[8]) : null;
                                return ("dbo." + table, column, resourceTypeId, searchParamId, referenceResourceTypeId);
                            },
                            logger,
                            cancel);
        }

        internal class ResourceSearchParamStats
        {
            private readonly ConcurrentDictionary<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId), bool> _stats;

            public ResourceSearchParamStats(
                ISqlRetryService sqlRetryService,
                ILogger<SqlServerSearchService> logger,
                CancellationToken cancel)
            {
                _stats = new ConcurrentDictionary<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId), bool>();
                Init(sqlRetryService, logger, cancel).Wait(cancel);
            }

            public ICollection<(string TableName, string ColumnName, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId)> GetStatsFromCache()
            {
                return _stats.Keys;
            }

            /*
            public async Task Create(
                SqlRootExpression expression,
                ISqlRetryService sqlRetryService,
                ILogger<SqlServerSearchService> logger,
                SqlServerFhirModel model,
                CancellationToken cancel)
            {
                // Iterate over top-level table expressions
                for (int tableIndex = 0; tableIndex < expression.SearchParamTableExpressions.Count; tableIndex++)
                {
                    var tableExpression = expression.SearchParamTableExpressions[tableIndex];

                    // We support Normal, Union, and NotExists. Skip include/sort/etc.
                    if (tableExpression.Kind != SearchParamTableExpressionKind.Normal &&
                        tableExpression.Kind != SearchParamTableExpressionKind.Union &&
                        tableExpression.Kind != SearchParamTableExpressionKind.NotExists)
                    {
                        continue;
                    }

                    // Collected raw tuples (table, resourceTypeId, searchParamId, referenceResourceTypeId)
                    var collected = new List<(string Table, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId)>();

                    // Track whether we also need a ResourceSurrogateId filtered stat
                    bool hasResourceSurrogateId = false;

                    if (tableExpression.Kind == SearchParamTableExpressionKind.Normal)
                    {
                        ProcessPredicateForStats(tableExpression.Predicate, tableExpression.QueryGenerator, model, tableExpression.ChainLevel, expression, tableIndex, collected, logger, parentMultiaryContext: null, isUnionBranch: false);
                    }
                    else if (tableExpression.Kind == SearchParamTableExpressionKind.Union &&
                             tableExpression.Predicate is UnionExpression unionPredicate)
                    {
                        // Each union branch is its own logical context; do not cross-associate resource type constraints
                        foreach (var branch in unionPredicate.Expressions)
                        {
                            ProcessUnionBranch(branch, tableExpression.QueryGenerator, model, tableExpression.ChainLevel, expression, tableIndex, collected, logger);
                        }
                    }
                    else if (tableExpression.Kind == SearchParamTableExpressionKind.NotExists)
                    {
                        ProcessNotExistsForStats(tableExpression.Predicate, tableExpression.QueryGenerator, model, collected, out hasResourceSurrogateId);
                    }

                    // Emit stats rows
                    foreach (var (table, resourceTypeId, searchParamId, referenceResourceTypeId) in collected)
                    {
                        // For a NotExists (:missing=true) predicate that carries a ResourceSurrogateId range
                        // constraint, create an additional ResourceSurrogateId filtered stat. This is emitted
                        // independently of the value-key columns so it is still created for tables that have no
                        // value columns (e.g. ReferenceSearchParam, UriSearchParam), which is exactly where the
                        // anti-join on ResourceSurrogateId benefits most. hasResourceSurrogateId is only ever set
                        // in the NotExists branch, so this never adds stats for Normal/Union searches.
                        if (hasResourceSurrogateId)
                        {
                            await Create(table, "ResourceSurrogateId", resourceTypeId, searchParamId, null, sqlRetryService, logger, cancel);
                        }

                        var columns = GetKeyColumns(table);
                        if (columns.Count == 0)
                        {
                            continue;
                        }

                        foreach (var column in columns)
                        {
                            await Create(table, column, resourceTypeId, searchParamId, referenceResourceTypeId, sqlRetryService, logger, cancel);
                        }
                    }
                }
            }

            private void ProcessUnionBranch(
                Expression unionInner,
                SearchParamTableExpressionQueryGenerator defaultGenerator,
                SqlServerFhirModel model,
                int chainLevel,
                SqlRootExpression root,
                int tableIndex,
                List<(string Table, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId)> collected,
                ILogger logger)
            {
                // A union branch may itself be a MultiaryExpression (AND group) or a single expression
                if (unionInner is MultiaryExpression multi)
                {
                    // Treat this AND group as a distinct resource-type/search-param context
                    foreach (var child in multi.Expressions)
                    {
                        ProcessPredicateForStats(child, defaultGenerator, model, chainLevel, root, tableIndex, collected, logger, parentMultiaryContext: multi, isUnionBranch: true);
                    }
                }
                else
                {
                    ProcessPredicateForStats(unionInner, defaultGenerator, model, chainLevel, root, tableIndex, collected, logger, parentMultiaryContext: null, isUnionBranch: true);
                }
            }

            /// <summary>
            /// Processes a NotExists predicate (produced by MissingSearchParamVisitor for :missing=true queries)
            /// to extract the owning search parameter and optionally detect ResourceSurrogateId range constraints.
            /// </summary>
            private void ProcessNotExistsForStats(
                Expression predicate,
                SearchParamTableExpressionQueryGenerator defaultGenerator,
                SqlServerFhirModel model,
                List<(string Table, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId)> collected,
                out bool hasResourceSurrogateId)
            {
                hasResourceSurrogateId = false;

                var missingParams = new List<MissingSearchParameterExpression>();
                var resourceTypeIds = new HashSet<short>();
                bool foundSurrogateId = false;

                CollectNotExistsLeaves(predicate, missingParams, resourceTypeIds, model, ref foundSurrogateId);

                // Conservative: skip if predicate resolves to anything other than exactly one owning search parameter
                if (missingParams.Count != 1)
                {
                    return;
                }

                var missingParam = missingParams[0];

                // Skip synthetic parameters
                if (missingParam.Parameter.Name == SqlSearchParameters.PrimaryKeyParameterName ||
                    missingParam.Parameter.Name == SqlSearchParameters.ResourceSurrogateIdParameterName)
                {
                    return;
                }

                // Resolve the table for this parameter
                var specificGenerator = missingParam.AcceptVisitor(_queryGeneratorFactory, _queryGeneratorFactory.InitialContext) ?? defaultGenerator;
                var tableName = specificGenerator.Table.TableName;

                // Resolve search param ID
                if (!model.TryGetSearchParamId(missingParam.Parameter.Url, out var searchParamId) || searchParamId == 0)
                {
                    return;
                }

                // Require a concrete resource-type constraint that came from the predicate itself. We
                // intentionally do NOT fall back to the search parameter's BaseResourceTypes here: for a typed
                // :missing search (e.g. Observation?_tag:missing=true) the resource type is present in the
                // NotExists predicate as a sibling, so it is already captured above. An untyped cross-type
                // :missing search (e.g. ?_tag:missing=true) has no concrete type and a BaseResourceTypes
                // fallback would fan out a stat for every base resource type, so we skip it instead.
                if (resourceTypeIds.Count == 0)
                {
                    return;
                }

                hasResourceSurrogateId = foundSurrogateId;

                foreach (var rtId in resourceTypeIds)
                {
                    collected.Add((tableName, rtId, searchParamId, null));
                }
            }

            /// <summary>
            /// Recursively collects MissingSearchParameterExpression leaves, resource type constraints,
            /// and detects ResourceSurrogateId range constraints from a NotExists predicate.
            /// </summary>
            internal static void CollectNotExistsLeaves(
                Expression expression,
                List<MissingSearchParameterExpression> missingParams,
                HashSet<short> resourceTypeIds,
                SqlServerFhirModel model,
                ref bool foundSurrogateId)
            {
                switch (expression)
                {
                    case MissingSearchParameterExpression msp:
                        missingParams.Add(msp);
                        break;

                    case SearchParameterExpression spe:
                        if (spe.Parameter.Name == SearchParameterNames.ResourceType)
                        {
                            CollectResourceTypesFromExpression(spe.Expression, model, resourceTypeIds);
                        }
                        else if (spe.Parameter.Name == SqlSearchParameters.ResourceSurrogateIdParameterName)
                        {
                            foundSurrogateId = true;
                        }

                        break;

                    case MultiaryExpression multi:
                        foreach (var inner in multi.Expressions)
                        {
                            CollectNotExistsLeaves(inner, missingParams, resourceTypeIds, model, ref foundSurrogateId);
                        }

                        break;
                }
            }

            private void ProcessPredicateForStats(
                Expression predicate,
                SearchParamTableExpressionQueryGenerator defaultGenerator,
                SqlServerFhirModel model,
                int chainLevel,
                SqlRootExpression root,
                int tableIndex,
                List<(string Table, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId)> collected,
                ILogger logger,
                MultiaryExpression parentMultiaryContext,
                bool isUnionBranch)
            {
                switch (predicate)
                {
                    case SearchParameterExpression spe:
                        HandleSearchParameterExpression(spe, defaultGenerator, model, chainLevel, root, tableIndex, collected, parentMultiaryContext, isUnionBranch);
                        break;

                    case MultiaryExpression multi:
                        foreach (var inner in multi.Expressions)
                        {
                            ProcessPredicateForStats(inner, defaultGenerator, model, chainLevel, root, tableIndex, collected, logger, parentMultiaryContext: multi, isUnionBranch: isUnionBranch);
                        }

                        break;

                    case UnionExpression union:
                        foreach (var branch in union.Expressions)
                        {
                            ProcessUnionBranch(branch, defaultGenerator, model, chainLevel, root, tableIndex, collected, logger);
                        }

                        break;

                    default:
                        // Non-search-parameter leaf (e.g. compartment) � ignore for stats
                        break;
                }
            }

            private void HandleSearchParameterExpression(
                SearchParameterExpression spe,
                SearchParamTableExpressionQueryGenerator defaultGenerator,
                SqlServerFhirModel model,
                int chainLevel,
                SqlRootExpression root,
                int tableIndex,
                List<(string Table, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId)> collected,
                MultiaryExpression parentMultiaryContext,
                bool isUnionBranch)
            {
                // Ignore synthetic parameters
                if (spe.Parameter.Name == SqlSearchParameters.PrimaryKeyParameterName ||
                    spe.Parameter.Name == SqlSearchParameters.ResourceSurrogateIdParameterName)
                {
                    return;
                }

                // Determine query generator for this specific expression
                var specificGenerator = spe.AcceptVisitor(_queryGeneratorFactory, _queryGeneratorFactory.InitialContext) ?? defaultGenerator;
                var tableName = specificGenerator.Table.TableName;

                // Extract searchParamId (skip if not resolvable)
                if (!model.TryGetSearchParamId(spe.Parameter.Url, out var searchParamId) || searchParamId == 0)
                {
                    // If this is the _type search parameter, we don't create stats entries directly for it
                    return;
                }

                // Collect applicable resource types
                var resourceTypeIds = new HashSet<short>();

                // 1. If inside an AND group (Multiary) gather resource type constraints from siblings
                if (parentMultiaryContext != null)
                {
                    foreach (var siblingSpe in parentMultiaryContext.Expressions
                        .OfType<SearchParameterExpression>()
                        .Where(spe => spe.Parameter.Name == SearchParameterNames.ResourceType))
                    {
                        CollectResourceTypesFromExpression(siblingSpe.Expression, model, resourceTypeIds);
                    }
                }

                // 2. For chain level 1, derive types from predecessor chain expression
                if (resourceTypeIds.Count == 0 &&
                    chainLevel == 1 &&
                    tableIndex > 0)
                {
                    var prev = root.SearchParamTableExpressions[tableIndex - 1];
                    if (prev.Kind == SearchParamTableExpressionKind.Chain &&
                        prev.Predicate is SqlChainLinkExpression chainLink)
                    {
                        foreach (var rt in chainLink.ResourceTypes)
                        {
                            if (model.TryGetResourceTypeId(rt, out var rtId))
                            {
                                resourceTypeIds.Add(rtId);
                            }
                        }
                    }
                }

                // 3. Fall back to base resource types from SearchParameter definition
                if (resourceTypeIds.Count == 0 && spe.Parameter.BaseResourceTypes?.Count > 0)
                {
                    foreach (var baseType in spe.Parameter.BaseResourceTypes)
                    {
                        if (model.TryGetResourceTypeId(baseType, out var rtId))
                        {
                            resourceTypeIds.Add(rtId);
                        }
                    }
                }

                // Skip if still none (cannot reliably pair)
                if (resourceTypeIds.Count == 0)
                {
                    return;
                }

                // For ReferenceSearchParam, extract target reference resource types from the expression
                // to create per-target-type filtered statistics (Approach B).
                var referenceResourceTypeIds = new HashSet<short>();
                if (tableName == VLatest.ReferenceSearchParam.TableName)
                {
                    CollectReferenceResourceTypes(spe.Expression, model, referenceResourceTypeIds);
                }

                foreach (var rtId in resourceTypeIds)
                {
                    if (referenceResourceTypeIds.Count > 0)
                    {
                        // Create one entry per target type for Approach B stats
                        foreach (var refRtId in referenceResourceTypeIds)
                        {
                            collected.Add((tableName, rtId, searchParamId, refRtId));
                        }
                    }
                    else
                    {
                        collected.Add((tableName, rtId, searchParamId, null));
                    }
                }
            }

            /// <summary>
            /// Walks the inner expression tree of a reference search parameter to extract
            /// target resource type IDs from <see cref="StringExpression"/> nodes with
            /// <see cref="FieldName.ReferenceResourceType"/>.
            /// </summary>
            private static void CollectReferenceResourceTypes(Expression expression, SqlServerFhirModel model, HashSet<short> referenceResourceTypeIds)
            {
                switch (expression)
                {
                    case StringExpression se when se.FieldName == FieldName.ReferenceResourceType:
                        if (model.TryGetResourceTypeId(se.Value, out var rtId))
                        {
                            referenceResourceTypeIds.Add(rtId);
                        }

                        break;
                    case MultiaryExpression me:
                        foreach (var inner in me.Expressions)
                        {
                            CollectReferenceResourceTypes(inner, model, referenceResourceTypeIds);
                        }

                        break;
                    case SearchParameterExpression innerSpe:
                        CollectReferenceResourceTypes(innerSpe.Expression, model, referenceResourceTypeIds);
                        break;
                }
            }
            */

            private static void CollectResourceTypesFromExpression(Expression expression, SqlServerFhirModel model, HashSet<short> resourceTypeIds)
            {
                switch (expression)
                {
                    case StringExpression se:
                        if (model.TryGetResourceTypeId(se.Value, out var rtId))
                        {
                            resourceTypeIds.Add(rtId);
                        }

                        break;
                    case MultiaryExpression me:
                        foreach (var inner in me.Expressions)
                        {
                            CollectResourceTypesFromExpression(inner, model, resourceTypeIds);
                        }

                        break;
                    case SearchParameterExpression innerSpe:
                        CollectResourceTypesFromExpression(innerSpe.Expression, model, resourceTypeIds);
                        break;
                }
            }

            private static HashSet<string> GetKeyColumns(string table) => SqlServerSearchService.GetKeyColumns(table);

            private async Task Create(string tableName, string columnName, short resourceTypeId, short searchParamId, short? referenceResourceTypeId, ISqlRetryService sqlRetryService, ILogger<SqlServerSearchService> logger, CancellationToken cancel)
            {
                // The 3-column reference-type filtered stat (which additionally filters on ReferenceResourceTypeId)
                // is gated behind a feature flag that is OFF by default. When disabled, fall back to the broader
                // 2-column filtered stat (ResourceTypeId + SearchParamId) so reference searches still get a stat.
                if (referenceResourceTypeId.HasValue && !_referenceResourceTypeFilteredStats.IsEnabled(sqlRetryService))
                {
                    referenceResourceTypeId = null;
                }

                if (_stats.ContainsKey((tableName, columnName, resourceTypeId, searchParamId, referenceResourceTypeId)))
                {
                    logger.LogInformation("ResourceSearchParamStats.FoundInCache Table={Table} Column={Column} Type={ResourceType} Param={SearchParam} RefType={ReferenceResourceType}", tableName, columnName, resourceTypeId, searchParamId, referenceResourceTypeId);
                    return;
                }

                try
                {
                    using var cmd = new SqlCommand() { CommandText = "dbo.CreateResourceSearchParamStats", CommandType = CommandType.StoredProcedure };
                    cmd.Parameters.AddWithValue("@Table", tableName[4..]); // remove dbo.
                    cmd.Parameters.AddWithValue("@Column", columnName);
                    cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
                    cmd.Parameters.AddWithValue("@SearchParamId", searchParamId);
                    cmd.Parameters.AddWithValue("@ReferenceResourceTypeId", (object)referenceResourceTypeId ?? DBNull.Value);
                    await cmd.ExecuteNonQueryAsync(sqlRetryService, logger, cancel);

                    _stats.TryAdd((tableName, columnName, resourceTypeId, searchParamId, referenceResourceTypeId), true);

                    logger.LogInformation("ResourceSearchParamStats.CreateStats.Completed Table={Table} Column={Column} Type={ResourceType} Param={SearchParam} RefType={ReferenceResourceType}", tableName, columnName, resourceTypeId, searchParamId, referenceResourceTypeId);
                }
                catch (SqlException ex)
                {
                    logger.LogWarning(ex, "ResourceSearchParamStats.CreateStats: Exception={Exception}", ex.Message);
                }
            }

            private async Task Init(ISqlRetryService sqlRetryService, ILogger<SqlServerSearchService> logger, CancellationToken cancel)
            {
                try
                {
                    var stats = await GetStatsFromDatabase(sqlRetryService, logger, cancel);

                    foreach (var stat in stats)
                    {
                        _stats.TryAdd(stat, true);
                    }

                    logger.LogInformation("ResourceSearchParamStats.Init: Stats={Stats}", stats.Count);
                }
                catch (SqlException ex)
                {
                    logger.LogWarning(ex, "ResourceSearchParamStats.Init: Exception={Exception}", ex.Message);
                }
            }
        }
    }
}
