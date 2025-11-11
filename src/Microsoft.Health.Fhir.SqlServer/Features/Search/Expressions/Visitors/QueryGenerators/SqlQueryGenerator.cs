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
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Storage;
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

        private string _cteMainSelect; // This is represents the CTE that is the main selector for use with includes
        private List<string> _includeCteIds;
        private Dictionary<string, List<string>> _includeLimitCtesByResourceType; // ctes of each include value, by their resource type

        // Include:iterate may be applied on results from multiple ctes
        private List<string> _includeFromCteIds;

        private int _tableExpressionCounter = -1;
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
                StringBuilder.AppendLine("DECLARE @FilteredDataSmartV2Union AS TABLE (T1 smallint, Sid1 bigint)");
                StringBuilder.AppendLine(";WITH");
                StringBuilder.AppendDelimited($"{Environment.NewLine},", expression.SearchParamTableExpressions.SortExpressionsByQueryLogic(), (sb, tableExpression) =>
                {
                    if (tableExpression.SplitExpressions(out UnionExpression unionExpression, out SearchParamTableExpression allOtherRemainingExpressions))
                    {
                        if (ContainsSmartV2UnionFlag(unionExpression))
                        {
                            // Union expressions for smart v2 scopes with search parameters needs to be handled differently
                            AppendSmartNewSetOfUnionAllTableExpressions(context, unionExpression, tableExpression.QueryGenerator);
                            if (hasIncludeExpressions)
                            {
                                sb.AppendLine();
                                sb.AppendLine($"INSERT INTO @FilteredDataSmartV2Union SELECT T1, Sid1 FROM cte{_tableExpressionCounter}");
                                AddOptionClause();
                                sb.AppendLine($";WITH cte{_tableExpressionCounter} AS (SELECT * FROM @FilteredDataSmartV2Union)");
                            }

                            _smartV2UnionVisited = true;
                        }
                        else
                        {
                            AppendNewSetOfUnionAllTableExpressions(context, unionExpression, tableExpression.QueryGenerator);
                        }

                        if (allOtherRemainingExpressions != null)
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
                            AddHash(); // hash is required in upper SQL
                            sb.AppendLine($"INSERT INTO @FilteredData SELECT T1, Sid1, IsMatch, IsPartial, Row{(isSortValueNeeded ? ", SortValue " : " ")}FROM cte{_tableExpressionCounter}");
                            AddOptionClause();
                            sb.AppendLine($";WITH cte{_tableExpressionCounter} AS (SELECT * FROM @FilteredData)");
                            sb.Append(","); // add comma back
                            visitedInclude = true;
                        }

                        AppendNewTableExpression(sb, tableExpression, ++_tableExpressionCounter, context);
                    }
                });

                StringBuilder.AppendLine();
            }

            if (!visitedInclude)
            {
                AddHash(); // for include and rev-include we already added hash for all filtering conditions to the filter query
            }

            string resourceTableAlias = "r";
            bool selectingFromResourceTable;

            if (searchOptions.CountOnly)
            {
                if (expression.SearchParamTableExpressions.Count > 0)
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

                // DISTINCT is used since different ctes may return the same resources due to _include and _include:iterate search parameters
                StringBuilder.Append("SELECT DISTINCT ");

                if (expression.SearchParamTableExpressions.Count == 0)
                {
                    StringBuilder.Append("TOP (").Append(Parameters.AddParameter(context.MaxItemCount + 1, includeInHash: false)).Append(") ");
                }

                StringBuilder.Append(VLatest.Resource.ResourceTypeId, resourceTableAlias).Append(", ")
                    .Append(VLatest.Resource.ResourceId, resourceTableAlias).Append(", ")
                    .Append(VLatest.Resource.Version, resourceTableAlias).Append(", ")
                    .Append(VLatest.Resource.IsDeleted, resourceTableAlias).Append(", ")
                    .Append(VLatest.Resource.ResourceSurrogateId, resourceTableAlias).Append(", ")
                    .Append(VLatest.Resource.RequestMethod, resourceTableAlias).Append(", ");

                // If there's a table expression, use the previously selected bit, otherwise everything in the select is considered a match
                StringBuilder.Append(expression.SearchParamTableExpressions.Count > 0 ? "CAST(IsMatch AS bit) AS IsMatch, " : "CAST(1 AS bit) AS IsMatch, ");
                StringBuilder.Append(expression.SearchParamTableExpressions.Count > 0 ? "CAST(IsPartial AS bit) AS IsPartial, " : "CAST(0 AS bit) AS IsPartial, ");

                StringBuilder.Append(VLatest.Resource.IsRawResourceMetaSet, resourceTableAlias).Append(", ");

                if (_schemaInfo.Current >= SchemaVersionConstants.SearchParameterHashSchemaVersion)
                {
                    StringBuilder.Append(VLatest.Resource.SearchParamHash, resourceTableAlias).Append(", ");
                }

                StringBuilder.Append(VLatest.Resource.RawResource, resourceTableAlias);

                if (IsSortValueNeeded(context))
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

                using (var delimitedClause = StringBuilder.BeginDelimitedWhereClause())
                {
                    foreach (var denormalizedPredicate in expression.ResourceTableExpressions)
                    {
                        delimitedClause.BeginDelimitedElement();
                        denormalizedPredicate.AcceptVisitor(ResourceTableSearchParameterQueryGenerator.Instance, GetContext());
                    }

                    AppendHistoryClause(delimitedClause, context.ResourceVersionTypes);

                    AppendDeletedClause(delimitedClause, context.ResourceVersionTypes);
                }

                if (!searchOptions.CountOnly)
                {
                    StringBuilder.Append("ORDER BY ");

                    if (_rootExpression.SearchParamTableExpressions.Any(t => t.Kind == SearchParamTableExpressionKind.Include))
                    {
                        // ensure the matches appear before includes
                        StringBuilder.Append("IsMatch DESC, ");
                    }

                    if (IsPrimaryKeySort(searchOptions))
                    {
                        StringBuilder.AppendDelimited(", ", searchOptions.Sort, (sb, sort) =>
                        {
                            Column column = sort.searchParameterInfo.Name switch
                            {
                                SearchParameterNames.ResourceType => VLatest.Resource.ResourceTypeId,
                                SearchParameterNames.LastUpdated => VLatest.Resource.ResourceSurrogateId,
                                _ => throw new InvalidOperationException($"Unexpected sort parameter {sort.searchParameterInfo.Name}"),
                            };
                            sb.Append(column, resourceTableAlias).Append(" ").Append(sort.sortOrder == SortOrder.Ascending ? "ASC" : "DESC");
                        })
                            .AppendLine();
                    }
                    else if (IsSortValueNeeded(searchOptions))
                    {
                        StringBuilder
                            .Append(TableExpressionName(_tableExpressionCounter))
                            .Append(".SortValue ")
                            .Append(searchOptions.Sort[0].sortOrder == SortOrder.Ascending ? "ASC" : "DESC").Append(", ")
                            .Append(VLatest.Resource.ResourceSurrogateId, resourceTableAlias).AppendLine(" ASC ");
                    }
                    else
                    {
                        StringBuilder
                            .Append(VLatest.Resource.ResourceSurrogateId, resourceTableAlias).AppendLine(" ASC ");
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

        private void AddHash()
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

                StringBuilder.Append("/* HASH ");
                Parameters.AppendHash(StringBuilder);
                Parameters.AppendHashedParameterNames(StringBuilder);
                StringBuilder.AppendLine(" */");
            }
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
            StringBuilder.Append(TableExpressionName(++_tableExpressionCounter)).AppendLine(" AS").AppendLine("(");

            StringBuilder.Append("SELECT ")
                .Append(VLatest.Resource.ResourceTypeId, null).Append(" AS T1, ")
                .Append(VLatest.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid1");

            var searchParameterExpressionPredicate = searchParamTableExpression.Predicate as SearchParameterExpression;

            // handle special case where we want to Union a specific resource to the results
            if (searchParameterExpressionPredicate != null &&
                searchParameterExpressionPredicate.Parameter.ColumnLocation().HasFlag(SearchParameterColumnLocation.ResourceTable))
            {
                StringBuilder.Append("FROM ").AppendLine(VLatest.Resource);
            }
            else
            {
                StringBuilder.Append("FROM ").AppendLine(searchParamTableExpression.QueryGenerator.Table);
            }

            using (var delimited = StringBuilder.BeginDelimitedWhereClause())
            {
                AppendHistoryClause(delimited, context.ResourceVersionTypes, searchParamTableExpression);

                if (searchParamTableExpression.Predicate != null)
                {
                    delimited.BeginDelimitedElement();
                    searchParamTableExpression.Predicate.AcceptVisitor(searchParamTableExpression.QueryGenerator, GetContext());
                }
            }

            StringBuilder.AppendLine("),");
        }

        private void HandleTableKindNormal(SearchParamTableExpression searchParamTableExpression, SearchOptions context)
        {
            var tableAlias = "predecessorTable";
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
                        .Append("FROM ").AppendLine($"{searchParamTableExpression.QueryGenerator.Table} {tableAlias}");
                }
                else
                {
                    // we are in sort mode and we need to join with previous cte to propagate the SortValue
                    var cte = TableExpressionName(predecessorIndex);
                    StringBuilder.Append("SELECT ")
                        .Append(VLatest.Resource.ResourceTypeId, null).Append(" AS T1, ")
                        .Append(VLatest.Resource.ResourceSurrogateId, null).Append(" AS Sid1, ")
                        .Append(cte).AppendLine(".SortValue")
                        .Append("FROM ").AppendLine($"{searchParamTableExpression.QueryGenerator.Table} {tableAlias}")
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
                    .Append("FROM ").AppendLine($"{specialCaseTableName} {tableAlias}")
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
                    .Append("FROM ").AppendLine($"{searchParamTableExpression.QueryGenerator.Table} {tableAlias}")
                    .Append(_joinShift).Append("JOIN ").Append(TableExpressionName(FindRestrictingPredecessorTableExpressionIndex()))
                    .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, null).Append(" = ").Append("T2")
                    .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, null).Append(" = ").AppendLine("Sid2");
            }

            if (UseAppendWithJoin()
                && searchParamTableExpression.ChainLevel == 0 && !IsInSortMode(context) && !context.SkipAppendIntersectionWithPredecessor)
            {
                AppendIntersectionWithPredecessorUsingInnerJoin(StringBuilder, searchParamTableExpression, tableAlias);
            }

            using (var delimited = StringBuilder.BeginDelimitedWhereClause())
            {
                AppendHistoryClause(delimited, context.ResourceVersionTypes, searchParamTableExpression, tableAlias, specialCaseTableName);

                // For smart request when we have union of all scopes ANDed with their respective search parameters
                // Like (ResourceType = x and searchParam1 = foo) Intersect (ResourceType = x and searchParam2 = doo) UNION (ResourceType = y and searchParam3 = goo) Intersect (ResourceType = y and searchParam4 = woo)
                // To get the intersection we need to AppendIntersectionWithPredecessor
                if (searchParamTableExpression.ChainLevel == 0 && !IsInSortMode(context) && !UseAppendWithJoin())
                {
                    if (!context.SkipAppendIntersectionWithPredecessor)
                    {
                        // if chainLevel > 0 or if in sort mode or if we need to simplify the query, the intersection is already handled in a JOIN
                        AppendIntersectionWithPredecessor(delimited, searchParamTableExpression, tableAlias);
                    }
                }

                if (searchParamTableExpression.Predicate != null)
                {
                    delimited.BeginDelimitedElement();
                    CheckForIdentifierSearchParams(searchParamTableExpression.Predicate);
                    searchParamTableExpression.Predicate.AcceptVisitor(searchParamTableExpression.QueryGenerator, GetContext(tableAlias));
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
            if (!context.IsIncludesOperation)
            {
                StringBuilder.AppendLine(" AS Sid1, 0 AS IsMatch ");
            }
            else
            {
                StringBuilder.AppendLine(" AS Sid1, 0 AS IsMatch, 0 AS IsPartial ");
            }

            StringBuilder.Append("FROM ").Append(VLatest.ReferenceSearchParam).Append(' ').AppendLine(referenceSourceTableAlias)
                .Append(_joinShift).Append("JOIN ").Append(VLatest.Resource).Append(' ').Append(referenceTargetResourceTableAlias)
                .Append(" ON ").Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceSourceTableAlias).Append(" = ").Append(VLatest.Resource.ResourceTypeId, referenceTargetResourceTableAlias)
                .Append(" AND ").Append(VLatest.ReferenceSearchParam.ReferenceResourceId, referenceSourceTableAlias).Append(" = ").AppendLine(VLatest.Resource.ResourceId, referenceTargetResourceTableAlias);

            using (var delimited = StringBuilder.BeginDelimitedWhereClause())
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
                        scopeForSmartV2.Append("SELECT * FROM @FilteredDataSmartV2Union")
                            .Append(" WHERE ").Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceSourceTableAlias).Append(" = T1 AND ")
                            .Append(VLatest.Resource.ResourceSurrogateId, referenceTargetResourceTableAlias).Append(" = Sid1)");
                    }
                    else
                    {
                        var scopeForSmartV2 = delimited.BeginDelimitedElement();
                        scopeForSmartV2.Append("EXISTS (");
                        scopeForSmartV2.Append("SELECT * FROM @FilteredDataSmartV2Union")
                            .Append(" WHERE ").Append(VLatest.ReferenceSearchParam.ResourceTypeId, referenceSourceTableAlias).Append(" = T1 AND ")
                            .Append(VLatest.ReferenceSearchParam.ResourceSurrogateId, referenceSourceTableAlias).Append(" = Sid1)");
                    }
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
            if (sortValueNeeded)
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
                if (sortValueNeeded)
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
                StringBuilder.Append("SELECT * FROM ").Append(TableExpressionName(tableExpressionId));

                if (tableExpressionId < lastInclusiveTableExpressionId)
                {
                    StringBuilder.AppendLine(" UNION ALL");
                }
            }

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

        private void AppendSmartNewSetOfUnionAllTableExpressions(SearchOptions context, UnionExpression unionExpression, SearchParamTableExpressionQueryGenerator defaultQueryGenerator)
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
                        childSearchParamExpression.AcceptVisitor(this, context);
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
            foreach (int tableExpressionId in lastAndedCTEs)
            {
                StringBuilder.Append("SELECT * FROM ").Append(TableExpressionName(tableExpressionId));

                if (tableExpressionId < lastInclusiveTableExpressionId)
                {
                    StringBuilder.AppendLine(" UNION ALL");
                }
            }

            StringBuilder.Append(")");

            _unionVisited = true;
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

        internal bool IsSortValueNeeded(SearchOptions context)
        {
            if (context.Sort.Count == 0)
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

        /// <summary>
        /// Recursively checks whether the given expression or any of its descendant expressions
        /// has the <see cref="Expression.IsSmartV2UnionExpressionForScopesSearchParameters"/> flag set to true.
        /// </summary>
        /// <param name="expression">The root expression to search.</param>
        /// <returns>True if any expression in the tree has the flag; otherwise, false.</returns>
        private static bool ContainsSmartV2UnionFlag(Expression expression)
        {
            if (expression == null)
            {
                return false;
            }

            // If this expression has the flag, return true.
            if (expression.IsSmartV2UnionExpressionForScopesSearchParameters)
            {
                return true;
            }

            // Check if expression can contain child expressions.
            if (expression is IExpressionsContainer container)
            {
                return container.Expressions.Any(ContainsSmartV2UnionFlag);
            }

            return false;
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
