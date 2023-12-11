// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EnsureThat;
using Microsoft.Data.SqlClient;
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

        private string _cteMainSelect; // This is represents the CTE that is the main selector for use with includes
        private List<string> _includeCteIds;
        private Dictionary<string, List<string>> _includeLimitCtesByResourceType; // ctes of each include value, by their resource type

        // Include:iterate may be applied on results from multiple ctes
        private List<string> _includeFromCteIds;

        private int _curFromCteIndex = -1;
        private readonly SqlSearchType _searchType;
        private readonly string _searchParameterHash;
        private int _tableExpressionCounter = -1;
        private SqlRootExpression _rootExpression;
        private readonly SchemaInformation _schemaInfo;
        private bool _sortVisited = false;
        private bool _unionVisited = false;
        private bool _firstChainAfterUnionVisited = false;
        private HashSet<int> _cteToLimit = new HashSet<int>();
        private bool _hasIdentifier = false;
        private int _searchParamCount = 0;
        private bool previousSqlQueryGeneratorFailure = false;
        private int maxTableExpressionCountLimitForExists = 5;

        public SqlQueryGenerator(
            IndentedStringBuilder sb,
            HashingSqlQueryParameterManager parameters,
            ISqlServerFhirModel model,
            SqlSearchType searchType,
            SchemaInformation schemaInfo,
            string searchParameterHash,
            SqlException sqlException = null)
        {
            EnsureArg.IsNotNull(sb, nameof(sb));
            EnsureArg.IsNotNull(parameters, nameof(parameters));
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(schemaInfo, nameof(schemaInfo));

            StringBuilder = sb;
            Parameters = parameters;
            Model = model;
            _searchType = searchType;
            _schemaInfo = schemaInfo;
            _searchParameterHash = searchParameterHash;

            if (sqlException?.Number == SqlErrorCodes.QueryProcessorNoQueryPlan)
            {
                previousSqlQueryGeneratorFailure = true;
            }
        }

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

            if (expression.SearchParamTableExpressions.Count > 0)
            {
                if (expression.ResourceTableExpressions.Count > 0)
                {
                    throw new InvalidOperationException("Expected no predicates on the Resource table because of the presence of TableExpressions");
                }

                // Union expressions must be executed first than all other expressions. The overral idea is that Union All expressions will
                // filter the highest group of records, and the following expressions will be executed on top of this group of records.
                StringBuilder.Append("WITH ");
                StringBuilder.AppendDelimited($"{Environment.NewLine},", expression.SearchParamTableExpressions.SortExpressionsByQueryLogic(), (sb, tableExpression) =>
                {
                    if (tableExpression.SplitExpressions(out UnionExpression unionExpression, out SearchParamTableExpression allOtherRemainingExpressions))
                    {
                        AppendNewSetOfUnionAllTableExpressions(context, unionExpression, tableExpression.QueryGenerator);

                        if (allOtherRemainingExpressions != null)
                        {
                            StringBuilder.AppendLine(", ");
                            AppendNewTableExpression(sb, allOtherRemainingExpressions, ++_tableExpressionCounter, context);
                        }
                    }
                    else
                    {
                        AppendNewTableExpression(sb, tableExpression, ++_tableExpressionCounter, context);
                    }
                });

                StringBuilder.AppendLine();
            }

            if (Parameters.HasParametersToHash) // hash cannot be last comment as it will not be stored in query store
            {
                // Add a hash of (most of the) parameter values as a comment.
                // We do this to avoid re-using query plans unless two queries have
                // the same parameter values. We currently exclude from the hash parameters
                // that are related to TOP clauses or continuation tokens.
                // We can exclude more in the future.

                StringBuilder.Append("/* HASH ");
                Parameters.AppendHash(StringBuilder);
                StringBuilder.AppendLine(" */");
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
                StringBuilder.Append("FROM ").Append(VLatest.Resource).Append(" ").Append(resourceTableAlias);

                if (expression.SearchParamTableExpressions.Count == 0 &&
                    !_searchType.HasFlag(SqlSearchType.IncludeHistory) &&
                    !_searchType.HasFlag(SqlSearchType.IncludeDeleted) &&
                    expression.ResourceTableExpressions.Any(e => e.AcceptVisitor(ExpressionContainsParameterVisitor.Instance, SearchParameterNames.ResourceType)) &&
                    !expression.ResourceTableExpressions.Any(e => e.AcceptVisitor(ExpressionContainsParameterVisitor.Instance, SearchParameterNames.Id)))
                {
                    // If this is a simple search over a resource type (like GET /Observation)
                    // make sure the optimizer does not decide to do a scan on the clustered index, since we have an index specifically for this common case
                    StringBuilder.Append(" WITH (INDEX(").Append(VLatest.Resource.IX_Resource_ResourceTypeId_ResourceSurrgateId).AppendLine("))");
                }
                else
                {
                    StringBuilder.AppendLine();
                }

                if (expression.SearchParamTableExpressions.Count > 0)
                {
                    StringBuilder.Append("     JOIN ").Append(TableExpressionName(_tableExpressionCounter));
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

                    AppendHistoryClause(delimitedClause); // This does not hurt today, but will be neded with resource history separation

                    if (expression.SearchParamTableExpressions.Count == 0)
                    {
                        AppendDeletedClause(delimitedClause);
                        AppendSearchParameterHashClause(delimitedClause);
                    }
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

                    // if we have a complex query more than one SearchParemter, one of the parameters is "identifier", and we have an include
                    // then we will tell SQL to ignore the parameter values and base the query plan one the
                    // statistics only.  We have seen SQL make poor choices in this instance, so we are making a special case here
                    if (AddOptimizeForUnknownClause())
                    {
                        StringBuilder.AppendLine("  OPTION (OPTIMIZE FOR UNKNOWN)"); // TODO: Remove when TokemSearchParamHighCard is used
                    }
                }
            }
            else
            {
                // this is selecting only from the last CTE (for a count)
                StringBuilder.Append("FROM ").AppendLine(TableExpressionName(_tableExpressionCounter));
            }

            return null;
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
                        HandleTableKindAll(searchParamTableExpression);
                        break;

                    case SearchParamTableExpressionKind.NotExists:
                        HandleTableKindNotExists(searchParamTableExpression, context);
                        break;

                    case SearchParamTableExpressionKind.Top:
                        HandleTableKindTop(context);
                        break;

                    case SearchParamTableExpressionKind.Chain:
                        HandleTableKindChain(searchParamTableExpression, referenceSourceTableAlias, referenceTargetResourceTableAlias);
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
                        HandleParamTableUnion(searchParamTableExpression);
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

        private void HandleParamTableUnion(SearchParamTableExpression searchParamTableExpression)
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
                StringBuilder.Append("FROM ").AppendLine(new VLatest.ResourceTable());
            }
            else
            {
                StringBuilder.Append("FROM ").AppendLine(searchParamTableExpression.QueryGenerator.Table);
            }

            using (var delimited = StringBuilder.BeginDelimitedWhereClause())
            {
                AppendHistoryClause(delimited);

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
                        .Append("JOIN ").Append(cte)
                        .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, null).Append(" = ").Append(cte).Append(".T1")
                        .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, null).Append(" = ").Append(cte).AppendLine(".Sid1");
                }
            }
            else if (searchParamTableExpression.ChainLevel == 1 && _unionVisited)
            {
                var tableName = searchParamTableExpression.QueryGenerator.Table;

                // handle special case where we want to Union a specific resource to the results
                var searchParameterExpressionPredicate = CheckExpressionOrFirstChildIsSearchParam(searchParamTableExpression.Predicate);
                if (searchParameterExpressionPredicate != null &&
                    searchParameterExpressionPredicate.Parameter.ColumnLocation().HasFlag(SearchParameterColumnLocation.ResourceTable))
                {
                    tableName = new VLatest.ResourceTable();
                }

                StringBuilder.Append("SELECT T1, Sid1, ")
                    .Append(VLatest.Resource.ResourceTypeId, null).Append(" AS T2, ")
                    .Append(VLatest.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid2")
                    .Append("FROM ").AppendLine(tableName)
                    .Append("JOIN ").Append(TableExpressionName(FindRestrictingPredecessorTableExpressionIndex()))
                    .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, null).Append(" = ").Append(_firstChainAfterUnionVisited ? "T2" : "T1")
                    .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, null).Append(" = ").AppendLine(_firstChainAfterUnionVisited ? "Sid2" : "Sid1");

                // once we have visited a table after the union all, the remained of the inner joins
                // should be on T1 and SID1
                _firstChainAfterUnionVisited = true;
            }
            else
            {
                StringBuilder.Append("SELECT T1, Sid1, ")
                    .Append(VLatest.Resource.ResourceTypeId, null).AppendLine(" AS T2, ")
                    .Append(VLatest.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid2")
                    .Append("FROM ").AppendLine(searchParamTableExpression.QueryGenerator.Table)
                    .Append("JOIN ").Append(TableExpressionName(FindRestrictingPredecessorTableExpressionIndex()))
                    .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, null).Append(" = ").Append("T2")
                    .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, null).Append(" = ").AppendLine("Sid2");
            }

            if (CheckAppendWithJoin()
                && searchParamTableExpression.ChainLevel == 0 && !IsInSortMode(context))
            {
                AppendIntersectionWithPredecessorUsingInnerJoin(StringBuilder, searchParamTableExpression);
            }

            using (var delimited = StringBuilder.BeginDelimitedWhereClause())
            {
                AppendHistoryClause(delimited);

                if (searchParamTableExpression.ChainLevel == 0 && !IsInSortMode(context) && !CheckAppendWithJoin())
                {
                    // if chainLevel > 0 or if in sort mode or if we need to simplify the query, the intersection is already handled in a JOIN
                    AppendIntersectionWithPredecessor(delimited, searchParamTableExpression);
                }

                if (searchParamTableExpression.Predicate != null)
                {
                    delimited.BeginDelimitedElement();
                    CheckForIdentifierSearchParams(searchParamTableExpression.Predicate);
                    searchParamTableExpression.Predicate.AcceptVisitor(searchParamTableExpression.QueryGenerator, GetContext());
                }
            }
        }

        private void HandleTableKindAll(SearchParamTableExpression searchParamTableExpression)
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
                    .Append("JOIN ").Append(cte)
                    .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, null).Append(" = ").Append(cte).Append(".T1")
                    .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, null).Append(" = ").Append(cte).AppendLine(".Sid1");

                using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                {
                    AppendHistoryClause(delimited);
                    AppendDeletedClause(delimited);
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
                    AppendHistoryClause(delimited);
                    AppendDeletedClause(delimited);
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
                    AppendHistoryClause(delimited);

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
                .Append("JOIN ").Append(VLatest.Resource).Append(' ').Append(referenceTargetResourceTableAlias)
                .Append(" ON ").Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceSourceTableAlias).Append(" = ").Append(VLatest.Resource.ResourceTypeId, referenceTargetResourceTableAlias)
                .Append(" AND ").Append(VLatest.ReferenceSearchParam.ReferenceResourceId, referenceSourceTableAlias).Append(" = ").AppendLine(VLatest.Resource.ResourceId, referenceTargetResourceTableAlias);

            // For reverse chaining, if there is a parameter on the _id search parameter, we need another join to get the resource ID of the reference source (all we have is the surrogate ID at this point)

            bool expressionOnTargetHandledBySecondJoin = chainedExpression.ExpressionOnTarget != null && chainedExpression.Reversed && chainedExpression.ExpressionOnTarget.AcceptVisitor(ExpressionContainsParameterVisitor.Instance, SearchParameterNames.Id);

            if (expressionOnTargetHandledBySecondJoin)
            {
                const string referenceSourceResourceTableAlias = "refSourceResource";

                StringBuilder.Append("JOIN ").Append(VLatest.Resource).Append(' ').AppendLine(referenceSourceResourceTableAlias);

                using (var delimited = StringBuilder.BeginDelimitedOnClause())
                {
                    delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ResourceSurrogateId, referenceSourceTableAlias)
                        .Append(" = ").Append(VLatest.Resource.ResourceSurrogateId, referenceSourceResourceTableAlias);

                    delimited.BeginDelimitedElement();
                    chainedExpression.ExpressionOnTarget.AcceptVisitor(ResourceTableSearchParameterQueryGenerator.Instance, GetContext(referenceSourceResourceTableAlias));
                }
            }

            if (searchParamTableExpression.ChainLevel > 1)
            {
                StringBuilder.Append("JOIN ").Append(TableExpressionName(FindRestrictingPredecessorTableExpressionIndex()))
                    .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, chainedExpression.Reversed ? referenceTargetResourceTableAlias : referenceSourceTableAlias).Append(" = ").Append("T2")
                    .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, chainedExpression.Reversed ? referenceTargetResourceTableAlias : referenceSourceTableAlias).Append(" = ").AppendLine("Sid2");
            }

            // since we are in chain table expression, we know the Table is the ReferenceSearchParam table
            else if (CheckAppendWithJoin())
            {
                AppendIntersectionWithPredecessorUsingInnerJoin(StringBuilder, searchParamTableExpression, chainedExpression.Reversed ? referenceTargetResourceTableAlias : referenceSourceTableAlias);
            }

            using (var delimited = StringBuilder.BeginDelimitedWhereClause())
            {
                delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.SearchParamId, referenceSourceTableAlias)
                    .Append(" = ").Append(Parameters.AddParameter(VLatest.ReferenceSearchParam.SearchParamId, Model.GetSearchParamId(chainedExpression.ReferenceSearchParameter.Url), true));

                AppendHistoryClause(delimited, referenceTargetResourceTableAlias);
                AppendHistoryClause(delimited, referenceSourceTableAlias);

                delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ResourceTypeId, referenceSourceTableAlias)
                    .Append(" IN (")
                    .Append(string.Join(", ", chainedExpression.ResourceTypes.Select(x => Parameters.AddParameter(VLatest.ReferenceSearchParam.ResourceTypeId, Model.GetResourceTypeId(x), true))))
                    .Append(")");

                delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceSourceTableAlias)
                    .Append(" IN (")
                    .Append(string.Join(", ", chainedExpression.TargetResourceTypes.Select(x => Parameters.AddParameter(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, Model.GetResourceTypeId(x), true))))
                    .Append(")");

                if (searchParamTableExpression.ChainLevel == 1 && !CheckAppendWithJoin())
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

            if (includeExpression.Reversed)
            {
                // In case its revinclude, we limit the number of returned items as the resultset size is potentially
                // unbounded. we ask for +1 so in the limit expression we know if to mark at truncated...
                StringBuilder.Append("TOP (").Append(Parameters.AddParameter(context.IncludeCount + 1, includeInHash: false)).Append(") ");
            }

            var table = !includeExpression.Reversed ? referenceTargetResourceTableAlias : referenceSourceTableAlias;

            StringBuilder.Append(VLatest.Resource.ResourceTypeId, table).Append(" AS T1, ")
                .Append(VLatest.Resource.ResourceSurrogateId, table)
                .AppendLine(" AS Sid1, 0 AS IsMatch ");

            StringBuilder.Append("FROM ").Append(VLatest.ReferenceSearchParam).Append(' ').AppendLine(referenceSourceTableAlias)
                .Append("JOIN ").Append(VLatest.Resource).Append(' ').Append(referenceTargetResourceTableAlias)
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
                        delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceSourceTableAlias)
                            .Append(" IN (")
                            .Append(string.Join(", ", includeExpression.AllowedResourceTypesByScope.Select(x => Parameters.AddParameter(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, Model.GetResourceTypeId(x), true))))
                            .Append(")");
                    }
                }

                AppendHistoryClause(delimited, referenceTargetResourceTableAlias);
                AppendHistoryClause(delimited, referenceSourceTableAlias);

                AppendDeletedClause(delimited, referenceTargetResourceTableAlias);

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
                string fromCte = _cteMainSelect;
                if (includeExpression.Iterate)
                {
                    // Include Iterate
                    if (!includeExpression.Reversed)
                    {
                        // _include:iterate may appear without a preceding _include, in case of circular reference
                        // On that case, the fromCte is _cteMainSelect
                        if (TryGetIncludeCtes(includeExpression.SourceResourceType, out _includeFromCteIds))
                        {
                            fromCte = _includeFromCteIds[++_curFromCteIndex];
                        }
                    }

                    // RevInclude Iterate
                    else
                    {
                        if (includeExpression.TargetResourceType != null)
                        {
                            if (TryGetIncludeCtes(includeExpression.TargetResourceType, out _includeFromCteIds))
                            {
                                fromCte = _includeFromCteIds[++_curFromCteIndex];
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
                            fromCte = _includeFromCteIds.Count > 0 ? _includeFromCteIds[++_curFromCteIndex] : fromCte;
                        }
                    }
                }

                if (includeExpression.Reversed)
                {
                    delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ResourceTypeId, referenceSourceTableAlias)
                        .Append(" = ").Append(Parameters.AddParameter(VLatest.ReferenceSearchParam.ResourceTypeId, Model.GetResourceTypeId(includeExpression.SourceResourceType), true));
                }

                delimited.BeginDelimitedElement().Append("EXISTS (SELECT * FROM ").Append(fromCte)
                    .Append(" WHERE ").Append(VLatest.Resource.ResourceTypeId, table).Append(" = T1 AND ")
                    .Append(VLatest.Resource.ResourceSurrogateId, table).Append(" = Sid1");

                if (!includeExpression.Iterate)
                {
                    // Limit the join to the main select CTE.
                    // The main select will have max+1 items in the result set to account for paging, so we only want to join using the max amount.

                    StringBuilder.Append(" AND Row < ").Append(Parameters.AddParameter(context.MaxItemCount + 1, true));
                }

                StringBuilder.Append(")");
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

            // Handle Multiple Results sets to include from
            if (count > 1 && _curFromCteIndex >= 0 && _curFromCteIndex < count - 1)
            {
                StringBuilder.AppendLine("),");

                // If it's not the last result set, append a new IncludeLimit cte, since IncludeLimitCte was not created for the current cte
                if (_curFromCteIndex < count - 1)
                {
                    var cteToLimit = TableExpressionName(_tableExpressionCounter);
                    WriteIncludeLimitCte(cteToLimit, context);
                }

                // Generate CTE to include from the additional result sets
                StringBuilder.Append(TableExpressionName(++_tableExpressionCounter)).AppendLine(" AS").AppendLine("(");
                searchParamTableExpression.AcceptVisitor(this, context);
            }
            else
            {
                _curFromCteIndex = -1;

                if (includeExpression.WildCard)
                {
                    includeExpression.ReferencedTypes?.ToList().ForEach(t => AddIncludeLimitCte(t, curLimitCte));
                }
            }
        }

        private void HandleTableKindIncludeLimit(SearchOptions context)
        {
            StringBuilder.Append("SELECT DISTINCT ");

            // TODO - https://github.com/microsoft/fhir-server/issues/1309 (limit for _include also)
            var isRev = _cteToLimit.Contains(_tableExpressionCounter - 1);
            if (isRev)
            {
                // the related cte is a reverse include, limit the number of returned items and count to
                // see if we are over the threshold (to produce a warning to the client)
                StringBuilder.Append("TOP (").Append(Parameters.AddParameter(context.IncludeCount, includeInHash: false)).Append(") ");
            }

            StringBuilder.Append("T1, Sid1, IsMatch, ");

            if (isRev)
            {
                StringBuilder.Append("CASE WHEN count_big(*) over() > ")
                    .Append(Parameters.AddParameter(context.IncludeCount, true))
                    .AppendLine(" THEN 1 ELSE 0 END AS IsPartial ");
            }
            else
            {
                // if forward, just mark as not partial
                StringBuilder.AppendLine("0 AS IsPartial ");
            }

            StringBuilder.Append("FROM ").AppendLine(TableExpressionName(_tableExpressionCounter - 1));

            // the 'original' include cte is not in the union, but this new layer is instead
            _includeCteIds.Add(TableExpressionName(_tableExpressionCounter));
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

            StringBuilder.Append("FROM ").AppendLine(_cteMainSelect);

            foreach (var includeCte in _includeCteIds)
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
                    .Append(sortContext.SortColumnName, null).AppendLine(" as SortValue")
                    .Append("FROM ").AppendLine(searchParamTableExpression.QueryGenerator.Table);

                if (CheckAppendWithJoin())
                {
                    AppendIntersectionWithPredecessorUsingInnerJoin(StringBuilder, searchParamTableExpression);
                }

                using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                {
                    AppendHistoryClause(delimited);
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

                    if (!CheckAppendWithJoin())
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
                    .Append(sortContext.SortColumnName, null).AppendLine(" as SortValue")
                    .Append("FROM ").AppendLine(searchParamTableExpression.QueryGenerator.Table);

                if (CheckAppendWithJoin())
                {
                    AppendIntersectionWithPredecessorUsingInnerJoin(StringBuilder, searchParamTableExpression);
                }

                using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                {
                    AppendHistoryClause(delimited);
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

                    if (!CheckAppendWithJoin())
                    {
                        AppendIntersectionWithPredecessor(delimited, searchParamTableExpression);
                    }
                }
            }

            _sortVisited = true;
        }

        private void WriteIncludeLimitCte(string cteToLimit, SearchOptions context)
        {
            StringBuilder.Append(TableExpressionName(++_tableExpressionCounter)).AppendLine(" AS").AppendLine("(");

            // the related cte is a reverse include, limit the number of returned items and count to
            // see if we are over the threshold (to produce a warning to the client)
            StringBuilder.Append("SELECT DISTINCT ");
            StringBuilder.Append("TOP (").Append(Parameters.AddParameter(context.IncludeCount, true)).Append(") ");

            StringBuilder.Append("T1, Sid1, IsMatch, ");
            StringBuilder.Append("CASE WHEN count_big(*) over() > ")
                .Append(Parameters.AddParameter(context.IncludeCount, true))
                .AppendLine(" THEN 1 ELSE 0 END AS IsPartial ");

            StringBuilder.Append("FROM ").AppendLine(cteToLimit);
            StringBuilder.AppendLine("),");

            // the 'original' include cte is not in the union, but this new layer is instead
            _includeCteIds.Add(TableExpressionName(_tableExpressionCounter));
        }

        private SearchParameterQueryGeneratorContext GetContext(string tableAlias = null)
        {
            return new SearchParameterQueryGeneratorContext(StringBuilder, Parameters, Model, _schemaInfo, tableAlias);
        }

        private void AppendNewSetOfUnionAllTableExpressions(SearchOptions context, UnionExpression unionExpression, SearchParamTableExpressionQueryGenerator queryGenerator)
        {
            if (unionExpression.Operator != UnionOperator.All)
            {
                throw new ArgumentOutOfRangeException(unionExpression.Operator.ToString());
            }

            // Iterate through all expressions and create a unique CTE for each one.
            int firstInclusiveTableExpressionId = _tableExpressionCounter + 1;
            foreach (Expression innerExpression in unionExpression.Expressions)
            {
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

            _unionVisited = true;
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

        private bool CheckAppendWithJoin()
        {
            // if either:
            // 1. the number of table expressions is greater than the limit indicating a complex query
            // 2. the previous query generator failed to generate a query
            // then we will use the EXISTS clause instead of the inner join
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
                sb.Append("JOIN " + TableExpressionName(predecessorIndex - 0))
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

        private void AppendDeletedClause(in IndentedStringBuilder.DelimitedScope delimited, string tableAlias = null)
        {
            if (!_searchType.HasFlag(SqlSearchType.IncludeDeleted))
            {
                delimited.BeginDelimitedElement();

                StringBuilder.Append(VLatest.Resource.IsDeleted, tableAlias).Append(" = 0 ");
            }
        }

        private void AppendHistoryClause(in IndentedStringBuilder.DelimitedScope delimited, string tableAlias = null)
        {
            if (!_searchType.HasFlag(SqlSearchType.IncludeHistory))
            {
                delimited.BeginDelimitedElement();

                StringBuilder.Append(VLatest.Resource.IsHistory, tableAlias).Append(" = 0 ");
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

        private void AppendSearchParameterHashClause(in IndentedStringBuilder.DelimitedScope delimited, string tableAlias = null)
        {
            if (_searchType.HasFlag(SqlSearchType.Reindex))
            {
                delimited.BeginDelimitedElement();

                StringBuilder.Append("(").Append(VLatest.Resource.SearchParamHash, tableAlias).Append(" != ").Append(Parameters.AddParameter(_searchParameterHash, true)).Append(" OR ").Append(VLatest.Resource.SearchParamHash, tableAlias).Append(" IS NULL)");
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

        private bool IsSortValueNeeded(SearchOptions context)
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
