// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Storage;
using Expression = Microsoft.Health.Fhir.Core.Features.Search.Expressions.Expression;
using SortOrder = Microsoft.Health.Fhir.Core.Features.Search.SortOrder;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class SqlQueryGenerator : DefaultSqlExpressionVisitor<SearchOptions, object>
    {
        // In the case of input search parameter being too complex, there is a possibility of a stack overflow.
        // Stack overflow exceptions cannot be caught in .NET and will abort the process. For that reason, we enforce this stack depth limit.
        private const int _stackOverflowLimiter = 100;
        private int _stackDepth = 0;

        private const string _joinShift = "     ";
        internal const string ParametersHashStart = "/* HASH ";
        internal const string ParametersHashEnd = " */";

        private string _cteMainSelect; // This is represents the CTE that is the main selector for use with includes
        private List<string> _includeCteIds;
        private Dictionary<string, List<string>> _includeLimitCtesByResourceType; // ctes of each include value, by their resource type

        // Include:iterate may be applied on results from multiple ctes
        private List<string> _includeFromCteIds;

        private int _tableExpressionCounter = -1;
        private int _smartv2ScopeUnionCTE = -1;
        private SqlRootExpression _rootExpression;
        private readonly SchemaInformation _schemaInfo;
        private bool _sortVisited = false;
        private bool _unionVisited = false;
        private bool _smartV2UnionVisited = false;
        private int _unionAggregateCTEIndex = -1; // the index of the CTE that aggregates all union results
        private bool _firstChainAfterUnionVisited = false;
        private HashSet<int> _cteToLimit = new HashSet<int>();
        private bool _hasIdentifier = false;
        private int _searchParamCount = 0;
        private bool previousSqlQueryGeneratorFailure = false;
        private int maxTableExpressionCountLimitForExists = 5;
        private bool _reuseQueryPlans;
        private bool _isAsyncOperation;
        private readonly HashSet<short> _searchParamIds = new();
        private readonly SearchParamTableExpressionQueryGeneratorFactory _queryGeneratorFactory;

        public SqlQueryGenerator(
            IndentedStringBuilder sb,
            HashingSqlQueryParameterManager parameters,
            ISqlServerFhirModel model,
            SchemaInformation schemaInfo,
            SearchParamTableExpressionQueryGeneratorFactory queryGeneratorFactory,
            bool reuseQueryPlans,
            bool isAsyncOperation,
            SqlException sqlException = null)
        {
            EnsureArg.IsNotNull(sb, nameof(sb));
            EnsureArg.IsNotNull(parameters, nameof(parameters));
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(schemaInfo, nameof(schemaInfo));
            EnsureArg.IsNotNull(queryGeneratorFactory, nameof(queryGeneratorFactory));

            StringBuilder = sb;
            Parameters = parameters;
            Model = model;
            _schemaInfo = schemaInfo;
            _queryGeneratorFactory = queryGeneratorFactory;
            _reuseQueryPlans = reuseQueryPlans;
            _isAsyncOperation = isAsyncOperation;

            if (sqlException?.Number == SqlErrorCodes.QueryProcessorNoQueryPlan)
            {
                previousSqlQueryGeneratorFailure = true;
            }
        }

        public HashSet<short> SearchParamIds => _searchParamIds;

        public IndentedStringBuilder StringBuilder { get; }

        public HashingSqlQueryParameterManager Parameters { get; }

        public ISqlServerFhirModel Model { get; }

        public override object VisitSqlRoot(SqlRootExpression expression, SearchOptions context)
        {
            if (!(context is SearchOptions searchOptions))
            {
                throw new ArgumentException($"Argument should be of type {nameof(SearchOptions)}", nameof(context));
            }

            _rootExpression = expression;
            SqlSearchOptions sqlSearchOptions = context as SqlSearchOptions;
            bool isVectorSearch = sqlSearchOptions?.PreparedVectorQuery != null;

            // Fail-closed invariant: when a SMART compartment membership context was attached for this search
            // (see SqlServerSearchService.AttachSmartCompartmentMembership), it must still be present on the
            // root expression that reaches SQL generation. SmartCompartmentMembership is carried outside the
            // visitable expression tree, so a rewrite step that reconstructs SqlRootExpression after the attach
            // would silently drop it — and the include CTEs would be generated without compartment
            // authorization. Refuse to generate that SQL. This cannot affect non-SMART or system-scope
            // searches: IsSmartCompartmentSearch is only set when a membership context was actually attached.
            if (context is SqlSearchOptions { IsSmartCompartmentSearch: true }
                && expression.SmartCompartmentMembership == null
                && expression.SearchParamTableExpressions.Any(t => t.Kind == SearchParamTableExpressionKind.Include))
            {
                throw new InvalidOperationException(
                    "SMART compartment membership context was dropped before SQL generation; refusing to generate _include/_revinclude SQL without compartment authorization.");
            }

            var visitedInclude = false;
            if (expression.SearchParamTableExpressions.Count > 0)
            {
                if (expression.ResourceTableExpressions.Count > 0)
                {
                    throw new InvalidOperationException("Expected no predicates on the Resource table because of the presence of TableExpressions");
                }

                // Union expressions must be executed first than all other expressions. The overral idea is that Union All expressions will
                // filter the highest group of records, and the following expressions will be executed on top of this group of records.
                // If include, split SQL into 2 parts: 1st filter and preserve data in filtered data table variable, and 2nd - use persisted data
                StringBuilder.Append("DECLARE @FilteredData AS TABLE (T1 smallint, Sid1 bigint, IsMatch bit, IsPartial bit, Row int");
                var isSortValueNeeded = IsSortValueNeeded(context);
                if (isSortValueNeeded)
                {
                    var sortContext = GetSortRelatedDetails(context);
                    var dbType = sortContext.SortColumnName.Metadata.SqlDbType;
                    var typeStr = dbType.ToString().ToLowerInvariant();
                    StringBuilder.Append($", SortValue {typeStr}");
                    if (dbType != System.Data.SqlDbType.DateTime2 && dbType != System.Data.SqlDbType.DateTime) // we support only date time and short string
                    {
                        StringBuilder.Append($"({sortContext.SortColumnName.Metadata.MaxLength})");
                    }
                }

                StringBuilder.AppendLine(")");
                bool hasIncludeExpressions = expression.SearchParamTableExpressions.Any(t => t.Kind == SearchParamTableExpressionKind.Include);
                bool hasSmartV2UnionExpressionInTheSet = expression.SearchParamTableExpressions.Any(t => t.HasSmartV2UnionExpression());

                // Find number of union expressions
                int numberOfUnionExpressions = expression.SearchParamTableExpressions.GetCountOfUnionAllExpressions();
                int smartV2TableCounter = 0;
                UnionExpression smartV2UnionExpression = null;
                SearchParamTableExpressionQueryGenerator smartV2QueryGenerator = null;
                StringBuilder.AppendLine(";WITH");
                StringBuilder.AppendDelimited($"{Environment.NewLine},", expression.SearchParamTableExpressions.SortExpressionsByQueryLogic(), (sb, tableExpression) =>
                {
                    if (tableExpression.SplitExpressions(out UnionExpression unionExpression, out SearchParamTableExpression allOtherRemainingExpressions))
                    {
                        numberOfUnionExpressions--;
                        if (tableExpression.HasSmartV2UnionExpression())
                        {
                            // Union expressions for smart v2 scopes with search parameters needs to be handled differently
                            smartV2TableCounter = _tableExpressionCounter;
                            smartV2UnionExpression = unionExpression;
                            smartV2QueryGenerator = tableExpression.QueryGenerator;

                            var parametersBeforeSmartScopesAreApplied = Parameters.ParametersToHash;
                            AppendSmartNewSetOfUnionAllTableExpressions(context, unionExpression, tableExpression.QueryGenerator, false);

                            if (hasIncludeExpressions)
                            {
                                // For include and revinclude searches we need to mark the parameters added during smart scope union as smart scope parameters
                                // As we are going to use these parameters to generate a hash for the include filtered data table
                                MarkNewParametersAsSmartScopeParameter(parametersBeforeSmartScopesAreApplied.ToHashSet());
                            }
                        }
                        else
                        {
                            AppendNewSetOfUnionAllTableExpressions(context, unionExpression, tableExpression.QueryGenerator);
                        }

                        // Keep building the sql the old way when there are other remaining expressions after the union all without smart v2 scopes with search parameters
                        if ((!hasSmartV2UnionExpressionInTheSet && allOtherRemainingExpressions != null) || (hasSmartV2UnionExpressionInTheSet && allOtherRemainingExpressions != null && numberOfUnionExpressions == 0))
                        {
                            StringBuilder.AppendLine(", ");
                            AppendNewTableExpression(sb, allOtherRemainingExpressions, ++_tableExpressionCounter, context);
                            _unionAggregateCTEIndex = _tableExpressionCounter;
                        }
                    }
                    else
                    {
                        // Look for include kind. Before going to include itself, add filtered data persistence.
                        if (!visitedInclude && tableExpression.Kind == SearchParamTableExpressionKind.Include)
                        {
                            sb.Remove(sb.Length - 1, 1); // remove last comma
                            AddParametersHash(); // hash is required in upper SQL
                            sb.AppendLine($"INSERT INTO @FilteredData SELECT T1, Sid1, IsMatch, IsPartial, Row{(isSortValueNeeded ? ", SortValue " : " ")}FROM cte{_tableExpressionCounter}");
                            AddOptionClause();

                            if (_smartV2UnionVisited)
                            {
                                // If we have smart v2 scopes with search parameters we need to re-generate the scope
                                // restricted data set for the include, because the
                                // include CTEs are emitted in a new ;WITH statement that cannot reference the CTEs above.
                                sb.AppendLine("OPTION (RECOMPILE)");
                                sb.AppendLine($";WITH");
                                int saveTableExpressionCounter = _tableExpressionCounter;
                                _tableExpressionCounter = smartV2TableCounter;
                                AppendSmartNewSetOfUnionAllTableExpressions(context, smartV2UnionExpression, smartV2QueryGenerator, true);
                                _tableExpressionCounter = saveTableExpressionCounter;
                                sb.AppendLine();
                                sb.AppendLine($",cte{_tableExpressionCounter} AS (SELECT * FROM @FilteredData)");
                                sb.Append(","); // add comma back
                            }
                            else
                            {
                                sb.AppendLine($";WITH cte{_tableExpressionCounter} AS (SELECT * FROM @FilteredData)");
                                sb.Append(","); // add comma back
                            }

                            visitedInclude = true;
                        }

                        AppendNewTableExpression(sb, tableExpression, ++_tableExpressionCounter, context);
                    }
                });

                StringBuilder.AppendLine();
            }

            if (!visitedInclude)
            {
                AddParametersHash(); // for include and rev-include we already added hash for all filtering conditions to the filter query
            }
            else if (visitedInclude && _smartV2UnionVisited)
            {
                AddParametersHash(true); // for include and rev-include with smart v2 scopes with search parameters add the hash
            }

            string resourceTableAlias = "r";
            bool selectingFromResourceTable;

            if (searchOptions.CountOnly)
            {
                if (isVectorSearch)
                {
                    selectingFromResourceTable = true;
                    StringBuilder.Append("SELECT count_big(DISTINCT ").Append(VLatest.Resource.ResourceSurrogateId, resourceTableAlias).AppendLine(")");
                }
                else if (expression.SearchParamTableExpressions.Count > 0)
                {
                    // The last CTE has all the surrogate IDs that match the results.
                    // We just need to count those and don't need to join with the Resource table
                    selectingFromResourceTable = false;
                    StringBuilder.AppendLine("SELECT count_big(DISTINCT Sid1)");
                }
                else
                {
                    // We will be counting over the Resource table.
                    selectingFromResourceTable = true;
                    StringBuilder.AppendLine("SELECT count_big(*)");
                }
            }
            else
            {
                selectingFromResourceTable = true;

                // When there are no SearchParamTableExpressions, we need TOP on the outer SELECT (after ORDER BY)
                // to ensure pagination works correctly. Previously TOP was in the inner subquery without ORDER BY,
                // causing SQL Server to return arbitrary rows before the outer ORDER BY reordered them.
                // Fix for pagination bug introduced in commit 6dd540c7d.
                if (expression.SearchParamTableExpressions.Count == 0 || isVectorSearch)
                {
                    StringBuilder.Append("SELECT TOP (").Append(Parameters.AddParameter(context.MaxItemCount + 1, includeInHash: false)).Append(") * FROM (");
                }
                else
                {
                    StringBuilder.Append("SELECT * FROM (");
                }

                // DISTINCT is used since different ctes may return the same resources due to _include and _include:iterate search parameters
                StringBuilder.Append("SELECT DISTINCT ");

                StringBuilder.Append(VLatest.Resource.ResourceTypeId, resourceTableAlias).Append(", ")
                    .Append(VLatest.Resource.ResourceId, resourceTableAlias).Append(", ")
                    .Append(VLatest.Resource.Version, resourceTableAlias).Append(", ")
                    .Append(VLatest.Resource.IsDeleted, resourceTableAlias).Append(", ")
                    .Append(VLatest.Resource.ResourceSurrogateId, resourceTableAlias).Append(", ")
                    .Append(VLatest.Resource.RequestMethod, resourceTableAlias).Append(", ");

                // If there's a table expression, use the previously selected bit, otherwise everything in the select is considered a match.
                // Vector search suppresses the Top CTE that carries IsMatch/IsPartial, and every ranked row is a match.
                bool selectMatchBitFromCte = expression.SearchParamTableExpressions.Count > 0 && !isVectorSearch;
                StringBuilder.Append(selectMatchBitFromCte ? "CAST(IsMatch AS bit) AS IsMatch, " : "CAST(1 AS bit) AS IsMatch, ");
                StringBuilder.Append(selectMatchBitFromCte ? "CAST(IsPartial AS bit) AS IsPartial, " : "CAST(0 AS bit) AS IsPartial, ");

                StringBuilder.Append(VLatest.Resource.IsRawResourceMetaSet, resourceTableAlias).Append(", ");

                if (_schemaInfo.Current >= SchemaVersionConstants.SearchParameterHashSchemaVersion)
                {
                    StringBuilder.Append(VLatest.Resource.SearchParamHash, resourceTableAlias).Append(", ");
                }

                StringBuilder.Append(VLatest.Resource.RawResource, resourceTableAlias);

                if (isVectorSearch)
                {
                    StringBuilder.Append(", semantic.SemanticDistance, semantic.SemanticChunkOrdinal, semantic.SemanticChunkText, semantic.SemanticSourceResourceTypeId, semantic.SemanticSourceResourceId, semantic.SemanticSourceResourceVersion, semantic.SemanticSourcePath, semantic.SemanticEvidenceJson");
                }

                if (IsSortValueNeeded(context) && !context.IsIncludesOperation)
                {
                    StringBuilder.Append(", ").Append(TableExpressionName(_tableExpressionCounter)).Append(".SortValue");
                }

                StringBuilder.AppendLine();
            }

            if (selectingFromResourceTable)
            {
                if (expression.SearchParamTableExpressions.Count == 0 &&
                    !context.ResourceVersionTypes.HasFlag(ResourceVersionType.History) &&
                    !context.ResourceVersionTypes.HasFlag(ResourceVersionType.SoftDeleted) &&
                    expression.ResourceTableExpressions.Any(e => e.AcceptVisitor(ExpressionContainsParameterVisitor.Instance, SearchParameterNames.ResourceType)) &&
                    !expression.ResourceTableExpressions.Any(e => e.AcceptVisitor(ExpressionContainsParameterVisitor.Instance, SearchParameterNames.Id)))
                {
                    StringBuilder.Append("FROM ").Append(VLatest.Resource).Append(" ").Append(resourceTableAlias);

                    // If this is a simple search over a resource type (like GET /Observation)
                    // make sure the optimizer does not decide to do a scan on the clustered index, since we have an index specifically for this common case
                    StringBuilder.Append(" WITH (INDEX(").Append(VLatest.Resource.IX_Resource_ResourceTypeId_ResourceSurrgateId).AppendLine("))");
                }
                else
                {
                    StringBuilder.Append("FROM ").Append(VLatest.Resource).Append(" ").AppendLine(resourceTableAlias);
                }

                if (expression.SearchParamTableExpressions.Count > 0)
                {
                    StringBuilder.Append(_joinShift).Append("JOIN ").Append(TableExpressionName(_tableExpressionCounter));
                    StringBuilder.Append(" ON ")
                        .Append(VLatest.Resource.ResourceTypeId, resourceTableAlias).Append(" = ").Append(TableExpressionName(_tableExpressionCounter)).Append(".T1 AND ")
                        .Append(VLatest.Resource.ResourceSurrogateId, resourceTableAlias).Append(" = ").Append(TableExpressionName(_tableExpressionCounter)).AppendLine(".Sid1");
                }

                if (isVectorSearch)
                {
                    AppendVectorSearchApply(sqlSearchOptions.PreparedVectorQuery, resourceTableAlias);
                }

                using (var delimitedClause = StringBuilder.BeginDelimitedWhereClause())
                {
                    foreach (var denormalizedPredicate in expression.ResourceTableExpressions)
                    {
                        delimitedClause.BeginDelimitedElement();
                        denormalizedPredicate.AcceptVisitor(ResourceTableSearchParameterQueryGenerator.Instance, GetContext());
                    }

                    AppendHistoryClause(delimitedClause, context.ResourceVersionTypes);

                    AppendDeletedClause(delimitedClause, context.ResourceVersionTypes);

                    if (isVectorSearch && sqlSearchOptions.SemanticContinuationDistance.HasValue)
                    {
                        object distanceParameter = Parameters.AddParameter(sqlSearchOptions.SemanticContinuationDistance.Value, includeInHash: false);
                        object resourceTypeParameter = Parameters.AddParameter(sqlSearchOptions.SemanticContinuationResourceTypeId.Value, includeInHash: false);
                        object surrogateIdParameter = Parameters.AddParameter(sqlSearchOptions.SemanticContinuationResourceSurrogateId.Value, includeInHash: false);

                        delimitedClause.BeginDelimitedElement();
                        StringBuilder
                            .Append("(semantic.SemanticDistance > ").Append(distanceParameter)
                            .Append(" OR (semantic.SemanticDistance = ").Append(distanceParameter).Append(" AND (")
                            .Append(VLatest.Resource.ResourceTypeId, resourceTableAlias).Append(" > ").Append(resourceTypeParameter)
                            .Append(" OR (").Append(VLatest.Resource.ResourceTypeId, resourceTableAlias).Append(" = ").Append(resourceTypeParameter)
                            .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, resourceTableAlias).Append(" > ").Append(surrogateIdParameter)
                            .Append("))))");
                    }
                }

                if (!searchOptions.CountOnly)
                {
                    var orderTableAlias = "t";
                    StringBuilder.Append(") AS ").Append(orderTableAlias).Append(" ORDER BY ");

                    var hasIncludes = _rootExpression.SearchParamTableExpressions.Any(t => t.Kind == SearchParamTableExpressionKind.Include);

                    if (hasIncludes)
                    {
                        // ensure the matches appear before includes
                        StringBuilder.Append("IsMatch DESC, ");
                    }

                    if (isVectorSearch && (searchOptions.Sort.Count == 0 || IsScoreSort(searchOptions)))
                    {
                        StringBuilder
                            .Append("SemanticDistance ASC, ")
                            .Append(VLatest.Resource.ResourceTypeId, orderTableAlias).Append(" ASC, ")
                            .Append(VLatest.Resource.ResourceSurrogateId, orderTableAlias).AppendLine(" ASC ");
                    }
                    else if (IsPrimaryKeySort(searchOptions))
                    {
                        StringBuilder.AppendDelimited(", ", searchOptions.Sort, (sb, sort) =>
                        {
                            Column column = sort.searchParameterInfo.Name switch
                            {
                                SearchParameterNames.ResourceType => VLatest.Resource.ResourceTypeId,
                                SearchParameterNames.LastUpdated => VLatest.Resource.ResourceSurrogateId,
                                _ => throw new InvalidOperationException($"Unexpected sort parameter {sort.searchParameterInfo.Name}"),
                            };

                            if (hasIncludes)
                            {
                                // when includes are present, we want to ensure that only matches sorted by the sort field
                                sb.Append("(CASE WHEN IsMatch = 1 THEN ");
                                sb.Append(column, orderTableAlias);
                                sb.Append(" ELSE NULL END) ");
                            }
                            else
                            {
                                sb.Append(column, orderTableAlias).Append(" ");
                            }

                            sb.Append(sort.sortOrder == SortOrder.Ascending ? "ASC" : "DESC");
                        });

                        if (hasIncludes)
                        {
                            StringBuilder.Append(", (CASE WHEN IsMatch = 0 THEN ").Append(VLatest.Resource.ResourceTypeId, orderTableAlias).Append(" ELSE NULL END) ASC, ");
                            StringBuilder.Append("(CASE WHEN IsMatch = 0 THEN ").Append(VLatest.Resource.ResourceSurrogateId, orderTableAlias).Append(" ELSE NULL END) ASC ");
                        }

                        StringBuilder.AppendLine();
                    }
                    else if (IsSortValueNeeded(searchOptions) && !context.IsIncludesOperation)
                    {
                        if (hasIncludes)
                        {
                            StringBuilder
                                .Append("(CASE WHEN IsMatch = 1 THEN ")
                                .Append(orderTableAlias)
                                .Append(".SortValue ELSE NULL END) ");
                        }
                        else
                        {
                            StringBuilder
                                .Append(orderTableAlias)
                                .Append(".SortValue ");
                        }

                        StringBuilder
                            .Append(searchOptions.Sort[0].sortOrder == SortOrder.Ascending ? "ASC" : "DESC").Append(", ")
                            .Append(VLatest.Resource.ResourceTypeId, orderTableAlias).Append(" ASC, ")
                            .Append(VLatest.Resource.ResourceSurrogateId, orderTableAlias).AppendLine(" ASC ");
                    }
                    else
                    {
                        StringBuilder
                            .Append(VLatest.Resource.ResourceTypeId, orderTableAlias).Append(" ASC, ")
                            .Append(VLatest.Resource.ResourceSurrogateId, orderTableAlias).AppendLine(" ASC ");
                    }

                    AddOptionClause();
                }
            }
            else
            {
                // this is selecting only from the last CTE (for a count)
                StringBuilder.Append("FROM ").AppendLine(TableExpressionName(_tableExpressionCounter));
            }

            return null;
        }

        private void AppendVectorSearchApply(PreparedVectorSearchQuery preparedQuery, string resourceTableAlias)
        {
            const string vectorTableAlias = "v";
            const string evidenceTableAlias = "ev";
            const string referenceTableAlias = "semanticReference";
            const string witnessTableAlias = "semanticWitness";
            PreparedVectorSearchChainLink chainLink = null;
            if (preparedQuery.ChainLinks.Count > 0)
            {
                if (preparedQuery.ChainLinks.Count != 1)
                {
                    throw new InvalidSearchOperationException("Semantic search currently supports one chain relationship.");
                }

                chainLink = preparedQuery.ChainLinks[0];
            }

            short searchParamId = Model.GetSearchParamId(preparedQuery.SearchParameter.Url);
            _searchParamIds.Add(searchParamId);
            object distanceMetricParameter = Parameters.AddParameter(VectorSearchConfiguration.SupportedDistanceMetric, includeInHash: false);
            object queryEmbeddingParameter = Parameters.AddParameter(SqlVectorFormatter.Format(preparedQuery.Embedding), includeInHash: false);
            object maximumDistanceParameter = Parameters.AddParameter(2 * (1 - preparedQuery.MinimumScore), includeInHash: false);

            StringBuilder.AppendLine("     CROSS APPLY")
                .AppendLine("     (")
                .Append("         SELECT TOP (1) VECTOR_DISTANCE(").Append(distanceMetricParameter).Append(", ")
                .Append(vectorTableAlias).Append('.').Append(VLatest.VectorSearchParamTable.Embedding).Append(", CAST(")
                .Append(queryEmbeddingParameter).Append(" AS VECTOR(").Append(VectorSearchConfiguration.SupportedDimensions).AppendLine("))) AS SemanticDistance,")
                .Append("             ").Append(VLatest.VectorSearchParam.ChunkOrdinal, vectorTableAlias).AppendLine(" AS SemanticChunkOrdinal,")
                .Append("             ").Append(VLatest.VectorSearchParam.ChunkText, vectorTableAlias).AppendLine(" AS SemanticChunkText,")
                .Append("             ").Append(VLatest.VectorSearchParam.SourceResourceTypeId, vectorTableAlias).AppendLine(" AS SemanticSourceResourceTypeId,")
                .Append("             ").Append(VLatest.VectorSearchParam.SourceResourceId, vectorTableAlias).AppendLine(" AS SemanticSourceResourceId,")
                .Append("             ").Append(VLatest.VectorSearchParam.SourceResourceVersion, vectorTableAlias).AppendLine(" AS SemanticSourceResourceVersion,")
                .Append("             ").Append(VLatest.VectorSearchParam.SourcePath, vectorTableAlias).AppendLine(" AS SemanticSourcePath,")
                .AppendLine("             (")
                .AppendLine("                 SELECT")
                .Append("                     ").Append(VLatest.VectorSearchParam.ChunkOrdinal, evidenceTableAlias).AppendLine(" AS chunkOrdinal,")
                .Append("                     ").Append(VLatest.VectorSearchParam.ChunkText, evidenceTableAlias).AppendLine(" AS text,")
                .Append("                     VECTOR_DISTANCE(").Append(distanceMetricParameter).Append(", ")
                .Append(evidenceTableAlias).Append('.').Append(VLatest.VectorSearchParamTable.Embedding).Append(", CAST(")
                .Append(queryEmbeddingParameter).Append(" AS VECTOR(").Append(VectorSearchConfiguration.SupportedDimensions).AppendLine("))) AS distance,")
                .Append("                     ").Append(VLatest.VectorSearchParam.SourceResourceTypeId, evidenceTableAlias).AppendLine(" AS sourceResourceTypeId,")
                .Append("                     ").Append(VLatest.VectorSearchParam.SourceResourceId, evidenceTableAlias).AppendLine(" AS sourceResourceId,")
                .Append("                     ").Append(VLatest.VectorSearchParam.SourceResourceVersion, evidenceTableAlias).AppendLine(" AS sourceResourceVersion,")
                .Append("                     ").Append(VLatest.VectorSearchParam.SourcePath, evidenceTableAlias).Append(" AS sourcePath");

            if (chainLink != null)
            {
                StringBuilder
                    .AppendLine(",")
                    .Append("                     ").Append(VLatest.Resource.ResourceTypeId, witnessTableAlias).AppendLine(" AS witnessResourceTypeId,")
                    .Append("                     ").Append(VLatest.Resource.ResourceId, witnessTableAlias).AppendLine(" AS witnessResourceId,")
                    .Append("                     ").Append(VLatest.Resource.Version, witnessTableAlias).AppendLine(" AS witnessResourceVersion");
            }
            else
            {
                StringBuilder.AppendLine();
            }

            StringBuilder
                .Append("                 FROM ").Append(VLatest.VectorSearchParam).Append(" AS ").AppendLine(evidenceTableAlias)
                .Append("                 WHERE ").Append(VLatest.VectorSearchParam.ResourceTypeId, evidenceTableAlias).Append(" = ").Append(VLatest.VectorSearchParam.ResourceTypeId, vectorTableAlias).AppendLine()
                .Append("                   AND ").Append(VLatest.VectorSearchParam.ResourceSurrogateId, evidenceTableAlias).Append(" = ").Append(VLatest.VectorSearchParam.ResourceSurrogateId, vectorTableAlias).AppendLine()
                .Append("                   AND ").Append(VLatest.VectorSearchParam.SearchParamId, evidenceTableAlias).Append(" = ").Append(Parameters.AddParameter(VLatest.VectorSearchParam.SearchParamId, searchParamId, includeInHash: true)).AppendLine()
                .Append("                   AND ").Append(VLatest.VectorSearchParam.EmbeddingModelId, evidenceTableAlias).Append(" = ").Append(Parameters.AddParameter(VLatest.VectorSearchParam.EmbeddingModelId, preparedQuery.EmbeddingModelId, includeInHash: false)).AppendLine()
                .Append("                   AND VECTOR_DISTANCE(").Append(distanceMetricParameter).Append(", ")
                .Append(evidenceTableAlias).Append('.').Append(VLatest.VectorSearchParamTable.Embedding).Append(", CAST(")
                .Append(queryEmbeddingParameter).Append(" AS VECTOR(").Append(VectorSearchConfiguration.SupportedDimensions).Append("))) <= ").Append(maximumDistanceParameter).AppendLine()
                .Append("                 ORDER BY VECTOR_DISTANCE(").Append(distanceMetricParameter).Append(", ")
                .Append(evidenceTableAlias).Append('.').Append(VLatest.VectorSearchParamTable.Embedding).Append(", CAST(")
                .Append(queryEmbeddingParameter).Append(" AS VECTOR(").Append(VectorSearchConfiguration.SupportedDimensions).Append("))), ")
                .Append(VLatest.VectorSearchParam.ChunkOrdinal, evidenceTableAlias).AppendLine(" ASC")
                .AppendLine("                 FOR JSON PATH")
                .AppendLine("             ) AS SemanticEvidenceJson");

            if (chainLink == null)
            {
                StringBuilder
                    .Append("         FROM ").Append(VLatest.VectorSearchParam).Append(" AS ").AppendLine(vectorTableAlias)
                    .Append("         WHERE ").Append(VLatest.VectorSearchParam.ResourceTypeId, vectorTableAlias).Append(" = ").Append(VLatest.Resource.ResourceTypeId, resourceTableAlias).AppendLine()
                    .Append("           AND ").Append(VLatest.VectorSearchParam.ResourceSurrogateId, vectorTableAlias).Append(" = ").Append(VLatest.Resource.ResourceSurrogateId, resourceTableAlias).AppendLine()
                    .Append("           AND ");
            }
            else
            {
                short referenceSearchParamId = Model.GetSearchParamId(chainLink.ReferenceSearchParameter.Url);
                _searchParamIds.Add(referenceSearchParamId);
                StringBuilder.Append("         FROM ").Append(VLatest.ReferenceSearchParam).Append(" AS ").AppendLine(referenceTableAlias);

                if (chainLink.Reversed)
                {
                    StringBuilder
                        .Append("         JOIN ").Append(VLatest.Resource).Append(" AS ").Append(witnessTableAlias)
                        .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, witnessTableAlias).Append(" = ").Append(VLatest.ReferenceSearchParam.ResourceTypeId, referenceTableAlias)
                        .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, witnessTableAlias).Append(" = ").AppendLine(VLatest.ReferenceSearchParam.ResourceSurrogateId, referenceTableAlias);
                }
                else
                {
                    StringBuilder
                        .Append("         JOIN ").Append(VLatest.Resource).Append(" AS ").Append(witnessTableAlias)
                        .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, witnessTableAlias).Append(" = ").Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceTableAlias)
                        .Append(" AND ").Append(VLatest.Resource.ResourceId, witnessTableAlias).Append(" = ").AppendLine(VLatest.ReferenceSearchParam.ReferenceResourceId, referenceTableAlias);
                }

                StringBuilder
                    .Append("         JOIN ").Append(VLatest.VectorSearchParam).Append(" AS ").Append(vectorTableAlias)
                    .Append(" ON ").Append(VLatest.VectorSearchParam.ResourceTypeId, vectorTableAlias).Append(" = ").Append(VLatest.Resource.ResourceTypeId, witnessTableAlias)
                    .Append(" AND ").Append(VLatest.VectorSearchParam.ResourceSurrogateId, vectorTableAlias).Append(" = ").AppendLine(VLatest.Resource.ResourceSurrogateId, witnessTableAlias)
                    .Append("         WHERE ").Append(VLatest.ReferenceSearchParam.SearchParamId, referenceTableAlias).Append(" = ").Append(Parameters.AddParameter(VLatest.ReferenceSearchParam.SearchParamId, referenceSearchParamId, includeInHash: true)).AppendLine()
                    .Append("           AND ").Append(VLatest.ReferenceSearchParam.ResourceTypeId, referenceTableAlias).Append(" IN (")
                    .Append(string.Join(", ", chainLink.ResourceTypes.Select(resourceType => Parameters.AddParameter(VLatest.ReferenceSearchParam.ResourceTypeId, Model.GetResourceTypeId(resourceType), includeInHash: true)))).AppendLine(")");

                if (chainLink.Reversed)
                {
                    StringBuilder
                        .Append("           AND ").Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceTableAlias).Append(" = ").Append(VLatest.Resource.ResourceTypeId, resourceTableAlias).AppendLine()
                        .Append("           AND ").Append(VLatest.ReferenceSearchParam.ReferenceResourceId, referenceTableAlias).Append(" = ").Append(VLatest.Resource.ResourceId, resourceTableAlias).AppendLine();
                }
                else
                {
                    StringBuilder
                        .Append("           AND ").Append(VLatest.ReferenceSearchParam.ResourceTypeId, referenceTableAlias).Append(" = ").Append(VLatest.Resource.ResourceTypeId, resourceTableAlias).AppendLine()
                        .Append("           AND ").Append(VLatest.ReferenceSearchParam.ResourceSurrogateId, referenceTableAlias).Append(" = ").Append(VLatest.Resource.ResourceSurrogateId, resourceTableAlias).AppendLine();
                }

                IReadOnlyList<string> witnessResourceTypes = chainLink.Reversed
                    ? chainLink.ResourceTypes
                    : chainLink.TargetResourceTypes;
                StringBuilder
                    .Append("           AND ").Append(VLatest.Resource.ResourceTypeId, witnessTableAlias).Append(" IN (")
                    .Append(string.Join(", ", witnessResourceTypes.Select(resourceType => Parameters.AddParameter(VLatest.Resource.ResourceTypeId, Model.GetResourceTypeId(resourceType), includeInHash: true)))).AppendLine(")")
                    .Append("           AND ").Append(VLatest.Resource.IsHistory, witnessTableAlias).AppendLine(" = 0")
                    .Append("           AND ").Append(VLatest.Resource.IsDeleted, witnessTableAlias).AppendLine(" = 0")
                    .Append("           AND ");
            }

            StringBuilder
                .Append(VLatest.VectorSearchParam.SearchParamId, vectorTableAlias).Append(" = ").Append(Parameters.AddParameter(VLatest.VectorSearchParam.SearchParamId, searchParamId, includeInHash: true)).AppendLine()
                .Append("           AND ").Append(VLatest.VectorSearchParam.EmbeddingModelId, vectorTableAlias).Append(" = ").Append(Parameters.AddParameter(VLatest.VectorSearchParam.EmbeddingModelId, preparedQuery.EmbeddingModelId, includeInHash: false)).AppendLine()
                .Append("           AND VECTOR_DISTANCE(").Append(distanceMetricParameter).Append(", ")
                .Append(vectorTableAlias).Append('.').Append(VLatest.VectorSearchParamTable.Embedding).Append(", CAST(")
                .Append(queryEmbeddingParameter).Append(" AS VECTOR(").Append(VectorSearchConfiguration.SupportedDimensions).Append("))) <= ").Append(maximumDistanceParameter).AppendLine()
                .Append("         ORDER BY VECTOR_DISTANCE(").Append(distanceMetricParameter).Append(", ")
                .Append(vectorTableAlias).Append('.').Append(VLatest.VectorSearchParamTable.Embedding).Append(", CAST(")
                .Append(queryEmbeddingParameter).Append(" AS VECTOR(").Append(VectorSearchConfiguration.SupportedDimensions).Append("))), ");

            if (chainLink != null)
            {
                StringBuilder
                    .Append(VLatest.VectorSearchParam.ResourceTypeId, vectorTableAlias).Append(" ASC, ")
                    .Append(VLatest.VectorSearchParam.ResourceSurrogateId, vectorTableAlias).Append(" ASC, ");
            }

            StringBuilder
                .Append(VLatest.VectorSearchParam.ChunkOrdinal, vectorTableAlias).AppendLine(" ASC")
                .AppendLine("     ) semantic");
        }

        // TODO: Remove when code starts using TokenSearchParamHighCard table
        private void AddOptionClause()
        {
            // if we have a complex query more than one SearchParemter, one of the parameters is "identifier", and we have an include
            // then we will tell SQL to ignore the parameter values and base the query plan one the
            // statistics only.  We have seen SQL make poor choices in this instance, so we are making a special case here
            if (AddOptimizeForUnknownClause())
            {
                StringBuilder.AppendLine("OPTION (OPTIMIZE FOR UNKNOWN)");
            }
        }

        private void AddParametersHash(bool forSmartV2Include = false)
        {
            foreach (var searchParamId in Parameters.SearchParamIds)
            {
                _searchParamIds.Add(searchParamId);
            }

            if (Parameters.HasParametersToHash && !_reuseQueryPlans) // hash cannot be last comment as it will not be stored in query store
            {
                // Add a hash of (most of the) parameter values as a comment.
                // We do this to avoid re-using query plans unless two queries have
                // the same parameter values. We currently exclude from the hash parameters
                // that are related to TOP clauses or continuation tokens.
                // We can exclude more in the future.

                StringBuilder.Append(ParametersHashStart);
                if (forSmartV2Include)
                {
                    // Only add the hash for smart scope parameters
                    Parameters.AppendSmartScopeHash(StringBuilder);
                    Parameters.AppendSmartScopeParameterNames(StringBuilder);
                }
                else
                {
                    Parameters.AppendHash(StringBuilder);
                    Parameters.AppendHashedParameterNames(StringBuilder);
                }

                StringBuilder.Append(ParametersHashEnd);
            }

            StringBuilder.AppendLine(); // do not include EOL into parameters hash line to get same behavior on Windows and Linux
        }

        /// <summary>
        /// Marks parameters that were added after a specific point in time as SMART scope parameters.
        /// </summary>
        /// <param name="parametersBefore">The set of parameters that existed before the operation.</param>
        /// <returns>List of new parameters that were added and marked as SMART scope parameters.</returns>
        private List<SqlParameter> MarkNewParametersAsSmartScopeParameter(HashSet<SqlParameter> parametersBefore)
        {
            var parametersAfter = new HashSet<SqlParameter>(Parameters.ParametersToHash);
            var newParameters = parametersAfter.Except(parametersBefore).ToList();

            if (newParameters.Any())
            {
                foreach (var param in newParameters)
                {
                    Parameters.MarkAsSmartScopeParameter(param);
                }
            }

            return newParameters;
        }

        private static string TableExpressionName(int id) => "cte" + id;

        private bool IsInSortMode(SearchOptions context) => context.Sort != null && context.Sort.Count > 0 && _sortVisited;

        public override object VisitTable(SearchParamTableExpression searchParamTableExpression, SearchOptions context)
        {
            try
            {
                _stackDepth++;
                if (_stackDepth > _stackOverflowLimiter)
                {
                    throw new SearchParameterTooComplexException();
                }

                const string referenceSourceTableAlias = "refSource";
                const string referenceTargetResourceTableAlias = "refTarget";

                switch (searchParamTableExpression.Kind)
                {
                    case SearchParamTableExpressionKind.Normal:
                        HandleTableKindNormal(searchParamTableExpression, context);
                        break;

                    case SearchParamTableExpressionKind.Concatenation:
                        StringBuilder.Append("SELECT * FROM ").AppendLine(TableExpressionName(_tableExpressionCounter - 1));
                        StringBuilder.AppendLine("UNION ALL");

                        goto case SearchParamTableExpressionKind.Normal;

                    case SearchParamTableExpressionKind.All:
                        HandleTableKindAll(searchParamTableExpression, context);
                        break;

                    case SearchParamTableExpressionKind.NotExists:
                        HandleTableKindNotExists(searchParamTableExpression, context);
                        break;

                    case SearchParamTableExpressionKind.Top:
                        HandleTableKindTop(context);
                        break;

                    case SearchParamTableExpressionKind.Chain:
                        HandleTableKindChain(searchParamTableExpression, context, referenceSourceTableAlias, referenceTargetResourceTableAlias);
                        break;

                    case SearchParamTableExpressionKind.Include:
                        HandleTableKindInclude(searchParamTableExpression, context, referenceSourceTableAlias, referenceTargetResourceTableAlias);
                        break;

                    case SearchParamTableExpressionKind.IncludeLimit:
                        HandleTableKindIncludeLimit(context);
                        break;

                    case SearchParamTableExpressionKind.IncludeUnionAll:
                        HandleTableKindIncludeUnionAll(context);
                        break;

                    case SearchParamTableExpressionKind.Sort:
                        HandleTableKindSort(searchParamTableExpression, context);
                        break;

                    case SearchParamTableExpressionKind.SortWithFilter:
                        HandleTableKindSortWithFilter(searchParamTableExpression, context);
                        break;

                    case SearchParamTableExpressionKind.Union:
                        HandleParamTableUnion(searchParamTableExpression, context);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(searchParamTableExpression.Kind.ToString());
                }
            }
            finally
            {
                _stackDepth--;
            }

            return null;
        }

        private void HandleParamTableUnion(SearchParamTableExpression searchParamTableExpression, SearchOptions context)
        {
            var specialCaseTableName = searchParamTableExpression.QueryGenerator.Table;
            StringBuilder.Append(TableExpressionName(++_tableExpressionCounter)).AppendLine(" AS").AppendLine("(");

            using (StringBuilder.Indent())
            {
                StringBuilder.Append("SELECT ")
                    .Append(VLatest.Resource.ResourceTypeId, null).Append(" AS T1, ")
                    .Append(VLatest.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid1");

                var searchParameterExpressionPredicate = searchParamTableExpression.Predicate as SearchParameterExpression;

                // handle special case where we want to Union a specific resource to the results
                if (searchParameterExpressionPredicate != null &&
                    searchParameterExpressionPredicate.Parameter.ColumnLocation().HasFlag(SearchParameterColumnLocation.ResourceTable))
                {
                    specialCaseTableName = VLatest.Resource;
                    StringBuilder.Append("FROM ").AppendLine(specialCaseTableName);
                }
                else
                {
                    // For Smart union expression, searchParamTableExpression.Predicate could be a multiary expression and not SearchParameterExpression
                    // To retrieve the main compartment resource we are building the Multiary expression with ResourceTypeId AND ResourceId (SearchParameterExpression)
                    // Check if its a Multiary expression, if yes then check the internal expressions are SearchParameterExpression of parameter _type and _id
                    // If yes then we can set the specialCaseTableName to Resource table and not to searchParamTableExpression.QueryGenerator.Table which will mostly be a ReferenceSearchParamTable
                    if (searchParamTableExpression.Predicate is MultiaryExpression multiaryExpression)
                    {
                        bool allAreResourceTypeOrId = multiaryExpression.Expressions.All(e =>
                            e is SearchParameterExpression spe &&
                            (spe.Parameter.Name == SearchParameterNames.ResourceType || spe.Parameter.Name == SearchParameterNames.Id));

                        if (allAreResourceTypeOrId)
                        {
                            specialCaseTableName = VLatest.Resource;
                        }
                    }

                    StringBuilder.Append("FROM ").AppendLine(specialCaseTableName);
                }

                using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                {
                    // Apply History and Delete clause when querying from Resource table in case of compartment unions
                    AppendHistoryClause(delimited, context.ResourceVersionTypes, searchParamTableExpression, null, specialCaseTableName);

                    if (specialCaseTableName.Equals(VLatest.Resource))
                    {
                        AppendDeletedClause(delimited, context.ResourceVersionTypes);
                    }

                    if (searchParamTableExpression.Predicate != null && !(searchParamTableExpression.Predicate is CompartmentSearchExpression))
                    {
                        delimited.BeginDelimitedElement();
                        searchParamTableExpression.Predicate.AcceptVisitor(searchParamTableExpression.QueryGenerator, GetContext());
                    }
                }
            }

            StringBuilder.AppendLine("),");
        }

        private void HandleTableKindNormal(SearchParamTableExpression searchParamTableExpression, SearchOptions context)
        {
            var specialCaseTableName = searchParamTableExpression.QueryGenerator.Table;

            if (searchParamTableExpression.ChainLevel == 0)
            {
                int predecessorIndex = FindRestrictingPredecessorTableExpressionIndex();

                // if this is not sort mode or if it is the first cte
                if (!IsInSortMode(context) || predecessorIndex < 0)
                {
                    StringBuilder.Append("SELECT ")
                        .Append(VLatest.Resource.ResourceTypeId, null).Append(" AS T1, ")
                        .Append(VLatest.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid1")
                        .Append("FROM ").AppendLine(searchParamTableExpression.QueryGenerator.Table);
                }
                else
                {
                    // we are in sort mode and we need to join with previous cte to propagate the SortValue
                    var cte = TableExpressionName(predecessorIndex);
                    StringBuilder.Append("SELECT ")
                        .Append(VLatest.Resource.ResourceTypeId, null).Append(" AS T1, ")
                        .Append(VLatest.Resource.ResourceSurrogateId, null).Append(" AS Sid1, ")
                        .Append(cte).AppendLine(".SortValue")
                        .Append("FROM ").AppendLine(searchParamTableExpression.QueryGenerator.Table)
                        .Append(_joinShift).Append("JOIN ").Append(cte)
                        .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, null).Append(" = ").Append(cte).Append(".T1")
                        .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, null).Append(" = ").Append(cte).AppendLine(".Sid1");
                }
            }
            else if (searchParamTableExpression.ChainLevel == 1 && _unionVisited)
            {
                // handle special case where we want to Union a specific resource to the results
                var searchParameterExpressionPredicate = CheckExpressionOrFirstChildIsSearchParam(searchParamTableExpression.Predicate);
                if (searchParameterExpressionPredicate != null &&
                    searchParameterExpressionPredicate.Parameter.ColumnLocation().HasFlag(SearchParameterColumnLocation.ResourceTable))
                {
                    specialCaseTableName = new VLatest.ResourceTable();
                }

                StringBuilder.Append("SELECT T1, Sid1, ")
                    .Append(VLatest.Resource.ResourceTypeId, null).Append(" AS T2, ")
                    .Append(VLatest.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid2")
                    .Append("FROM ").AppendLine(specialCaseTableName)
                    .Append(_joinShift).Append("JOIN ").Append(TableExpressionName(FindRestrictingPredecessorTableExpressionIndex()))
                    .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, null).Append(" = ").Append(_firstChainAfterUnionVisited ? "T2" : "T1")
                    .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, null).Append(" = ").AppendLine(_firstChainAfterUnionVisited ? "Sid2" : "Sid1");

                // once we have visited a table after the union all, the remained of the inner joins
                // should be on T1 and Sid1
                _firstChainAfterUnionVisited = true;
            }
            else
            {
                StringBuilder.Append("SELECT T1, Sid1, ")
                    .Append(VLatest.Resource.ResourceTypeId, null).Append(" AS T2, ")
                    .Append(VLatest.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid2")
                    .Append("FROM ").AppendLine(searchParamTableExpression.QueryGenerator.Table)
                    .Append(_joinShift).Append("JOIN ").Append(TableExpressionName(FindRestrictingPredecessorTableExpressionIndex()))
                    .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, null).Append(" = ").Append("T2")
                    .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, null).Append(" = ").AppendLine("Sid2");
            }

            if (UseAppendWithJoin()
                && searchParamTableExpression.ChainLevel == 0 && !IsInSortMode(context) && !context.SkipAppendIntersectionWithPredecessor)
            {
                AppendIntersectionWithPredecessorUsingInnerJoin(StringBuilder, searchParamTableExpression);
            }

            using (var delimited = StringBuilder.BeginDelimitedWhereClause())
            {
                AppendHistoryClause(delimited, context.ResourceVersionTypes, searchParamTableExpression, null, specialCaseTableName);

                // For smart request when we have union of all scopes ANDed with their respective search parameters
                // Like (ResourceType = x and searchParam1 = foo) Intersect (ResourceType = x and searchParam2 = doo) UNION (ResourceType = y and searchParam3 = goo) Intersect (ResourceType = y and searchParam4 = woo)
                // To get the intersection we need to AppendIntersectionWithPredecessor
                if (searchParamTableExpression.ChainLevel == 0 && !IsInSortMode(context) && !UseAppendWithJoin())
                {
                    if (!context.SkipAppendIntersectionWithPredecessor)
                    {
                        // if chainLevel > 0 or if in sort mode or if we need to simplify the query, the intersection is already handled in a JOIN
                        AppendIntersectionWithPredecessor(delimited, searchParamTableExpression);
                    }
                }

                if (searchParamTableExpression.Predicate != null)
                {
                    delimited.BeginDelimitedElement();
                    CheckForIdentifierSearchParams(searchParamTableExpression.Predicate);
                    searchParamTableExpression.Predicate.AcceptVisitor(searchParamTableExpression.QueryGenerator, GetContext());
                }
            }
        }

        private void HandleTableKindAll(SearchParamTableExpression searchParamTableExpression, SearchOptions context)
        {
            int predecessorIndex = FindRestrictingPredecessorTableExpressionIndex();

            // In the case the query contains a UNION operator, the following CTE must join the latest Union CTE
            // where all data is aggregated.
            if (_unionVisited && predecessorIndex > 0 && searchParamTableExpression.ChainLevel == 0)
            {
                var cte = TableExpressionName(predecessorIndex);
                StringBuilder.Append("SELECT ")
                    .Append(VLatest.Resource.ResourceTypeId, null).Append(" AS T1, ")
                    .Append(VLatest.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid1") // SELECT and FROM can be on same line only for singe line statements
                    .Append("FROM ").AppendLine(VLatest.Resource)
                    .Append(_joinShift).Append("JOIN ").Append(cte)
                    .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, null).Append(" = ").Append(cte).Append(".T1")
                    .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, null).Append(" = ").Append(cte).AppendLine(".Sid1");

                using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                {
                    AppendHistoryClause(delimited, context.ResourceVersionTypes);
                    AppendDeletedClause(delimited, context.ResourceVersionTypes);
                    if (searchParamTableExpression.Predicate != null)
                    {
                        delimited.BeginDelimitedElement();
                        searchParamTableExpression.Predicate.AcceptVisitor(ResourceTableSearchParameterQueryGenerator.Instance, GetContext());
                    }
                }
            }
            else
            {
                StringBuilder.Append("SELECT ")
                    .Append(VLatest.Resource.ResourceTypeId, null).Append(" AS T1, ")
                    .Append(VLatest.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid1")
                    .Append("FROM ").AppendLine(VLatest.Resource);

                using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                {
                    AppendHistoryClause(delimited, context.ResourceVersionTypes);
                    AppendDeletedClause(delimited, context.ResourceVersionTypes);
                    if (searchParamTableExpression.Predicate != null)
                    {
                        delimited.BeginDelimitedElement();
                        searchParamTableExpression.Predicate.AcceptVisitor(ResourceTableSearchParameterQueryGenerator.Instance, GetContext());
                    }
                }
            }
        }

        private void HandleTableKindNotExists(SearchParamTableExpression searchParamTableExpression, SearchOptions context)
        {
            StringBuilder.Append("SELECT T1, Sid1");
            StringBuilder.AppendLine(IsInSortMode(context) ? ", SortValue" : string.Empty);
            StringBuilder.Append("FROM ").AppendLine(TableExpressionName(_tableExpressionCounter - 1));
            StringBuilder.AppendLine("WHERE Sid1 NOT IN").AppendLine("(");

            using (StringBuilder.Indent())
            {
                StringBuilder.Append("SELECT ").AppendLine(VLatest.Resource.ResourceSurrogateId, null)
                    .Append("FROM ").AppendLine(searchParamTableExpression.QueryGenerator.Table);
                using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                {
                    AppendHistoryClause(delimited, context.ResourceVersionTypes, searchParamTableExpression);

                    delimited.BeginDelimitedElement();
                    searchParamTableExpression.Predicate.AcceptVisitor(searchParamTableExpression.QueryGenerator, GetContext());
                }
            }

            StringBuilder.AppendLine(")");
        }

        private void HandleTableKindTop(SearchOptions context)
        {
            var tableExpressionName = TableExpressionName(_tableExpressionCounter - 1);
            var sortExpression = IsSortValueNeeded(context) ? $"{tableExpressionName}.SortValue" : null;

            bool hasIncludeExpression = _rootExpression.SearchParamTableExpressions.Any(t => t.Kind == SearchParamTableExpressionKind.Include);

            IndentedStringBuilder.IndentedScope indentedScope = default;
            if (hasIncludeExpression)
            {
                // a subsequent _include will need to join with the top context.MaxItemCount of this resultset, so we include a Row column
                StringBuilder.Append("SELECT row_number() OVER (");
                AppendOrderBy();
                StringBuilder.AppendLine(") AS Row, *")
                    .AppendLine("FROM")
                    .AppendLine("(");

                indentedScope = StringBuilder.Indent();
            }

            // Everything in the top expression is considered a match
            const string selectStatement = "SELECT DISTINCT";
            StringBuilder.Append(selectStatement).Append(" TOP (").Append(Parameters.AddParameter(context.MaxItemCount + 1, includeInHash: false)).Append(") T1, Sid1, 1 AS IsMatch, 0 AS IsPartial ")
                .AppendLine(sortExpression == null ? string.Empty : $", {sortExpression}")
                .Append("FROM ").AppendLine(tableExpressionName);

            AppendOrderBy();
            StringBuilder.AppendLine();

            if (hasIncludeExpression)
            {
                indentedScope.Dispose();
                StringBuilder.AppendLine(") t");
            }

            // For any includes, the source of the resource surrogate ids to join on is saved
            _cteMainSelect = TableExpressionName(_tableExpressionCounter);

            void AppendOrderBy()
            {
                StringBuilder.Append("ORDER BY ");
                if (IsPrimaryKeySort(context))
                {
                    StringBuilder.AppendDelimited(", ", context.Sort, (sb, sort) =>
                    {
                        string column = sort.searchParameterInfo.Name switch
                        {
                            SearchParameterNames.ResourceType => "T1",
                            SearchParameterNames.LastUpdated => "Sid1",
                            _ => throw new InvalidOperationException($"Unexpected sort parameter {sort.searchParameterInfo.Name}"),
                        };
                        sb.Append(column).Append(" ").Append(sort.sortOrder == SortOrder.Ascending ? "ASC" : "DESC");
                    });
                }
                else if (IsSortValueNeeded(context))
                {
                    StringBuilder.Append("SortValue ").Append(" ").Append(context.Sort[0].sortOrder == SortOrder.Ascending ? "ASC" : "DESC").Append(", Sid1 ASC");
                }
                else
                {
                    StringBuilder.Append("Sid1 ASC");
                }
            }
        }

        private void HandleTableKindChain(
            SearchParamTableExpression searchParamTableExpression,
            SearchOptions context,
            string referenceSourceTableAlias,
            string referenceTargetResourceTableAlias)
        {
            var chainedExpression = (SqlChainLinkExpression)searchParamTableExpression.Predicate;
            StringBuilder.Append("SELECT ");
            if (searchParamTableExpression.ChainLevel == 1)
            {
                StringBuilder.Append(VLatest.ReferenceSearchParam.ResourceTypeId, referenceSourceTableAlias).Append(" AS ").Append(chainedExpression.Reversed ? "T2" : "T1").Append(", ");
                StringBuilder.Append(VLatest.ReferenceSearchParam.ResourceSurrogateId, referenceSourceTableAlias).Append(" AS ").Append(chainedExpression.Reversed ? "Sid2" : "Sid1").Append(", ");
            }
            else
            {
                StringBuilder.Append("T1, Sid1, ");
            }

            StringBuilder
                .Append(VLatest.Resource.ResourceTypeId, chainedExpression.Reversed && searchParamTableExpression.ChainLevel > 1 ? referenceSourceTableAlias : referenceTargetResourceTableAlias).Append(" AS ").Append(chainedExpression.Reversed && searchParamTableExpression.ChainLevel == 1 ? "T1, " : "T2, ")
                .Append(VLatest.Resource.ResourceSurrogateId, chainedExpression.Reversed && searchParamTableExpression.ChainLevel > 1 ? referenceSourceTableAlias : referenceTargetResourceTableAlias).Append(" AS ").AppendLine(chainedExpression.Reversed && searchParamTableExpression.ChainLevel == 1 ? "Sid1 " : "Sid2 ")
                .Append("FROM ").Append(VLatest.ReferenceSearchParam).Append(' ').AppendLine(referenceSourceTableAlias)
                .Append(_joinShift).Append("JOIN ").Append(VLatest.Resource).Append(' ').Append(referenceTargetResourceTableAlias)
                .Append(" ON ").Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceSourceTableAlias).Append(" = ").Append(VLatest.Resource.ResourceTypeId, referenceTargetResourceTableAlias)
                .Append(" AND ").Append(VLatest.ReferenceSearchParam.ReferenceResourceId, referenceSourceTableAlias).Append(" = ").AppendLine(VLatest.Resource.ResourceId, referenceTargetResourceTableAlias);

            // For reverse chaining, if there is a parameter on the _id search parameter, we need another join to get the resource ID of the reference source (all we have is the surrogate ID at this point)
            bool expressionOnTargetHandledBySecondJoin = chainedExpression.ExpressionOnTarget != null && chainedExpression.Reversed && chainedExpression.ExpressionOnTarget.AcceptVisitor(ExpressionContainsParameterVisitor.Instance, SearchParameterNames.Id);
            if (expressionOnTargetHandledBySecondJoin)
            {
                const string referenceSourceResourceTableAlias = "refSourceResource";
                StringBuilder.Append(_joinShift).Append("JOIN ").Append(VLatest.Resource).Append(' ').Append(referenceSourceResourceTableAlias)
                    .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, referenceSourceTableAlias).Append(" = ").Append(VLatest.Resource.ResourceTypeId, referenceSourceResourceTableAlias)
                    .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, referenceSourceTableAlias).Append(" = ").Append(VLatest.Resource.ResourceSurrogateId, referenceSourceResourceTableAlias)
                    .Append(" AND ");
                chainedExpression.ExpressionOnTarget.AcceptVisitor(ResourceTableSearchParameterQueryGenerator.Instance, GetContext(referenceSourceResourceTableAlias));
                StringBuilder.AppendLine();
            }

            if (searchParamTableExpression.ChainLevel > 1)
            {
                StringBuilder.Append(_joinShift).Append("JOIN ").Append(TableExpressionName(FindRestrictingPredecessorTableExpressionIndex()))
                    .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, chainedExpression.Reversed ? referenceTargetResourceTableAlias : referenceSourceTableAlias).Append(" = ").Append("T2")
                    .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, chainedExpression.Reversed ? referenceTargetResourceTableAlias : referenceSourceTableAlias).Append(" = ").AppendLine("Sid2");
            }

            // since we are in chain table expression, we know the Table is the ReferenceSearchParam table
            else if (UseAppendWithJoin())
            {
                AppendIntersectionWithPredecessorUsingInnerJoin(StringBuilder, searchParamTableExpression, chainedExpression.Reversed ? referenceTargetResourceTableAlias : referenceSourceTableAlias);
            }

            using (var delimited = StringBuilder.BeginDelimitedWhereClause())
            {
                delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.SearchParamId, referenceSourceTableAlias)
                    .Append(" = ").Append(Parameters.AddParameter(VLatest.ReferenceSearchParam.SearchParamId, Model.GetSearchParamId(chainedExpression.ReferenceSearchParameter.Url), true));

                // We should remove IsHistory from ReferenceSearchParam (Source) only but keep on Resource (Target)
                AppendHistoryClause(delimited, context.ResourceVersionTypes, null, referenceTargetResourceTableAlias);
                AppendDeletedClause(delimited, context.ResourceVersionTypes, referenceTargetResourceTableAlias);

                delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ResourceTypeId, referenceSourceTableAlias)
                    .Append(" IN (")
                    .Append(string.Join(", ", chainedExpression.ResourceTypes.Select(x => Parameters.AddParameter(VLatest.ReferenceSearchParam.ResourceTypeId, Model.GetResourceTypeId(x), true))))
                    .Append(")");

                delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceSourceTableAlias)
                    .Append(" IN (")
                    .Append(string.Join(", ", chainedExpression.TargetResourceTypes.Select(x => Parameters.AddParameter(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, Model.GetResourceTypeId(x), true))))
                    .Append(")");

                if (searchParamTableExpression.ChainLevel == 1 && !UseAppendWithJoin())
                {
                    // if > 1, the intersection is handled by the JOIN
                    AppendIntersectionWithPredecessor(delimited, searchParamTableExpression, chainedExpression.Reversed ? referenceTargetResourceTableAlias : referenceSourceTableAlias);
                }

                if (chainedExpression.ExpressionOnTarget != null && !expressionOnTargetHandledBySecondJoin)
                {
                    delimited.BeginDelimitedElement();
                    chainedExpression.ExpressionOnTarget.AcceptVisitor(ResourceTableSearchParameterQueryGenerator.Instance, GetContext(chainedExpression.Reversed ? referenceSourceTableAlias : referenceTargetResourceTableAlias));
                }

                if (chainedExpression.ExpressionOnSource != null)
                {
                    delimited.BeginDelimitedElement();
                    chainedExpression.ExpressionOnSource.AcceptVisitor(ResourceTableSearchParameterQueryGenerator.Instance, GetContext(chainedExpression.Reversed ? referenceTargetResourceTableAlias : referenceSourceTableAlias));
                }
            }
        }

        private void HandleTableKindInclude(
            SearchParamTableExpression searchParamTableExpression,
            SearchOptions context,
            string referenceSourceTableAlias,
            string referenceTargetResourceTableAlias)
        {
            var includeExpression = (IncludeExpression)searchParamTableExpression.Predicate;
            _includeCteIds = _includeCteIds ?? new List<string>();
            _includeLimitCtesByResourceType = _includeLimitCtesByResourceType ?? new Dictionary<string, List<string>>();
            _includeFromCteIds = _includeFromCteIds ?? new List<string>();

            StringBuilder.Append("SELECT DISTINCT ");

            // Adding 1 to the include count for detecting a case of truncated "include" resources.
            StringBuilder.Append("TOP (").Append(Parameters.AddParameter(context.IncludeCount + 1, includeInHash: false)).Append(") ");

            var table = !includeExpression.Reversed ? referenceTargetResourceTableAlias : referenceSourceTableAlias;

            StringBuilder.Append(VLatest.Resource.ResourceTypeId, table).Append(" AS T1, ")
                .Append(VLatest.Resource.ResourceSurrogateId, table);

            // Always project IsPartial to maintain consistent column count across UNION branches
            StringBuilder.AppendLine(" AS Sid1, 0 AS IsMatch, 0 AS IsPartial ");

            StringBuilder.Append("FROM ").Append(VLatest.ReferenceSearchParam).Append(' ').AppendLine(referenceSourceTableAlias)
                .Append(_joinShift).Append("JOIN ").Append(VLatest.Resource).Append(' ').Append(referenceTargetResourceTableAlias)
                .Append(" ON ").Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceSourceTableAlias).Append(" = ").Append(VLatest.Resource.ResourceTypeId, referenceTargetResourceTableAlias)
                .Append(" AND ").Append(VLatest.ReferenceSearchParam.ReferenceResourceId, referenceSourceTableAlias).Append(" = ").AppendLine(VLatest.Resource.ResourceId, referenceTargetResourceTableAlias);

            using (var delimited = StringBuilder.BeginDelimitedWhereClause())
            {
                // Smart V2 with SearchParam has a special handling for references resources
                if (!_smartV2UnionVisited)
                {
                    if (!includeExpression.WildCard)
                    {
                        delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.SearchParamId, referenceSourceTableAlias)
                            .Append(" = ").Append(Parameters.AddParameter(VLatest.ReferenceSearchParam.SearchParamId, Model.GetSearchParamId(includeExpression.ReferenceSearchParameter.Url), true));

                        if (includeExpression.TargetResourceType != null)
                        {
                            delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceSourceTableAlias)
                                .Append(" = ").Append(Parameters.AddParameter(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, Model.GetResourceTypeId(includeExpression.TargetResourceType), true));
                        }
                        else if (includeExpression.AllowedResourceTypesByScope != null &&
                                !includeExpression.AllowedResourceTypesByScope.Contains(KnownResourceTypes.All))
                        {
                            // AllowedResourceTypesByScope - types allowed by SMART scopes on this request
                            // If the list contains "All", then we don't add a filter
                            // Restrict the reference resource types that are returned to the allowed types by scope
                            // For revinclude that would be ReferenceSearchParam.ResourceTypeId (Resource type that referes the target)
                            // For include that would be ReferenceSearchParam.ReferenceResourceTypeId (Resource type that is refered by the source)
                            // Smart V2 with SP has a special handling for references resources
                            if (!includeExpression.Reversed)
                            {
                                delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceSourceTableAlias)
                                    .Append(" IN (")
                                    .Append(string.Join(", ", includeExpression.AllowedResourceTypesByScope.Select(x => Parameters.AddParameter(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, Model.GetResourceTypeId(x), true))))
                                    .Append(")");
                            }
                            else
                            {
                                // For _revinclude we need to filter on ResourceTypeId (the resource type that contains the reference)
                                // Example: /Patient?_revinclude=*:* and scope Patient/Patient and Patient/Encounter
                                // In this case, we need to filter the resources referring Patient by the allowed types by scope
                                delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ResourceTypeId, referenceSourceTableAlias)
                                .Append(" IN (")
                                .Append(string.Join(", ", includeExpression.AllowedResourceTypesByScope.Select(x => Parameters.AddParameter(VLatest.ReferenceSearchParam.ResourceTypeId, Model.GetResourceTypeId(x), true))))
                                .Append(")");
                            }
                        }
                    }
                    else if (includeExpression.WildCard && includeExpression.AllowedResourceTypesByScope != null &&
                            !includeExpression.AllowedResourceTypesByScope.Contains(KnownResourceTypes.All))
                    {
                        // AllowedResourceTypesByScope - types allowed by SMART scopes on this request
                        // If the list contains "All", then we don't add a filter
                        // Restrict the reference resource types that are returned to the allowed types by scope
                        // For revinclude that would be ReferenceSearchParam.ResourceTypeId (Resource type that referes the target)
                        // For include that would be ReferenceSearchParam.ReferenceResourceTypeId (Resource type that is refered by the source)
                        if (!includeExpression.Reversed)
                        {
                            delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceSourceTableAlias)
                                .Append(" IN (")
                                .Append(string.Join(", ", includeExpression.AllowedResourceTypesByScope.Select(x => Parameters.AddParameter(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, Model.GetResourceTypeId(x), true))))
                                .Append(")");
                        }
                        else
                        {
                            // For _revinclude we need to filter on ResourceTypeId (the resource type that contains the reference)
                            // Example: /Patient?_revinclude=*:* and scope Patient/Patient and Patient/Encounter
                            // In this case, we need to filter the resources referring Patient by the allowed types by scope
                            delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ResourceTypeId, referenceSourceTableAlias)
                            .Append(" IN (")
                            .Append(string.Join(", ", includeExpression.AllowedResourceTypesByScope.Select(x => Parameters.AddParameter(VLatest.ReferenceSearchParam.ResourceTypeId, Model.GetResourceTypeId(x), true))))
                            .Append(")");
                        }
                    }
                }

                // We should remove IsHistory from ReferenceSearchParam (Source) only but keep on Resource (Target)
                AppendHistoryClause(delimited, context.ResourceVersionTypes, null, referenceTargetResourceTableAlias);

                AppendDeletedClause(delimited, context.ResourceVersionTypes, referenceTargetResourceTableAlias);

                table = !includeExpression.Reversed ? referenceSourceTableAlias : referenceTargetResourceTableAlias;

                // For RevIncludeIterate we expect to have a TargetType specified if the target reference can be of multiple types
                var resourceTypeIds = includeExpression.ResourceTypes.Select(x => Model.GetResourceTypeId(x)).ToArray();
                if (includeExpression.Reversed && includeExpression.Iterate)
                {
                    if (includeExpression.TargetResourceType != null)
                    {
                        resourceTypeIds = new[] { Model.GetResourceTypeId(includeExpression.TargetResourceType) };
                    }
                    else if (includeExpression.ReferenceSearchParameter?.TargetResourceTypes?.Count > 0)
                    {
                        resourceTypeIds = new[] { Model.GetResourceTypeId(includeExpression.ReferenceSearchParameter.TargetResourceTypes.ToList().First()) };
                    }
                }

                delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ResourceTypeId, table)
                    .Append(" IN (")
                    .Append(string.Join(", ", resourceTypeIds))
                    .Append(")");

                // Get FROM ctes
                List<string> fromCte = new List<string>();
                fromCte.Add(_cteMainSelect);

                if (includeExpression.Iterate)
                {
                    // Include Iterate
                    if (!includeExpression.Reversed)
                    {
                        // _include:iterate may appear without a preceding _include, in case of circular reference
                        // On that case, the fromCte is _cteMainSelect
                        if (TryGetIncludeCtes(includeExpression.SourceResourceType, out _includeFromCteIds))
                        {
                            fromCte = _includeFromCteIds;
                        }
                    }

                    // RevInclude Iterate
                    else
                    {
                        if (includeExpression.TargetResourceType != null)
                        {
                            if (TryGetIncludeCtes(includeExpression.TargetResourceType, out _includeFromCteIds))
                            {
                                fromCte = _includeFromCteIds;
                            }
                        }
                        else if (includeExpression.ReferenceSearchParameter?.TargetResourceTypes != null)
                        {
                            // Assumes TargetResourceTypes is of length 1. Otherwise, a BadRequest would have been thrown earlier for _revinclude:iterate
                            List<string> fromCtes;
                            var targetType = includeExpression.ReferenceSearchParameter.TargetResourceTypes[0];

                            if (TryGetIncludeCtes(targetType, out fromCtes))
                            {
                                _includeFromCteIds.AddRange(fromCtes);
                            }

                            _includeFromCteIds = _includeFromCteIds.Distinct().ToList();
                            fromCte = _includeFromCteIds.Count > 0 ? _includeFromCteIds : fromCte;
                        }
                    }
                }

                var includesContinuationToken = IncludesContinuationToken.FromString(context.IncludesContinuationToken);
                if (!context.IsIncludesOperation || includesContinuationToken?.IncludeResourceTypeId == null || includesContinuationToken?.IncludeResourceSurrogateId == null)
                {
                    if (includeExpression.Reversed && includeExpression.SourceResourceType != "*")
                    {
                        delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ResourceTypeId, referenceSourceTableAlias)
                            .Append(" = ").Append(Parameters.AddParameter(VLatest.ReferenceSearchParam.ResourceTypeId, Model.GetResourceTypeId(includeExpression.SourceResourceType), true));
                    }
                }
                else
                {
                    var tableAlias = includeExpression.Reversed ? referenceSourceTableAlias : referenceTargetResourceTableAlias;
                    delimited.BeginDelimitedElement()
                        .Append("(")
                        .Append(VLatest.Resource.ResourceTypeId, tableAlias)
                        .Append(" > ")
                        .Append(includesContinuationToken.IncludeResourceTypeId)
                        .Append(" OR (")
                        .Append(VLatest.Resource.ResourceTypeId, tableAlias)
                        .Append(" = ")
                        .Append(includesContinuationToken.IncludeResourceTypeId)
                        .Append(" AND ")
                        .Append(VLatest.ReferenceSearchParam.ResourceSurrogateId, tableAlias)
                        .Append(" > ")
                        .Append(includesContinuationToken.IncludeResourceSurrogateId)
                        .Append("))");
                }

                var scope = delimited.BeginDelimitedElement();
                scope.Append("EXISTS (");
                for (var index = 0; index < fromCte.Count; index++)
                {
                    var cte = fromCte[index];
                    scope.Append("SELECT * FROM ").Append(cte)
                        .Append(" WHERE ").Append(VLatest.Resource.ResourceTypeId, table).Append(" = T1 AND ")
                        .Append(VLatest.Resource.ResourceSurrogateId, table).Append(" = Sid1");

                    if (!includeExpression.Iterate && !context.IsIncludesOperation)
                    {
                        // Limit the join to the main select CTE.
                        // The main select will have max+1 items in the result set to account for paging, so we only want to join using the max amount.

                        scope.Append(" AND Row < ").Append(Parameters.AddParameter(context.MaxItemCount + 1, true));
                    }

                    if (index < fromCte.Count - 1)
                    {
                        scope.AppendLine(" UNION ALL ");
                    }
                }

                scope.Append(")");

                if (includeExpression.AllowedResourceTypesByScope != null && !includeExpression.AllowedResourceTypesByScope.Contains(KnownResourceTypes.All) && _smartV2UnionVisited)
                {
                    if (!includeExpression.Reversed)
                    {
                        var scopeForSmartV2 = delimited.BeginDelimitedElement();
                        scopeForSmartV2.Append("EXISTS (");
                        scopeForSmartV2.Append("SELECT * FROM ");
                        scopeForSmartV2.Append(TableExpressionName(_smartv2ScopeUnionCTE))
                            .Append(" WHERE ").Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceSourceTableAlias).Append(" = T1 AND ")
                            .Append(VLatest.Resource.ResourceSurrogateId, referenceTargetResourceTableAlias).Append(" = Sid1)");
                    }
                    else
                    {
                        var scopeForSmartV2 = delimited.BeginDelimitedElement();
                        scopeForSmartV2.Append("EXISTS (");
                        scopeForSmartV2.Append("SELECT * FROM ");
                        scopeForSmartV2.Append(TableExpressionName(_smartv2ScopeUnionCTE))
                            .Append(" WHERE ").Append(VLatest.ReferenceSearchParam.ResourceTypeId, referenceSourceTableAlias).Append(" = T1 AND ")
                            .Append(VLatest.ReferenceSearchParam.ResourceSurrogateId, referenceSourceTableAlias).Append(" = Sid1)");
                    }
                }

                if (_rootExpression.SmartCompartmentMembership != null)
                {
                    AppendSmartCompartmentCandidatePredicate(
                        delimited.BeginDelimitedElement(),
                        includeExpression.Reversed ? referenceSourceTableAlias : referenceTargetResourceTableAlias,
                        candidateIsResourceTable: !includeExpression.Reversed);
                }
            }

            if (context.IsIncludesOperation)
            {
                StringBuilder.AppendLine("ORDER BY T1 ASC, Sid1 ASC");
                _includeCteIds.Add(TableExpressionName(_tableExpressionCounter));
            }

            if (includeExpression.Reversed)
            {
                // mark that this cte is a reverse one, meaning we need to add another items limitation
                // cte on top of it
                _cteToLimit.Add(_tableExpressionCounter);
            }

            // Update target reference cte dictionary
            var curLimitCte = TableExpressionName(_tableExpressionCounter + 1);

            // Take the count before AddIncludeLimitCte because _includeFromCteIds?.Count will be incremented differently depending on the resource type.
            int count = _includeFromCteIds?.Count ?? 0;

            // Add current cte limit to the dictionary
            if (includeExpression.Reversed)
            {
                AddIncludeLimitCte(includeExpression.SourceResourceType, curLimitCte);
            }
            else
            {
                // Not reversed and a specific target type is provided as the 3rd part of include value
                if (includeExpression.TargetResourceType != null)
                {
                    AddIncludeLimitCte(includeExpression.TargetResourceType, curLimitCte);
                }
                else if (includeExpression.ReferenceSearchParameter != null)
                {
                    includeExpression.ReferenceSearchParameter.TargetResourceTypes?.ToList().ForEach(t => AddIncludeLimitCte(t, curLimitCte));
                }
            }

            if (includeExpression.WildCard)
            {
                includeExpression.ReferencedTypes?.ToList().ForEach(t => AddIncludeLimitCte(t, curLimitCte));
            }
        }

        private void AppendSmartCompartmentCandidatePredicate(
            IndentedStringBuilder scope,
            string candidateTableAlias,
            bool candidateIsResourceTable)
        {
            const string membershipAlias = "smartCompartmentMembership";
            const string rootAlias = "smartCompartmentRoot";

            SmartCompartmentMembershipContext membership = _rootExpression.SmartCompartmentMembership;
            var candidateResourceTypeId = candidateIsResourceTable
                ? VLatest.Resource.ResourceTypeId
                : VLatest.ReferenceSearchParam.ResourceTypeId;
            var candidateResourceSurrogateId = candidateIsResourceTable
                ? VLatest.Resource.ResourceSurrogateId
                : VLatest.ReferenceSearchParam.ResourceSurrogateId;

            object compartmentResourceTypeId = Parameters.AddParameter(
                VLatest.Resource.ResourceTypeId,
                Model.GetResourceTypeId(membership.CompartmentResourceType),
                true);
            object compartmentResourceId = Parameters.AddParameter(
                VLatest.Resource.ResourceId,
                membership.CompartmentResourceId,
                true);

            scope.Append("(")
                .Append("(")
                .Append(candidateResourceTypeId, candidateTableAlias)
                .Append(" = ")
                .Append(compartmentResourceTypeId)
                .Append(" AND ");

            if (candidateIsResourceTable)
            {
                scope.Append(VLatest.Resource.ResourceId, candidateTableAlias)
                    .Append(" = ")
                    .Append(compartmentResourceId);
            }
            else
            {
                scope.Append("EXISTS (SELECT 1 FROM ")
                    .Append(VLatest.Resource)
                    .Append(' ')
                    .Append(rootAlias)
                    .Append(" WHERE ")
                    .Append(VLatest.Resource.ResourceTypeId, rootAlias)
                    .Append(" = ")
                    .Append(candidateResourceTypeId, candidateTableAlias)
                    .Append(" AND ")
                    .Append(VLatest.Resource.ResourceSurrogateId, rootAlias)
                    .Append(" = ")
                    .Append(candidateResourceSurrogateId, candidateTableAlias)
                    .Append(" AND ")
                    .Append(VLatest.Resource.ResourceId, rootAlias)
                    .Append(" = ")
                    .Append(compartmentResourceId)
                    .Append(")");
            }

            scope.Append(")");

            if (!membership.SharedResourceTypes.IsDefaultOrEmpty)
            {
                scope.Append(" OR ")
                    .Append(candidateResourceTypeId, candidateTableAlias)
                    .Append(" IN (")
                    .Append(string.Join(
                        ", ",
                        membership.SharedResourceTypes.Select(resourceType => Parameters.AddParameter(
                            VLatest.Resource.ResourceTypeId,
                            Model.GetResourceTypeId(resourceType),
                            true))))
                    .Append(")");
            }

            if (!membership.MembershipRules.IsDefaultOrEmpty)
            {
                scope.Append(" OR EXISTS (SELECT 1 FROM ")
                    .Append(VLatest.ReferenceSearchParam)
                    .Append(' ')
                    .Append(membershipAlias)
                    .Append(" WHERE ")
                    .Append(VLatest.ReferenceSearchParam.ResourceTypeId, membershipAlias)
                    .Append(" = ")
                    .Append(candidateResourceTypeId, candidateTableAlias)
                    .Append(" AND ")
                    .Append(VLatest.ReferenceSearchParam.ResourceSurrogateId, membershipAlias)
                    .Append(" = ")
                    .Append(candidateResourceSurrogateId, candidateTableAlias)
                    .Append(" AND ")
                    .Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, membershipAlias)
                    .Append(" = ")
                    .Append(compartmentResourceTypeId)
                    .Append(" AND ")
                    .Append(VLatest.ReferenceSearchParam.ReferenceResourceId, membershipAlias)
                    .Append(" = ")
                    .Append(compartmentResourceId)
                    .Append(" AND ")
                    .Append(VLatest.ReferenceSearchParam.BaseUri, membershipAlias)
                    .Append(" IS NULL AND (");

                for (int ruleIndex = 0; ruleIndex < membership.MembershipRules.Length; ruleIndex++)
                {
                    SmartCompartmentMembershipRule rule = membership.MembershipRules[ruleIndex];
                    if (ruleIndex > 0)
                    {
                        scope.Append(" OR ");
                    }

                    scope.Append("(")
                        .Append(VLatest.ReferenceSearchParam.ResourceTypeId, membershipAlias)
                        .Append(" = ")
                        .Append(Parameters.AddParameter(
                            VLatest.ReferenceSearchParam.ResourceTypeId,
                            Model.GetResourceTypeId(rule.ResourceType),
                            true))
                        .Append(" AND ")
                        .Append(VLatest.ReferenceSearchParam.SearchParamId, membershipAlias)
                        .Append(" IN (")
                        .Append(string.Join(
                            ", ",
                            rule.SearchParameterUrls.Select(url => Parameters.AddParameter(
                                VLatest.ReferenceSearchParam.SearchParamId,
                                Model.GetSearchParamId(url),
                                true))))
                        .Append("))");
                }

                scope.Append("))");
            }

            if (!membership.ConditionalRules.IsDefaultOrEmpty)
            {
                // Conditional-visibility legs (for example the SMART Device limits). Each rule authorizes a candidate
                // of a given resource type that either references the compartment root (own device) or has no
                // reference at all (unassigned device). Candidates that satisfy neither (for example a device
                // assigned to a different patient) are excluded, closing the _include/_revinclude leak. This loop is
                // generic: the rules are data supplied by SmartCompartmentSearchRewriter.GetConditionalCompartmentRules,
                // so no resource-type-specific logic lives here.
                for (int conditionalIndex = 0; conditionalIndex < membership.ConditionalRules.Length; conditionalIndex++)
                {
                    SmartCompartmentConditionalMembershipRule rule = membership.ConditionalRules[conditionalIndex];
                    string conditionalAlias = "smartCompartmentConditional" + conditionalIndex.ToString(CultureInfo.InvariantCulture);

                    object ruleResourceTypeId = Parameters.AddParameter(
                        VLatest.Resource.ResourceTypeId,
                        Model.GetResourceTypeId(rule.ResourceType),
                        true);
                    object ruleSearchParamId = Parameters.AddParameter(
                        VLatest.ReferenceSearchParam.SearchParamId,
                        Model.GetSearchParamId(new Uri(rule.ReferenceSearchParameterUrl)),
                        true);

                    scope.Append(" OR (")
                        .Append(candidateResourceTypeId, candidateTableAlias)
                        .Append(" = ")
                        .Append(ruleResourceTypeId)
                        .Append(" AND ");

                    scope.Append(rule.Visibility == SmartCompartmentConditionalVisibility.HasNoReference ? "NOT EXISTS" : "EXISTS")
                        .Append(" (SELECT 1 FROM ")
                        .Append(VLatest.ReferenceSearchParam)
                        .Append(' ')
                        .Append(conditionalAlias)
                        .Append(" WHERE ")
                        .Append(VLatest.ReferenceSearchParam.ResourceTypeId, conditionalAlias)
                        .Append(" = ")
                        .Append(candidateResourceTypeId, candidateTableAlias)
                        .Append(" AND ")
                        .Append(VLatest.ReferenceSearchParam.ResourceSurrogateId, conditionalAlias)
                        .Append(" = ")
                        .Append(candidateResourceSurrogateId, candidateTableAlias)
                        .Append(" AND ")
                        .Append(VLatest.ReferenceSearchParam.SearchParamId, conditionalAlias)
                        .Append(" = ")
                        .Append(ruleSearchParamId);

                    if (rule.Visibility == SmartCompartmentConditionalVisibility.ReferencesCompartmentRoot)
                    {
                        scope.Append(" AND ")
                            .Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, conditionalAlias)
                            .Append(" = ")
                            .Append(compartmentResourceTypeId)
                            .Append(" AND ")
                            .Append(VLatest.ReferenceSearchParam.ReferenceResourceId, conditionalAlias)
                            .Append(" = ")
                            .Append(compartmentResourceId)
                            .Append(" AND ")
                            .Append(VLatest.ReferenceSearchParam.BaseUri, conditionalAlias)
                            .Append(" IS NULL");
                    }

                    scope.Append("))");
                }
            }

            scope.Append(")");
        }

        private void HandleTableKindIncludeLimit(SearchOptions context)
        {
            StringBuilder.Append("SELECT DISTINCT TOP (")
                .Append(Parameters.AddParameter(context.IncludeCount + 1, includeInHash: false))
                .Append(") T1, Sid1, IsMatch, ");

            StringBuilder.Append("CASE WHEN count_big(*) over() > ")
                .Append(Parameters.AddParameter(context.IncludeCount, true))
                .AppendLine(" THEN 1 ELSE 0 END AS IsPartial ");

            StringBuilder.Append("FROM ").AppendLine(TableExpressionName(_tableExpressionCounter - 1));
            if (!context.IsIncludesOperation)
            {
                // the 'original' include cte is not in the union, but this new layer is instead
                _includeCteIds.Add(TableExpressionName(_tableExpressionCounter));
            }
            else
            {
                StringBuilder.AppendLine("ORDER BY T1 ASC, Sid1 ASC");
            }
        }

        private void HandleTableKindIncludeUnionAll(SearchOptions context)
        {
            StringBuilder.Append("SELECT T1, Sid1, IsMatch, IsPartial ");

            bool sortValueNeeded = IsSortValueNeeded(context);

            // The includes operation does not contain matched resources, so no sort value is needed.
            if (sortValueNeeded && !context.IsIncludesOperation)
            {
                StringBuilder.AppendLine(", SortValue");
            }
            else
            {
                StringBuilder.AppendLine();
            }

            // Excluding a cte for matched resources for $includes operation.
            var rootCte = _cteMainSelect;
            var skip = 0;
            if (context.IsIncludesOperation)
            {
                rootCte = _includeCteIds.FirstOrDefault();
                skip = rootCte == null ? 0 : 1;
            }

            StringBuilder.Append("FROM ").AppendLine(rootCte);

            foreach (var includeCte in _includeCteIds.Skip(skip))
            {
                StringBuilder.AppendLine("UNION ALL");
                StringBuilder.Append("SELECT T1, Sid1, IsMatch, IsPartial");
                if (sortValueNeeded && !context.IsIncludesOperation)
                {
                    StringBuilder.AppendLine(", NULL as SortValue ");
                }
                else
                {
                    StringBuilder.AppendLine();
                }

                // Matched results should be excluded from included CTEs
                StringBuilder.Append("FROM ").Append(includeCte)
                    .Append(" WHERE NOT EXISTS (SELECT * FROM ").Append(_cteMainSelect)
                    .Append(" WHERE ").Append(_cteMainSelect).Append(".Sid1 = ").Append(includeCte).Append(".Sid1")
                    .Append(" AND ").Append(_cteMainSelect).Append(".T1 = ").Append(includeCte).AppendLine(".T1)");
            }
        }

        private void HandleTableKindSort(SearchParamTableExpression searchParamTableExpression, SearchOptions context)
        {
            if (searchParamTableExpression.ChainLevel != 0)
            {
                throw new InvalidOperationException("Multiple chain level is not possible.");
            }

            SortContext sortContext = GetSortRelatedDetails(context);

            if (!string.IsNullOrEmpty(sortContext.SortColumnName) && searchParamTableExpression.QueryGenerator != null)
            {
                StringBuilder.Append("SELECT ")
                    .Append(VLatest.Resource.ResourceTypeId, null).Append(" AS T1, ")
                    .Append(VLatest.Resource.ResourceSurrogateId, null).Append(" AS Sid1, ")
                    .Append(sortContext.SortColumnName, null).AppendLine(" AS SortValue")
                    .Append("FROM ").AppendLine(searchParamTableExpression.QueryGenerator.Table);

                if (UseAppendWithJoin())
                {
                    AppendIntersectionWithPredecessorUsingInnerJoin(StringBuilder, searchParamTableExpression);
                }

                using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                {
                    AppendHistoryClause(delimited, context.ResourceVersionTypes, searchParamTableExpression);
                    AppendMinOrMax(delimited, context);

                    if (searchParamTableExpression.Predicate != null)
                    {
                        delimited.BeginDelimitedElement();
                        searchParamTableExpression.Predicate.AcceptVisitor(searchParamTableExpression.QueryGenerator, GetContext());
                    }

                    // if continuation token exists, add it to the query
                    if (sortContext.ContinuationToken != null)
                    {
                        var sortOperand = sortContext.SortOrder == SortOrder.Ascending ? ">" : "<";

                        delimited.BeginDelimitedElement();
                        StringBuilder.Append("((").Append(sortContext.SortColumnName, null).Append(" = ").Append(Parameters.AddParameter(sortContext.SortColumnName, sortContext.SortValue, includeInHash: false));
                        StringBuilder.Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, null).Append(" > ").Append(Parameters.AddParameter(VLatest.Resource.ResourceSurrogateId, sortContext.ContinuationToken.ResourceSurrogateId, includeInHash: false)).Append(")");
                        StringBuilder.Append(" OR ").Append(sortContext.SortColumnName, null).Append(" ").Append(sortOperand).Append(" ").Append(Parameters.AddParameter(sortContext.SortColumnName, sortContext.SortValue, includeInHash: false)).AppendLine(")");
                    }

                    if (!UseAppendWithJoin())
                    {
                        AppendIntersectionWithPredecessor(delimited, searchParamTableExpression);
                    }
                }
            }

            _sortVisited = true;
        }

        private void HandleTableKindSortWithFilter(SearchParamTableExpression searchParamTableExpression, SearchOptions context)
        {
            SortContext sortContext = GetSortRelatedDetails(context);

            if (!string.IsNullOrEmpty(sortContext.SortColumnName) && searchParamTableExpression.QueryGenerator != null)
            {
                StringBuilder.Append("SELECT ")
                    .Append(VLatest.Resource.ResourceTypeId, null).Append(" AS T1, ")
                    .Append(VLatest.Resource.ResourceSurrogateId, null).Append(" AS Sid1, ")
                    .Append(sortContext.SortColumnName, null).AppendLine(" AS SortValue")
                    .Append("FROM ").AppendLine(searchParamTableExpression.QueryGenerator.Table);

                if (UseAppendWithJoin())
                {
                    AppendIntersectionWithPredecessorUsingInnerJoin(StringBuilder, searchParamTableExpression);
                }

                using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                {
                    AppendHistoryClause(delimited, context.ResourceVersionTypes, searchParamTableExpression);
                    AppendMinOrMax(delimited, context);

                    if (searchParamTableExpression.Predicate != null)
                    {
                        delimited.BeginDelimitedElement();
                        searchParamTableExpression.Predicate.AcceptVisitor(searchParamTableExpression.QueryGenerator, GetContext());
                    }

                    // if continuation token exists, add it to the query
                    if (sortContext.ContinuationToken != null)
                    {
                        var sortOperand = sortContext.SortOrder == SortOrder.Ascending ? ">" : "<";

                        delimited.BeginDelimitedElement();
                        StringBuilder.Append("((").Append(sortContext.SortColumnName, null).Append(" = ").Append(Parameters.AddParameter(sortContext.SortColumnName, sortContext.SortValue, includeInHash: false));
                        StringBuilder.Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, null).Append(" > ").Append(Parameters.AddParameter(VLatest.Resource.ResourceSurrogateId, sortContext.ContinuationToken.ResourceSurrogateId, includeInHash: false)).Append(")");
                        StringBuilder.Append(" OR ").Append(sortContext.SortColumnName, null).Append(" ").Append(sortOperand).Append(" ").Append(Parameters.AddParameter(sortContext.SortColumnName, sortContext.SortValue, includeInHash: false)).AppendLine(")");
                    }

                    if (!UseAppendWithJoin())
                    {
                        AppendIntersectionWithPredecessor(delimited, searchParamTableExpression);
                    }
                }
            }

            _sortVisited = true;
        }

        private SearchParameterQueryGeneratorContext GetContext(string tableAlias = null)
        {
            return new SearchParameterQueryGeneratorContext(StringBuilder, Parameters, Model, _schemaInfo, isAsyncOperation: _isAsyncOperation, tableAlias);
        }

        private void AppendNewSetOfUnionAllTableExpressions(SearchOptions context, UnionExpression unionExpression, SearchParamTableExpressionQueryGenerator defaultQueryGenerator)
        {
            if (unionExpression.Operator != UnionOperator.All)
            {
                throw new ArgumentOutOfRangeException(unionExpression.Operator.ToString());
            }

            // Iterate through all expressions and create a unique CTE for each one.
            int firstInclusiveTableExpressionId = _tableExpressionCounter + 1;
            foreach (Expression innerExpression in unionExpression.Expressions)
            {
                // Determine the appropriate query generator for this specific inner expression
                var queryGenerator = DetermineQueryGeneratorForExpression(innerExpression, defaultQueryGenerator);

                var searchParamExpression = new SearchParamTableExpression(
                    queryGenerator,
                    innerExpression,
                    SearchParamTableExpressionKind.Union);

                searchParamExpression.AcceptVisitor(this, context);
            }

            int lastInclusiveTableExpressionId = _tableExpressionCounter;

            // Create a final CTE aggregating results from all previous CTEs.
            StringBuilder.Append(TableExpressionName(++_tableExpressionCounter)).AppendLine(" AS").AppendLine("(");
            for (int tableExpressionId = firstInclusiveTableExpressionId; tableExpressionId <= lastInclusiveTableExpressionId; tableExpressionId++)
            {
                using (StringBuilder.Indent())
                {
                    StringBuilder.Append("SELECT * FROM ").Append(TableExpressionName(tableExpressionId));

                    if (tableExpressionId < lastInclusiveTableExpressionId)
                    {
                        StringBuilder.AppendLine();
                        StringBuilder.Append("UNION ALL ");
                    }
                }
            }

            StringBuilder.AppendLine();
            StringBuilder.Append(")");

            // check for a previous union all, and if so, join the new union all with the previous one
            if (_unionAggregateCTEIndex > -1)
            {
                var prevUnionAggregateTableName = TableExpressionName(_unionAggregateCTEIndex);
                var currentUnionAggregateTableName = TableExpressionName(_tableExpressionCounter);

                StringBuilder.Append(", ");
                StringBuilder.AppendLine();
                StringBuilder.Append(TableExpressionName(++_tableExpressionCounter)).AppendLine(" AS").AppendLine("(");

                using (StringBuilder.Indent())
                {
                    StringBuilder.Append("SELECT ").Append(prevUnionAggregateTableName + ".T1, ").Append(prevUnionAggregateTableName + ".Sid1")
                    .AppendLine()
                    .Append("FROM ").Append(prevUnionAggregateTableName)
                    .AppendLine()
                    .Append(_joinShift).Append("JOIN ").Append(currentUnionAggregateTableName)
                    .Append(" ON ").Append(prevUnionAggregateTableName + ".T1").Append(" = ").Append(currentUnionAggregateTableName + ".T1")
                    .Append(" AND ").Append(prevUnionAggregateTableName + ".Sid1").Append(" = ").Append(currentUnionAggregateTableName + ".Sid1")
                    .AppendLine();
                }

                StringBuilder.Append(")");
            }

            _unionAggregateCTEIndex = _tableExpressionCounter;

            _unionVisited = true;
            _firstChainAfterUnionVisited = false;
        }

        private void AppendSmartNewSetOfUnionAllTableExpressions(SearchOptions context, UnionExpression unionExpression, SearchParamTableExpressionQueryGenerator defaultQueryGenerator, bool skipJoinFromPreviousUnions)
        {
            if (unionExpression.Operator != UnionOperator.All)
            {
                throw new ArgumentOutOfRangeException(unionExpression.Operator.ToString());
            }

            List<int> lastAndedCTEs = new List<int>();

            // Iterate through all expressions and create a unique CTE for each one.
            foreach (Expression innerExpression in unionExpression.Expressions)
            {
                context.SkipAppendIntersectionWithPredecessor = false;
                if (innerExpression is MultiaryExpression innerMultiaryExpression)
                {
                    bool firstQueryParamExpression = true;
                    foreach (Expression childExpression in innerMultiaryExpression.Expressions)
                    {
                        // Determine the appropriate query generator for this specific inner expression
                        StringBuilder.Append(TableExpressionName(++_tableExpressionCounter)).AppendLine(" AS").AppendLine("(");
                        var childQueryGenerator = DetermineQueryGeneratorForExpression(childExpression, defaultQueryGenerator);

                        var childSearchParamExpression = new SearchParamTableExpression(
                            childQueryGenerator,
                            childExpression,
                            SearchParamTableExpressionKind.Normal);

                        context.SkipAppendIntersectionWithPredecessor = firstQueryParamExpression;
                        firstQueryParamExpression = false;
                        using (StringBuilder.Indent())
                        {
                            childSearchParamExpression.AcceptVisitor(this, context);
                        }

                        StringBuilder.AppendLine("),");
                    }

                    lastAndedCTEs.Add(_tableExpressionCounter);
                }
                else
                {
                    // Determine the appropriate query generator for this specific inner expression
                    var queryGenerator = DetermineQueryGeneratorForExpression(innerExpression, defaultQueryGenerator);

                    var searchParamExpression = new SearchParamTableExpression(
                        queryGenerator,
                        innerExpression,
                        SearchParamTableExpressionKind.Union);

                    searchParamExpression.AcceptVisitor(this, context);
                    lastAndedCTEs.Add(_tableExpressionCounter);
                }
            }

            context.SkipAppendIntersectionWithPredecessor = false;
            int lastInclusiveTableExpressionId = _tableExpressionCounter;

            // Create a final CTE aggregating results from all previous CTEs.
            StringBuilder.Append(TableExpressionName(++_tableExpressionCounter)).AppendLine(" AS").AppendLine("(");
            _smartv2ScopeUnionCTE = _tableExpressionCounter;
            foreach (int tableExpressionId in lastAndedCTEs)
            {
                using (StringBuilder.Indent())
                {
                    StringBuilder.Append("SELECT * FROM ").Append(TableExpressionName(tableExpressionId));

                    if (tableExpressionId < lastInclusiveTableExpressionId)
                    {
                        StringBuilder.AppendLine();
                        StringBuilder.Append("UNION ALL ");
                    }
                }
            }

            StringBuilder.AppendLine();
            StringBuilder.Append(")");

            // check for a previous union all, and if so, join the new union all with the previous one
            if (!skipJoinFromPreviousUnions && _unionAggregateCTEIndex > -1)
            {
                var prevUnionAggregateTableName = TableExpressionName(_unionAggregateCTEIndex);
                var currentUnionAggregateTableName = TableExpressionName(_tableExpressionCounter);

                StringBuilder.Append(", ");
                StringBuilder.AppendLine();
                StringBuilder.Append(TableExpressionName(++_tableExpressionCounter)).AppendLine(" AS").AppendLine("(");

                using (StringBuilder.Indent())
                {
                    StringBuilder.Append("SELECT ").Append(prevUnionAggregateTableName + ".T1, ").Append(prevUnionAggregateTableName + ".Sid1")
                    .AppendLine()
                    .Append("FROM ").Append(prevUnionAggregateTableName)
                    .AppendLine()
                    .Append(_joinShift).Append("JOIN ").Append(currentUnionAggregateTableName)
                    .Append(" ON ").Append(prevUnionAggregateTableName + ".T1").Append(" = ").Append(currentUnionAggregateTableName + ".T1")
                    .Append(" AND ").Append(prevUnionAggregateTableName + ".Sid1").Append(" = ").Append(currentUnionAggregateTableName + ".Sid1")
                    .AppendLine();
                }

                StringBuilder.Append(")");
            }

            _unionVisited = true;
            _smartV2UnionVisited = true;
            _firstChainAfterUnionVisited = false;
        }

        private void AppendNewTableExpression(IndentedStringBuilder sb, SearchParamTableExpression tableExpression, int cteId, SearchOptions context)
        {
            sb.Append(TableExpressionName(cteId)).AppendLine(" AS").AppendLine("(");

            using (sb.Indent())
            {
                tableExpression.AcceptVisitor(this, context);
            }

            sb.Append(")");
        }

        /// <summary>
        /// Determines the appropriate query generator for a specific expression within a UNION.
        /// This allows different expressions in a UNION to use different underlying SQL tables.
        /// </summary>
        private SearchParamTableExpressionQueryGenerator DetermineQueryGeneratorForExpression(Expression expression, SearchParamTableExpressionQueryGenerator defaultQueryGenerator)
        {
            // Use the factory to determine the appropriate query generator for this expression
            var specificGenerator = expression.AcceptVisitor(_queryGeneratorFactory, _queryGeneratorFactory.InitialContext);
            return specificGenerator ?? defaultQueryGenerator;
        }

        private bool UseAppendWithJoin()
        {
            // if either:
            // 1. the number of table expressions is greater than the limit indicating a complex query
            // 2. the previous query generator failed to generate a query
            // then we will NOT use the EXISTS clause instead of the inner join
            if (_rootExpression.SearchParamTableExpressions.Count > maxTableExpressionCountLimitForExists ||
                previousSqlQueryGeneratorFailure)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private void AppendIntersectionWithPredecessor(IndentedStringBuilder.DelimitedScope delimited, SearchParamTableExpression searchParamTableExpression, string tableAlias = null)
        {
            int predecessorIndex = FindRestrictingPredecessorTableExpressionIndex();

            if (predecessorIndex >= 0)
            {
                delimited.BeginDelimitedElement();

                bool intersectWithFirst = (searchParamTableExpression.Kind == SearchParamTableExpressionKind.Chain ? searchParamTableExpression.ChainLevel - 1 : searchParamTableExpression.ChainLevel) == 0;

                StringBuilder.Append("EXISTS (SELECT * FROM ").Append(TableExpressionName(predecessorIndex))
                    .Append(" WHERE ").Append(VLatest.Resource.ResourceTypeId, tableAlias).Append(" = ").Append(intersectWithFirst ? "T1" : "T2")
                    .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, tableAlias).Append(" = ").Append(intersectWithFirst ? "Sid1" : "Sid2")
                    .Append(')');
            }
        }

        private void AppendIntersectionWithPredecessorUsingInnerJoin(IndentedStringBuilder sb, SearchParamTableExpression searchParamTableExpression, string tableAlias = null)
        {
            int predecessorIndex = FindRestrictingPredecessorTableExpressionIndex();

            if (predecessorIndex >= 0)
            {
                bool intersectWithFirst = (searchParamTableExpression.Kind == SearchParamTableExpressionKind.Chain ? searchParamTableExpression.ChainLevel - 1 : searchParamTableExpression.ChainLevel) == 0;

                // To simplify query plan generation, if we are intersecting with the Reference search param table, we will use an inner join
                // rather than an EXISTS clause.  We have see that this significanlty reduces the query plan generation time for
                // complex queries
                sb.Append(_joinShift).Append("JOIN " + TableExpressionName(predecessorIndex - 0))
                    .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, tableAlias).Append(" = ").Append(intersectWithFirst ? "T1" : "T2")
                    .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, tableAlias).Append(" = ").Append(intersectWithFirst ? "Sid1" : "Sid2")
                    .AppendLine();
            }
        }

        private int FindRestrictingPredecessorTableExpressionIndex()
        {
            int FindImpl(int currentIndex)
            {
                // Due to the UnionAll expressions, the number of the current index used to create new CTEs can be greater than
                // the number of expressions in '_rootExpression.SearchParamTableExpressions'.
                if (currentIndex >= _rootExpression.SearchParamTableExpressions.Count)
                {
                    return currentIndex - 1;
                }

                SearchParamTableExpression currentSearchParamTableExpression = _rootExpression.SearchParamTableExpressions[currentIndex];

                // Include all the required SearchParamTableExpressionKind here
                switch (currentSearchParamTableExpression.Kind)
                {
                    case SearchParamTableExpressionKind.NotExists:
                    case SearchParamTableExpressionKind.Normal:
                    case SearchParamTableExpressionKind.Chain:
                    case SearchParamTableExpressionKind.Top:
                        return currentIndex - 1;
                    case SearchParamTableExpressionKind.Concatenation:
                        return FindImpl(currentIndex - 1);
                    case SearchParamTableExpressionKind.Sort:
                    case SearchParamTableExpressionKind.SortWithFilter:
                        return currentIndex - 1;
                    case SearchParamTableExpressionKind.All:
                        return currentIndex - 1;
                    case SearchParamTableExpressionKind.Include:
                    case SearchParamTableExpressionKind.IncludeLimit:
                    case SearchParamTableExpressionKind.Union:
                    case SearchParamTableExpressionKind.IncludeUnionAll:
                        return currentIndex - 1;
                    default:
                        throw new ArgumentOutOfRangeException(currentSearchParamTableExpression.Kind.ToString());
                }
            }

            return FindImpl(_tableExpressionCounter);
        }

        private void AppendDeletedClause(in IndentedStringBuilder.DelimitedScope delimited, ResourceVersionType resourceVersionType, string tableAlias = null)
        {
            if (resourceVersionType.HasFlag(ResourceVersionType.Latest) && !resourceVersionType.HasFlag(ResourceVersionType.SoftDeleted))
            {
                delimited.BeginDelimitedElement();
                StringBuilder.Append(VLatest.Resource.IsDeleted, tableAlias).Append(" = 0 ");
            }
            else if (resourceVersionType.HasFlag(ResourceVersionType.SoftDeleted) && !resourceVersionType.HasFlag(ResourceVersionType.Latest))
            {
                delimited.BeginDelimitedElement();
                StringBuilder.Append(VLatest.Resource.IsDeleted, tableAlias).Append(" = 1 ");
            }
        }

        private void AppendHistoryClause(in IndentedStringBuilder.DelimitedScope delimited, ResourceVersionType resourceVersionType, SearchParamTableExpression expression = null, string tableAlias = null, string specialCaseTableName = null)
        {
            if (expression != null &&
                expression.QueryGenerator.Table.TableName.EndsWith("SearchParam", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(specialCaseTableName) ||
                 expression.QueryGenerator.Table.TableName.Equals(specialCaseTableName, StringComparison.OrdinalIgnoreCase)))
            {
                // History clause is not applicable for search param tables except for the special case table like Resource in case of Compartment search
                return;
            }

            if (resourceVersionType.HasFlag(ResourceVersionType.Latest) && !resourceVersionType.HasFlag(ResourceVersionType.History))
            {
                delimited.BeginDelimitedElement();
                StringBuilder.Append(VLatest.Resource.IsHistory, tableAlias).Append(" = 0 ");
            }
            else if (resourceVersionType.HasFlag(ResourceVersionType.History) && !resourceVersionType.HasFlag(ResourceVersionType.Latest))
            {
                delimited.BeginDelimitedElement();
                StringBuilder.Append(VLatest.Resource.IsHistory, tableAlias).Append(" = 1 ");
            }
        }

        private void AppendMinOrMax(in IndentedStringBuilder.DelimitedScope delimited, SearchOptions context)
        {
            if (_schemaInfo.Current < SchemaVersionConstants.AddMinMaxForDateAndStringSearchParamVersion)
            {
                return;
            }

            delimited.BeginDelimitedElement();
            if (context.Sort[0].sortOrder == SortOrder.Ascending)
            {
                StringBuilder.Append(VLatest.StringSearchParam.IsMin, tableAlias: null).Append(" = 1");
            }
            else if (context.Sort[0].sortOrder == SortOrder.Descending)
            {
                StringBuilder.Append(VLatest.StringSearchParam.IsMax, tableAlias: null).Append(" = 1");
            }
        }

        private void AddIncludeLimitCte(string resourceType, string cte)
        {
            _includeLimitCtesByResourceType ??= new Dictionary<string, List<string>>();
            List<string> ctes;
            if (!_includeLimitCtesByResourceType.TryGetValue(resourceType, out ctes))
            {
                ctes = new List<string>();
                _includeLimitCtesByResourceType.Add(resourceType, ctes);
            }

            if (!ctes.Contains(cte))
            {
                _includeLimitCtesByResourceType[resourceType].Add(cte);
            }
        }

        private bool TryGetIncludeCtes(string resourceType, out List<string> ctes)
        {
            if (_includeLimitCtesByResourceType == null)
            {
                ctes = null;
                return false;
            }

            return _includeLimitCtesByResourceType.TryGetValue(resourceType, out ctes);
        }

        private static bool IsPrimaryKeySort(SearchOptions searchOptions)
        {
            return searchOptions.Sort.All(s => s.searchParameterInfo.Name is SearchParameterNames.ResourceType or SearchParameterNames.LastUpdated);
        }

        private static bool IsScoreSort(SearchOptions searchOptions)
        {
            return searchOptions.Sort.Count > 0 && searchOptions.Sort[0].searchParameterInfo.Name == SearchParameterNames.Score;
        }

        internal bool IsSortValueNeeded(SearchOptions context)
        {
            if (context.Sort.Count == 0 || IsScoreSort(context))
            {
                return false;
            }

            if (IsPrimaryKeySort(context))
            {
                return false;
            }

            foreach (var searchParamTableExpression in _rootExpression.SearchParamTableExpressions)
            {
                if (searchParamTableExpression.Kind == SearchParamTableExpressionKind.Sort ||
                    searchParamTableExpression.Kind == SearchParamTableExpressionKind.SortWithFilter)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// We are looking for 3 conditions to add the OptimizeForUnknownClause:
        /// 1. Has an include expression
        /// 2. Has an identifier search
        /// 3. Has at least one more search parameter
        /// </summary>
        /// <returns>True if all condition are met</returns>
        private bool AddOptimizeForUnknownClause()
        {
            var hasInclude = _rootExpression.SearchParamTableExpressions.Any(t => t.Kind == SearchParamTableExpressionKind.Include);

            return hasInclude && _hasIdentifier && (_searchParamCount >= 2);
        }

        private void CheckForIdentifierSearchParams(Expression predicate)
        {
            var searchParameterExpressionPredicate = predicate as SearchParameterExpression;
            if (searchParameterExpressionPredicate != null)
            {
                _searchParamCount++;
                if (searchParameterExpressionPredicate.Parameter.Name == KnownQueryParameterNames.Identifier)
                {
                    _hasIdentifier = true;
                }
            }
        }

        private static SortContext GetSortRelatedDetails(SearchOptions context)
        {
            SortContext sortContext = new SortContext();
            SearchParameterInfo searchParamInfo = default;
            if (context.Sort?.Count > 0)
            {
                (searchParamInfo, sortContext.SortOrder) = context.Sort[0];
            }

            sortContext.ContinuationToken = ContinuationToken.FromString(context.ContinuationToken);

            switch (searchParamInfo.Type)
            {
                case ValueSets.SearchParamType.Date:
                    sortContext.SortColumnName = VLatest.DateTimeSearchParam.StartDateTime;
                    if (sortContext.ContinuationToken != null)
                    {
                        DateTime dateSortValue;
                        if (DateTime.TryParseExact(sortContext.ContinuationToken.SortValue, "o", null, DateTimeStyles.None, out dateSortValue))
                        {
                            sortContext.SortValue = dateSortValue;
                        }
                    }

                    break;
                case ValueSets.SearchParamType.String:
                    sortContext.SortColumnName = VLatest.StringSearchParam.Text;
                    if (sortContext.ContinuationToken != null)
                    {
                        sortContext.SortValue = sortContext.ContinuationToken.SortValue;
                    }

                    break;
            }

            return sortContext;
        }

        private static SearchParameterExpression CheckExpressionOrFirstChildIsSearchParam(Expression expression)
        {
            while (expression is MultiaryExpression)
            {
                expression = ((MultiaryExpression)expression).Expressions[0];
            }

            return expression as SearchParameterExpression;
        }

        /// <summary>
        /// A visitor to determine if there are any references to a search parameter in an expression.
        /// </summary>
        private class ExpressionContainsParameterVisitor : DefaultExpressionVisitor<string, bool>
        {
            public static readonly ExpressionContainsParameterVisitor Instance = new ExpressionContainsParameterVisitor();

            private ExpressionContainsParameterVisitor()
                : base((acc, curr) => acc || curr)
            {
            }

            public override bool VisitSearchParameter(SearchParameterExpression expression, string context) => string.Equals(expression.Parameter.Code, context, StringComparison.Ordinal);
        }

        internal class SortContext
        {
            public SortOrder SortOrder { get; set; }

            public ContinuationToken ContinuationToken { get; set; }

            public object SortValue { get; set; }

            public Column SortColumnName { get; set; }
        }
    }
}
