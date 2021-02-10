// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class SqlQueryGenerator : DefaultSqlExpressionVisitor<SearchOptions, object>
    {
        private string _cteMainSelect; // This is represents the CTE that is the main selector for use with includes
        private List<string> _includeCteIds;
        private Dictionary<string, List<string>> _includeLimitCtesByResourceType; // ctes of each include value, by their resource type

        // Include:iterate may be applied on results from multiple ctes
        private List<string> _includeFromCteIds;

        private int _curFromCteIndex = -1;
        private readonly bool _isHistorySearch;
        private int _tableExpressionCounter = -1;
        private SqlRootExpression _rootExpression;
        private readonly SchemaInformation _schemaInfo;
        private bool _sortVisited = false;
        private HashSet<int> _cteToLimit = new HashSet<int>();

        public SqlQueryGenerator(IndentedStringBuilder sb, SqlQueryParameterManager parameters, SqlServerFhirModel model, bool isHistorySearch, SchemaInformation schemaInfo)
        {
            EnsureArg.IsNotNull(sb, nameof(sb));
            EnsureArg.IsNotNull(parameters, nameof(parameters));
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(schemaInfo, nameof(schemaInfo));

            StringBuilder = sb;
            Parameters = parameters;
            Model = model;
            _isHistorySearch = isHistorySearch;
            _schemaInfo = schemaInfo;
        }

        public IndentedStringBuilder StringBuilder { get; }

        public SqlQueryParameterManager Parameters { get; }

        public SqlServerFhirModel Model { get; }

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

                StringBuilder.Append("WITH ");

                StringBuilder.AppendDelimited($",{Environment.NewLine}", expression.SearchParamTableExpressions, (sb, tableExpression) =>
                {
                    sb.Append(TableExpressionName(++_tableExpressionCounter)).AppendLine(" AS").AppendLine("(");

                    using (sb.Indent())
                    {
                        tableExpression.AcceptVisitor(this, context);
                    }

                    sb.Append(")");
                });

                StringBuilder.AppendLine();
            }

            string resourceTableAlias = "r";
            bool selectingFromResourceTable;
            var (searchParamInfo, sortOrder) = searchOptions.Sort.Count == 0 ? default : searchOptions.Sort[0];

            if (searchOptions.CountOnly)
            {
                if (expression.SearchParamTableExpressions.Count > 0)
                {
                    // The last CTE has all the surrogate IDs that match the results.
                    // We just need to count those and don't need to join with the Resource table
                    selectingFromResourceTable = false;
                    StringBuilder.AppendLine("SELECT COUNT(DISTINCT Sid1)");
                }
                else
                {
                    // We will be counting over the Resource table.
                    selectingFromResourceTable = true;
                    StringBuilder.AppendLine("SELECT COUNT(*)");
                }
            }
            else
            {
                selectingFromResourceTable = true;

                // DISTINCT is used since different ctes may return the same resources due to _include and _include:iterate search parameters
                StringBuilder.Append("SELECT DISTINCT ");

                if (expression.SearchParamTableExpressions.Count == 0)
                {
                    StringBuilder.Append("TOP (").Append(Parameters.AddParameter(context.MaxItemCount + 1)).Append(") ");
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
                if (searchParamInfo != null && searchParamInfo.Code != KnownQueryParameterNames.LastUpdated)
                {
                    StringBuilder.Append(", ").Append(TableExpressionName(_tableExpressionCounter)).Append(".SortValue");
                }

                StringBuilder.AppendLine();
            }

            if (selectingFromResourceTable)
            {
                StringBuilder.Append("FROM ").Append(VLatest.Resource).Append(" ").Append(resourceTableAlias);

                if (expression.SearchParamTableExpressions.Count == 0 &&
                    !_isHistorySearch &&
                    expression.ResourceTableExpressions.Any(e => e.AcceptVisitor(ExpressionContainsParameterVisitor.Instance, SearchParameterNames.ResourceType)) &&
                    !expression.ResourceTableExpressions.Any(e => e.AcceptVisitor(ExpressionContainsParameterVisitor.Instance, SearchParameterNames.Id)))
                {
                    // If this is a simple search over a resource type (like GET /Observation)
                    // make sure the optimizer does not decide to do a scan on the clustered index, since we have an index specifically for this common case
                    StringBuilder.Append(" WITH(INDEX(").Append(VLatest.Resource.IX_Resource_ResourceTypeId_ResourceSurrgateId).AppendLine("))");
                }
                else
                {
                    StringBuilder.AppendLine();
                }

                if (expression.SearchParamTableExpressions.Count > 0)
                {
                    StringBuilder.AppendLine().Append("INNER JOIN ").AppendLine(TableExpressionName(_tableExpressionCounter));
                    StringBuilder.Append("ON ").Append(VLatest.Resource.ResourceSurrogateId, resourceTableAlias).Append(" = ").Append(TableExpressionName(_tableExpressionCounter)).AppendLine(".Sid1");
                }

                using (var delimitedClause = StringBuilder.BeginDelimitedWhereClause())
                {
                    foreach (var denormalizedPredicate in expression.ResourceTableExpressions)
                    {
                        delimitedClause.BeginDelimitedElement();
                        denormalizedPredicate.AcceptVisitor(ResourceTableSearchParameterQueryGenerator.Instance, GetContext());
                    }

                    if (expression.SearchParamTableExpressions.Count == 0)
                    {
                        AppendHistoryClause(delimitedClause);
                        AppendDeletedClause(delimitedClause);
                    }
                }

                if (!searchOptions.CountOnly)
                {
                    StringBuilder.Append("ORDER BY ");
                    if (searchParamInfo == null || searchParamInfo.Code == KnownQueryParameterNames.LastUpdated)
                    {
                        StringBuilder
                            .Append(VLatest.Resource.ResourceSurrogateId, resourceTableAlias).Append(" ")
                            .AppendLine(sortOrder == SortOrder.Ascending ? "ASC" : "DESC");
                    }
                    else
                    {
                        StringBuilder
                            .Append($"{TableExpressionName(_tableExpressionCounter)}.SortValue ")
                            .Append(sortOrder == SortOrder.Ascending ? "ASC" : "DESC").Append(", ")
                            .Append(VLatest.Resource.ResourceSurrogateId, resourceTableAlias).AppendLine(" ASC ");
                    }
                }
            }
            else
            {
                // this is selecting only from the last CTE (for a count)
                StringBuilder.Append("FROM ").AppendLine(TableExpressionName(_tableExpressionCounter));
            }

            StringBuilder.Append("OPTION(RECOMPILE)");

            return null;
        }

        private static string TableExpressionName(int id) => "cte" + id;

        private bool IsInSortMode(SearchOptions context) => context.Sort != null && context.Sort.Count > 0 && _sortVisited;

        public override object VisitTable(SearchParamTableExpression searchParamTableExpression, SearchOptions context)
        {
            const string referenceSourceTableAlias = "refSource";
            const string referenceTargetResourceTableAlias = "refTarget";

            switch (searchParamTableExpression.Kind)
            {
                case SearchParamTableExpressionKind.Normal:

                    if (searchParamTableExpression.ChainLevel == 0)
                    {
                        int predecessorIndex = FindRestrictingPredecessorTableExpressionIndex();

                        // if this is not sort mode or if it is the first cte
                        if (!IsInSortMode(context) || predecessorIndex < 0)
                        {
                            StringBuilder.Append("SELECT ").Append(VLatest.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid1")
                                .Append("FROM ").AppendLine(searchParamTableExpression.QueryGenerator.Table);
                        }
                        else
                        {
                            // we are in sort mode and we need to join with previous cte to propagate the SortValue
                            var cte = TableExpressionName(predecessorIndex);
                            StringBuilder.Append("SELECT ").Append(VLatest.Resource.ResourceSurrogateId, null).Append(" AS Sid1, ")
                                .Append(cte).AppendLine(".SortValue")
                                .Append("FROM ").AppendLine(searchParamTableExpression.QueryGenerator.Table)
                                .Append("INNER JOIN ").AppendLine(cte);

                            using (var delimited = StringBuilder.BeginDelimitedOnClause())
                            {
                                delimited.BeginDelimitedElement().Append(VLatest.Resource.ResourceSurrogateId, null).Append(" = ").Append(cte).Append(".Sid1");
                            }
                        }
                    }
                    else
                    {
                        StringBuilder.Append("SELECT Sid1, ").Append(VLatest.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid2")
                            .Append("FROM ").AppendLine(searchParamTableExpression.QueryGenerator.Table)
                            .Append("INNER JOIN ").AppendLine(TableExpressionName(FindRestrictingPredecessorTableExpressionIndex()));

                        using (var delimited = StringBuilder.BeginDelimitedOnClause())
                        {
                            delimited.BeginDelimitedElement().Append(VLatest.Resource.ResourceSurrogateId, null).Append(" = ").Append("Sid2");
                        }
                    }

                    using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                    {
                        AppendHistoryClause(delimited);

                        if (searchParamTableExpression.ChainLevel == 0 && !IsInSortMode(context))
                        {
                            // if chainLevel > 0 or if in sort mode, the intersection is already handled in the JOIN
                            AppendIntersectionWithPredecessor(delimited, searchParamTableExpression);
                        }

                        if (searchParamTableExpression.Predicate != null)
                        {
                            delimited.BeginDelimitedElement();
                            searchParamTableExpression.Predicate.AcceptVisitor(searchParamTableExpression.QueryGenerator, GetContext());
                        }
                    }

                    break;

                case SearchParamTableExpressionKind.Concatenation:
                    StringBuilder.Append("SELECT * FROM ").AppendLine(TableExpressionName(_tableExpressionCounter - 1));
                    StringBuilder.AppendLine("UNION ALL");

                    goto case SearchParamTableExpressionKind.Normal;

                case SearchParamTableExpressionKind.All:
                    StringBuilder.Append("SELECT ").Append(VLatest.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid1")
                        .Append("FROM ").AppendLine(VLatest.Resource);

                    using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                    {
                        AppendHistoryClause(delimited);
                        AppendDeletedClause(delimited);
                        if (searchParamTableExpression.Predicate != null)
                        {
                            delimited.BeginDelimitedElement();
                            searchParamTableExpression.Predicate?.AcceptVisitor(ResourceTableSearchParameterQueryGenerator.Instance, GetContext());
                        }
                    }

                    break;

                case SearchParamTableExpressionKind.NotExists:
                    StringBuilder.Append("SELECT Sid1");
                    StringBuilder.AppendLine(context.Sort?.Count > 0 ? ", SortValue" : string.Empty);
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
                    break;

                case SearchParamTableExpressionKind.Top:
                    var (paramInfo, sortOrder) = context.Sort.Count == 0 ? default : context.Sort[0];
                    var tableExpressionName = TableExpressionName(_tableExpressionCounter - 1);
                    var sortExpression = (paramInfo == null || paramInfo.Code == KnownQueryParameterNames.LastUpdated) ? null : $"{tableExpressionName}.SortValue";

                    // Everything in the top expression is considered a match
                    StringBuilder.Append("SELECT DISTINCT TOP (").Append(Parameters.AddParameter(context.MaxItemCount + 1)).Append(") Sid1, 1 AS IsMatch, 0 AS IsPartial ")
                        .AppendLine(sortExpression == null ? string.Empty : $", {sortExpression}")
                        .Append("FROM ").AppendLine(tableExpressionName)
                        .AppendLine($"ORDER BY {(sortExpression == null ? string.Empty : $"{sortExpression} {(sortOrder == SortOrder.Ascending ? "ASC" : "DESC")}, ")} Sid1 {((sortExpression != null || sortOrder == SortOrder.Ascending) ? "ASC" : "DESC")}");

                    // For any includes, the source of the resource surrogate ids to join on is saved
                    _cteMainSelect = TableExpressionName(_tableExpressionCounter);

                    break;

                case SearchParamTableExpressionKind.Chain:
                    var chainedExpression = (SqlChainLinkExpression)searchParamTableExpression.Predicate;

                    StringBuilder.Append("SELECT ");
                    if (searchParamTableExpression.ChainLevel == 1)
                    {
                        StringBuilder.Append(VLatest.ReferenceSearchParam.ResourceSurrogateId, referenceSourceTableAlias).Append(" AS ").Append(chainedExpression.Reversed ? "Sid2" : "Sid1").Append(", ");
                    }
                    else
                    {
                        StringBuilder.Append("Sid1, ");
                    }

                    StringBuilder.Append(VLatest.Resource.ResourceSurrogateId, chainedExpression.Reversed && searchParamTableExpression.ChainLevel > 1 ? referenceSourceTableAlias : referenceTargetResourceTableAlias).Append(" AS ").AppendLine(chainedExpression.Reversed && searchParamTableExpression.ChainLevel == 1 ? "Sid1 " : "Sid2 ")
                        .Append("FROM ").Append(VLatest.ReferenceSearchParam).Append(' ').AppendLine(referenceSourceTableAlias)
                        .Append("INNER JOIN ").Append(VLatest.Resource).Append(' ').AppendLine(referenceTargetResourceTableAlias);

                    using (var delimited = StringBuilder.BeginDelimitedOnClause())
                    {
                        delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceSourceTableAlias)
                            .Append(" = ").Append(VLatest.Resource.ResourceTypeId, referenceTargetResourceTableAlias);

                        delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ReferenceResourceId, referenceSourceTableAlias)
                            .Append(" = ").Append(VLatest.Resource.ResourceId, referenceTargetResourceTableAlias);
                    }

                    // For reverse chaining, if there is a parameter on the _id search parameter, we need another join to get the resource ID of the reference source (all we have is the surrogate ID at this point)

                    bool expressionOnTargetHandledBySecondJoin = chainedExpression.ExpressionOnTarget != null && chainedExpression.Reversed && chainedExpression.ExpressionOnTarget.AcceptVisitor(ExpressionContainsParameterVisitor.Instance, SearchParameterNames.Id);

                    if (expressionOnTargetHandledBySecondJoin)
                    {
                        const string referenceSourceResourceTableAlias = "refSourceResource";

                        StringBuilder.Append("INNER JOIN ").Append(VLatest.Resource).Append(' ').AppendLine(referenceSourceResourceTableAlias);

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
                        StringBuilder.Append("INNER JOIN ").AppendLine(TableExpressionName(FindRestrictingPredecessorTableExpressionIndex()));

                        using (var delimited = StringBuilder.BeginDelimitedOnClause())
                        {
                            delimited.BeginDelimitedElement().Append(VLatest.Resource.ResourceSurrogateId, chainedExpression.Reversed ? referenceTargetResourceTableAlias : referenceSourceTableAlias).Append(" = ").Append("Sid2");
                        }
                    }

                    using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                    {
                        delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.SearchParamId, referenceSourceTableAlias)
                            .Append(" = ").Append(Parameters.AddParameter(VLatest.ReferenceSearchParam.SearchParamId, Model.GetSearchParamId(chainedExpression.ReferenceSearchParameter.Url)));

                        AppendHistoryClause(delimited, referenceTargetResourceTableAlias);
                        AppendHistoryClause(delimited, referenceSourceTableAlias);

                        delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ResourceTypeId, referenceSourceTableAlias)
                            .Append(" IN (")
                            .Append(string.Join(", ", chainedExpression.ResourceTypes.Select(x => Parameters.AddParameter(VLatest.ReferenceSearchParam.ResourceTypeId, Model.GetResourceTypeId(x)))))
                            .Append(")");

                        delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceSourceTableAlias)
                            .Append(" IN (")
                            .Append(string.Join(", ", chainedExpression.TargetResourceTypes.Select(x => Parameters.AddParameter(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, Model.GetResourceTypeId(x)))))
                            .Append(")");

                        if (searchParamTableExpression.ChainLevel == 1)
                        {
                            // if > 1, the intersection is handled by the JOIN
                            AppendIntersectionWithPredecessor(delimited, searchParamTableExpression, chainedExpression.Reversed ? referenceTargetResourceTableAlias : referenceSourceTableAlias);
                        }

                        if (chainedExpression.ExpressionOnTarget != null && !expressionOnTargetHandledBySecondJoin)
                        {
                            delimited.BeginDelimitedElement();
                            chainedExpression.ExpressionOnTarget?.AcceptVisitor(ResourceTableSearchParameterQueryGenerator.Instance, GetContext(chainedExpression.Reversed ? referenceSourceTableAlias : referenceTargetResourceTableAlias));
                        }

                        if (chainedExpression.ExpressionOnSource != null)
                        {
                            delimited.BeginDelimitedElement();
                            chainedExpression.ExpressionOnSource.AcceptVisitor(ResourceTableSearchParameterQueryGenerator.Instance, GetContext(chainedExpression.Reversed ? referenceTargetResourceTableAlias : referenceSourceTableAlias));
                        }
                    }

                    break;
                case SearchParamTableExpressionKind.Include:
                    var includeExpression = (IncludeExpression)searchParamTableExpression.Predicate;

                    _includeCteIds = _includeCteIds ?? new List<string>();
                    _includeLimitCtesByResourceType = _includeLimitCtesByResourceType ?? new Dictionary<string, List<string>>();
                    _includeFromCteIds = _includeFromCteIds ?? new List<string>();

                    StringBuilder.Append("SELECT DISTINCT ");

                    if (includeExpression.Reversed)
                    {
                        // In case its revinclude, we limit the number of returned items as the resultset size is potentially
                        // unbounded. we ask for +1 so in the limit expression we know if to mark at truncated...
                        StringBuilder.Append("TOP (").Append(Parameters.AddParameter(context.IncludeCount + 1)).Append(") ");
                    }

                    var table = !includeExpression.Reversed ? referenceTargetResourceTableAlias : referenceSourceTableAlias;
                    StringBuilder.Append(VLatest.Resource.ResourceSurrogateId, table);
                    StringBuilder.AppendLine(" AS Sid1, 0 AS IsMatch ");

                    StringBuilder.Append("FROM ").Append(VLatest.ReferenceSearchParam).Append(' ').AppendLine(referenceSourceTableAlias)
                        .Append("INNER JOIN ").Append(VLatest.Resource).Append(' ').AppendLine(referenceTargetResourceTableAlias);

                    using (var delimited = StringBuilder.BeginDelimitedOnClause())
                    {
                        delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceSourceTableAlias)
                            .Append(" = ").Append(VLatest.Resource.ResourceTypeId, referenceTargetResourceTableAlias);

                        delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ReferenceResourceId, referenceSourceTableAlias)
                            .Append(" = ").Append(VLatest.Resource.ResourceId, referenceTargetResourceTableAlias);
                    }

                    using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                    {
                        if (!includeExpression.WildCard)
                        {
                            delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.SearchParamId, referenceSourceTableAlias)
                                .Append(" = ").Append(Parameters.AddParameter(VLatest.ReferenceSearchParam.SearchParamId, Model.GetSearchParamId(includeExpression.ReferenceSearchParameter.Url)));

                            if (includeExpression.TargetResourceType != null)
                            {
                                delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceSourceTableAlias)
                                    .Append(" = ").Append(Parameters.AddParameter(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, Model.GetResourceTypeId(includeExpression.TargetResourceType)));
                            }
                        }

                        AppendHistoryClause(delimited, referenceTargetResourceTableAlias);
                        AppendHistoryClause(delimited, referenceSourceTableAlias);

                        table = !includeExpression.Reversed ? referenceSourceTableAlias : referenceTargetResourceTableAlias;

                        // For RevIncludeIterate we expect to have a TargetType specified if the target reference can be of multiple types
                        var resourceIds = includeExpression.ResourceTypes.Select(x => Model.GetResourceTypeId(x)).ToArray();
                        if (includeExpression.Reversed && includeExpression.Iterate)
                        {
                            if (includeExpression.TargetResourceType != null)
                            {
                                resourceIds = new[] { Model.GetResourceTypeId(includeExpression.TargetResourceType) };
                            }
                            else if (includeExpression.ReferenceSearchParameter?.TargetResourceTypes?.Count > 0)
                            {
                                resourceIds = new[] { Model.GetResourceTypeId(includeExpression.ReferenceSearchParameter.TargetResourceTypes.ToList().First()) };
                            }
                        }

                        delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ResourceTypeId, table)
                            .Append(" IN (")
                            .Append(string.Join(", ", resourceIds))
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
                                .Append(" = ").Append(Parameters.AddParameter(VLatest.ReferenceSearchParam.ResourceTypeId, Model.GetResourceTypeId(includeExpression.SourceResourceType)));
                        }

                        // Limit the join to the main select CTE.
                        // The main select will have max+1 items in the result set to account for paging, so we only want to join using the max amount.
                        if (!includeExpression.Iterate)
                        {
                            delimited.BeginDelimitedElement().Append(VLatest.Resource.ResourceSurrogateId, table)
                                .Append(" IN (SELECT TOP(")
                                .Append(Parameters.AddParameter(context.MaxItemCount))
                                .Append(") Sid1 FROM ").Append(fromCte).Append(")");
                        }
                        else
                        {
                            delimited.BeginDelimitedElement().Append(VLatest.Resource.ResourceSurrogateId, table)
                                .Append(" IN (SELECT Sid1 FROM ").Append(fromCte).Append(")");
                        }
                    }

                    if (includeExpression.Reversed)
                    {
                        // mark that this cte is a reverse one, meaning we need to add another items limitation
                        // cte on top of it
                        _cteToLimit.Add(_tableExpressionCounter);
                    }

                    // Update target reference cte dictionary
                    var curLimitCte = TableExpressionName(_tableExpressionCounter + 1);

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
                    if (_includeFromCteIds?.Count > 1 && _curFromCteIndex >= 0 && _curFromCteIndex < _includeFromCteIds.Count - 1)
                    {
                        StringBuilder.Append($"),{Environment.NewLine}");

                        // If it's not the last result set, append a new IncludeLimit cte, since IncludeLimitCte was not created for the current cte
                        if (_curFromCteIndex < _includeFromCteIds?.Count - 1)
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

                    break;
                case SearchParamTableExpressionKind.IncludeLimit:
                    StringBuilder.Append("SELECT DISTINCT ");

                    // TODO - https://github.com/microsoft/fhir-server/issues/1309 (limit for _include also)
                    var isRev = _cteToLimit.Contains(_tableExpressionCounter - 1);
                    if (isRev)
                    {
                        // the related cte is a reverse include, limit the number of returned items and count to
                        // see if we are over the threshold (to produce a warning to the client)
                        StringBuilder.Append("TOP (").Append(Parameters.AddParameter(context.IncludeCount)).Append(") ");
                    }

                    StringBuilder.Append("Sid1, IsMatch, ");

                    if (isRev)
                    {
                        StringBuilder.Append("CASE WHEN count(*) over() > ")
                            .Append(Parameters.AddParameter(context.IncludeCount))
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
                    break;
                case SearchParamTableExpressionKind.IncludeUnionAll:
                    StringBuilder.Append("SELECT Sid1, IsMatch, IsPartial ");
                    var (supportedSortParam, _) = context.Sort.Count == 0 ? default : context.Sort[0];

                    // In union, any valid sort param is ok, except _lastUpdated, which gets a special treatment.
                    bool supportedSortParamExists = supportedSortParam != null && supportedSortParam.Code != KnownQueryParameterNames.LastUpdated;
                    if (supportedSortParamExists)
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
                        StringBuilder.Append("SELECT Sid1, IsMatch, IsPartial");
                        if (supportedSortParamExists)
                        {
                            StringBuilder.AppendLine(", NULL as SortValue ");
                        }
                        else
                        {
                            StringBuilder.AppendLine();
                        }

                        StringBuilder.Append("FROM ").AppendLine(includeCte);
                    }

                    break;
                case SearchParamTableExpressionKind.Sort:
                    if (searchParamTableExpression.ChainLevel != 0)
                    {
                        throw new InvalidOperationException("Multiple chain level is not possible.");
                    }

                    var (searchParamInfo, searchSort) = context.Sort.Count == 0 ? default : context.Sort[0];
                    var continuationToken = ContinuationToken.FromString(context.ContinuationToken);
                    object sortValue = null;
                    Health.SqlServer.Features.Schema.Model.Column sortColumnName = default(Health.SqlServer.Features.Schema.Model.Column);

                    if (searchParamInfo.Type == ValueSets.SearchParamType.Date)
                    {
                        sortColumnName = VLatest.DateTimeSearchParam.StartDateTime;

                        if (continuationToken != null)
                        {
                            DateTime dateSortValue;
                            if (DateTime.TryParseExact(continuationToken.SortValue, "o", null, DateTimeStyles.None, out dateSortValue))
                            {
                                sortValue = dateSortValue;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(sortColumnName) && searchParamTableExpression.QueryGenerator != null)
                    {
                        StringBuilder.Append("SELECT ").Append(VLatest.Resource.ResourceSurrogateId, null).Append(" AS Sid1, ")
                            .Append(sortColumnName, null).AppendLine(" as SortValue")
                            .Append("FROM ").AppendLine(searchParamTableExpression.QueryGenerator.Table);

                        using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                        {
                            AppendHistoryClause(delimited);

                            if (searchParamTableExpression.Predicate != null)
                            {
                                delimited.BeginDelimitedElement();
                                searchParamTableExpression.Predicate.AcceptVisitor(searchParamTableExpression.QueryGenerator, GetContext());
                            }

                            // if continuation token exists, add it to the query
                            if (continuationToken != null)
                            {
                                var sortOperand = searchSort == SortOrder.Ascending ? ">" : "<";

                                delimited.BeginDelimitedElement();
                                StringBuilder.Append("((").Append(sortColumnName, null).Append($" = ").Append(Parameters.AddParameter(sortColumnName, sortValue));
                                StringBuilder.Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, null).Append($" >= ").Append(Parameters.AddParameter(VLatest.Resource.ResourceSurrogateId, continuationToken.ResourceSurrogateId)).Append(")");
                                StringBuilder.Append(" OR ").Append(sortColumnName, null).Append($" {sortOperand} ").Append(Parameters.AddParameter(sortColumnName, sortValue)).AppendLine(")");
                            }

                            AppendIntersectionWithPredecessor(delimited, searchParamTableExpression);
                        }
                    }

                    _sortVisited = true;

                    break;
                default:
                    throw new ArgumentOutOfRangeException(searchParamTableExpression.Kind.ToString());
            }

            return null;
        }

        private void WriteIncludeLimitCte(string cteToLimit, SearchOptions context)
        {
            StringBuilder.Append(TableExpressionName(++_tableExpressionCounter)).AppendLine(" AS").AppendLine("(");

            // the related cte is a reverse include, limit the number of returned items and count to
            // see if we are over the threshold (to produce a warning to the client)
            StringBuilder.Append("SELECT DISTINCT ");
            StringBuilder.Append("TOP (").Append(Parameters.AddParameter(context.IncludeCount)).Append(") ");

            StringBuilder.Append("Sid1, IsMatch, ");
            StringBuilder.Append("CASE WHEN count(*) over() > ")
                .Append(Parameters.AddParameter(context.IncludeCount))
                .AppendLine(" THEN 1 ELSE 0 END AS IsPartial ");

            StringBuilder.Append("FROM ").AppendLine(cteToLimit);
            StringBuilder.Append($"),{Environment.NewLine}");

            // the 'original' include cte is not in the union, but this new layer is instead
            _includeCteIds.Add(TableExpressionName(_tableExpressionCounter));
        }

        private SearchParameterQueryGeneratorContext GetContext(string tableAlias = null)
        {
            return new SearchParameterQueryGeneratorContext(StringBuilder, Parameters, Model, tableAlias);
        }

        private void AppendIntersectionWithPredecessor(IndentedStringBuilder.DelimitedScope delimited, SearchParamTableExpression searchParamTableExpression, string tableAlias = null)
        {
            int predecessorIndex = FindRestrictingPredecessorTableExpressionIndex();

            if (predecessorIndex >= 0)
            {
                delimited.BeginDelimitedElement();

                string columnToSelect = (searchParamTableExpression.Kind == SearchParamTableExpressionKind.Chain ? searchParamTableExpression.ChainLevel - 1 : searchParamTableExpression.ChainLevel) == 0 ? "Sid1" : "Sid2";

                StringBuilder.Append(VLatest.Resource.ResourceSurrogateId, tableAlias).Append(" IN (SELECT ").Append(columnToSelect)
                    .Append(" FROM ").Append(TableExpressionName(predecessorIndex)).Append(")");
            }
        }

        private int FindRestrictingPredecessorTableExpressionIndex()
        {
            int FindImpl(int currentIndex)
            {
                SearchParamTableExpression currentSearchParamTableExpression = _rootExpression.SearchParamTableExpressions[currentIndex];
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
                        return currentIndex - 1;
                    default:
                        throw new ArgumentOutOfRangeException(currentSearchParamTableExpression.Kind.ToString());
                }
            }

            return FindImpl(_tableExpressionCounter);
        }

        private void AppendDeletedClause(in IndentedStringBuilder.DelimitedScope delimited, string tableAlias = null)
        {
            if (!_isHistorySearch)
            {
                delimited.BeginDelimitedElement().Append(VLatest.Resource.IsDeleted, tableAlias).Append(" = 0");
            }
        }

        private void AppendHistoryClause(in IndentedStringBuilder.DelimitedScope delimited, string tableAlias = null)
        {
            if (!_isHistorySearch)
            {
                delimited.BeginDelimitedElement();

                StringBuilder.Append(VLatest.Resource.IsHistory, tableAlias).Append(" = 0");
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
    }
}
