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
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class SqlQueryGenerator : DefaultExpressionVisitor<SearchOptions, object>, ISqlExpressionVisitor<SearchOptions, object>
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

        public object VisitSqlRoot(SqlRootExpression expression, SearchOptions context)
        {
            if (!(context is SearchOptions searchOptions))
            {
                throw new ArgumentException($"Argument should be of type {nameof(SearchOptions)}", nameof(context));
            }

            _rootExpression = expression;

            if (expression.TableExpressions.Count > 0)
            {
                StringBuilder.Append("WITH ");

                StringBuilder.AppendDelimited($",{Environment.NewLine}", expression.TableExpressions, (sb, tableExpression) =>
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
            var (searchParamInfo, sortOrder) = searchOptions.Sort.Count == 0 ? default : searchOptions.Sort[0];

            if (searchOptions.CountOnly)
            {
                StringBuilder.AppendLine("SELECT COUNT(DISTINCT ").Append(VLatest.Resource.ResourceSurrogateId, resourceTableAlias).Append(")");
            }
            else
            {
                // DISTINCT is used since different ctes may return the same resources due to _include and _include:iterate search parameters
                StringBuilder.Append("SELECT DISTINCT ");

                if (expression.TableExpressions.Count == 0)
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
                StringBuilder.Append(expression.TableExpressions.Count > 0 ? "CAST(IsMatch AS bit) AS IsMatch, " : "CAST(1 AS bit) AS IsMatch, ");
                StringBuilder.Append(expression.TableExpressions.Count > 0 ? "CAST(IsPartial AS bit) AS IsPartial, " : "CAST(0 AS bit) AS IsPartial, ");

                if (_schemaInfo.Current > 3)
                {
                    // IsRawResourceMetaSet column was added in V4
                    StringBuilder.Append(VLatest.Resource.IsRawResourceMetaSet, resourceTableAlias).Append(", ");
                }
                else
                {
                    StringBuilder.Append("CAST(0 AS bit) AS IsRawResourceMetaSet, ");
                }

                StringBuilder.Append(VLatest.Resource.RawResource, resourceTableAlias);
                if (searchParamInfo != null && searchParamInfo.Name != KnownQueryParameterNames.LastUpdated)
                {
                    StringBuilder.Append(", ").Append(TableExpressionName(_tableExpressionCounter)).Append(".SortValue");
                }

                StringBuilder.AppendLine();
            }

            StringBuilder.Append("FROM ").Append(VLatest.Resource).Append(" ").AppendLine(resourceTableAlias);

            if (expression.TableExpressions.Count > 0)
            {
                StringBuilder.Append("INNER JOIN ").AppendLine(TableExpressionName(_tableExpressionCounter));
                StringBuilder.Append("ON ").Append(VLatest.Resource.ResourceSurrogateId, resourceTableAlias).Append(" = ").Append(TableExpressionName(_tableExpressionCounter)).AppendLine(".Sid1");
            }

            using (var delimitedClause = StringBuilder.BeginDelimitedWhereClause())
            {
                foreach (var denormalizedPredicate in expression.DenormalizedExpressions)
                {
                    delimitedClause.BeginDelimitedElement();
                    denormalizedPredicate.AcceptVisitor(DispatchingDenormalizedSearchParameterQueryGenerator.Instance, GetContext());
                }

                if (expression.TableExpressions.Count == 0)
                {
                    AppendHistoryClause(delimitedClause);
                    AppendDeletedClause(delimitedClause);
                }
            }

            if (!searchOptions.CountOnly)
            {
                StringBuilder.Append("ORDER BY ");
                if (searchParamInfo == null || searchParamInfo.Name == KnownQueryParameterNames.LastUpdated)
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

            StringBuilder.Append("OPTION(RECOMPILE)");

            return null;
        }

        private static string TableExpressionName(int id) => "cte" + id;

        public object VisitTable(TableExpression tableExpression, SearchOptions context)
        {
            const string referenceSourceTableAlias = "refSource";
            const string referenceTargetResourceTableAlias = "refTarget";

            switch (tableExpression.Kind)
            {
                case TableExpressionKind.Normal:

                    if (tableExpression.ChainLevel == 0)
                    {
                        StringBuilder.Append("SELECT ").Append(VLatest.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid1")
                            .Append("FROM ").AppendLine(tableExpression.SearchParameterQueryGenerator.Table);
                    }
                    else
                    {
                        StringBuilder.Append("SELECT Sid1, ").Append(VLatest.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid2")
                            .Append("FROM ").AppendLine(tableExpression.SearchParameterQueryGenerator.Table)
                            .Append("INNER JOIN ").AppendLine(TableExpressionName(FindRestrictingPredecessorTableExpressionIndex()));

                        using (var delimited = StringBuilder.BeginDelimitedOnClause())
                        {
                            delimited.BeginDelimitedElement().Append(VLatest.Resource.ResourceSurrogateId, null).Append(" = ").Append("Sid2");
                        }
                    }

                    using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                    {
                        AppendHistoryClause(delimited);

                        if (tableExpression.ChainLevel == 0)
                        {
                            // if chainLevel > 0, the intersection is already handled in the JOIN
                            AppendIntersectionWithPredecessor(delimited, tableExpression);
                        }

                        if (tableExpression.DenormalizedPredicate != null)
                        {
                            delimited.BeginDelimitedElement();
                            tableExpression.DenormalizedPredicate?.AcceptVisitor(DispatchingDenormalizedSearchParameterQueryGenerator.Instance, GetContext());
                        }

                        if (tableExpression.NormalizedPredicate != null)
                        {
                            delimited.BeginDelimitedElement();
                            tableExpression.NormalizedPredicate.AcceptVisitor(tableExpression.SearchParameterQueryGenerator, GetContext());
                        }
                    }

                    break;

                case TableExpressionKind.Concatenation:
                    StringBuilder.Append("SELECT * FROM ").AppendLine(TableExpressionName(_tableExpressionCounter - 1));
                    StringBuilder.AppendLine("UNION ALL");

                    goto case TableExpressionKind.Normal;

                case TableExpressionKind.All:
                    StringBuilder.Append("SELECT ").Append(VLatest.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid1")
                        .Append("FROM ").AppendLine(VLatest.Resource);

                    using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                    {
                        AppendHistoryClause(delimited);
                        AppendDeletedClause(delimited);
                        if (tableExpression.DenormalizedPredicate != null)
                        {
                            delimited.BeginDelimitedElement();
                            tableExpression.DenormalizedPredicate?.AcceptVisitor(DispatchingDenormalizedSearchParameterQueryGenerator.Instance, GetContext());
                        }
                    }

                    break;

                case TableExpressionKind.NotExists:
                    StringBuilder.Append("SELECT Sid1 FROM ").AppendLine(TableExpressionName(_tableExpressionCounter - 1));
                    StringBuilder.AppendLine("WHERE Sid1 NOT IN").AppendLine("(");

                    using (StringBuilder.Indent())
                    {
                        StringBuilder.Append("SELECT ").AppendLine(VLatest.Resource.ResourceSurrogateId, null)
                            .Append("FROM ").AppendLine(tableExpression.SearchParameterQueryGenerator.Table);
                        using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                        {
                            AppendHistoryClause(delimited);

                            if (tableExpression.DenormalizedPredicate != null)
                            {
                                delimited.BeginDelimitedElement();
                                tableExpression.DenormalizedPredicate?.AcceptVisitor(DispatchingDenormalizedSearchParameterQueryGenerator.Instance, GetContext());
                            }

                            delimited.BeginDelimitedElement();
                            tableExpression.NormalizedPredicate.AcceptVisitor(tableExpression.SearchParameterQueryGenerator, GetContext());
                        }
                    }

                    StringBuilder.AppendLine(")");
                    break;

                case TableExpressionKind.Top:
                    var (paramInfo, sortOrder) = context.Sort.Count == 0 ? default : context.Sort[0];
                    var tableExpressionName = TableExpressionName(_tableExpressionCounter - 1);
                    var sortExpression = (paramInfo == null || paramInfo.Name == KnownQueryParameterNames.LastUpdated) ? null : $"{tableExpressionName}.SortValue";

                    // Everything in the top expression is considered a match
                    StringBuilder.Append("SELECT DISTINCT TOP (").Append(Parameters.AddParameter(context.MaxItemCount + 1)).Append(") Sid1, 1 AS IsMatch, 0 AS IsPartial ")
                        .AppendLine(sortExpression == null ? string.Empty : $", {sortExpression}")
                        .Append("FROM ").AppendLine(tableExpressionName)
                        .AppendLine($"ORDER BY {(sortExpression == null ? string.Empty : $"{sortExpression} {(sortOrder == SortOrder.Ascending ? "ASC" : "DESC")}, ")} Sid1 {((sortExpression != null || sortOrder == SortOrder.Ascending) ? "ASC" : "DESC")}");

                    // For any includes, the source of the resource surrogate ids to join on is saved
                    _cteMainSelect = TableExpressionName(_tableExpressionCounter);

                    break;

                case TableExpressionKind.Chain:
                    var chainedExpression = (ChainedExpression)tableExpression.NormalizedPredicate;

                    StringBuilder.Append("SELECT ");
                    if (tableExpression.ChainLevel == 1)
                    {
                        StringBuilder.Append(VLatest.ReferenceSearchParam.ResourceSurrogateId, referenceSourceTableAlias).Append(" AS ").Append(chainedExpression.Reversed ? "Sid2" : "Sid1").Append(", ");
                    }
                    else
                    {
                        StringBuilder.Append("Sid1, ");
                    }

                    StringBuilder.Append(VLatest.Resource.ResourceSurrogateId, chainedExpression.Reversed && tableExpression.ChainLevel > 1 ? referenceSourceTableAlias : referenceTargetResourceTableAlias).Append(" AS ").AppendLine(chainedExpression.Reversed && tableExpression.ChainLevel == 1 ? "Sid1 " : "Sid2 ")
                        .Append("FROM ").Append(VLatest.ReferenceSearchParam).Append(' ').AppendLine(referenceSourceTableAlias)
                        .Append("INNER JOIN ").Append(VLatest.Resource).Append(' ').AppendLine(referenceTargetResourceTableAlias);

                    using (var delimited = StringBuilder.BeginDelimitedOnClause())
                    {
                        delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceSourceTableAlias)
                            .Append(" = ").Append(VLatest.Resource.ResourceTypeId, referenceTargetResourceTableAlias);

                        delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ReferenceResourceId, referenceSourceTableAlias)
                            .Append(" = ").Append(VLatest.Resource.ResourceId, referenceTargetResourceTableAlias);
                    }

                    // For reverse chaning, if there is a parameter on the _id search parameter, we need another join to get the resource ID of the reference source (all we have is the surrogate ID at this point)

                    bool denormalizedHandledBySecondJoin = tableExpression.DenormalizedPredicate != null && chainedExpression.Reversed && tableExpression.DenormalizedPredicate.AcceptVisitor(DenormalizedExpressionContainsIdParameterVisitor.Instance, null);

                    if (denormalizedHandledBySecondJoin)
                    {
                        const string referenceSourceResourceTableAlias = "refSourceResource";

                        denormalizedHandledBySecondJoin = true;
                        StringBuilder.Append("INNER JOIN ").Append(VLatest.Resource).Append(' ').AppendLine(referenceSourceResourceTableAlias);

                        using (var delimited = StringBuilder.BeginDelimitedOnClause())
                        {
                            delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ResourceSurrogateId, referenceSourceTableAlias)
                                .Append(" = ").Append(VLatest.Resource.ResourceSurrogateId, referenceSourceResourceTableAlias);

                            delimited.BeginDelimitedElement();
                            tableExpression.DenormalizedPredicate?.AcceptVisitor(DispatchingDenormalizedSearchParameterQueryGenerator.Instance, GetContext(referenceSourceResourceTableAlias));
                        }
                    }

                    if (tableExpression.ChainLevel > 1)
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
                            .Append(" = ").Append(Parameters.AddParameter(VLatest.ReferenceSearchParam.ResourceTypeId, Model.GetResourceTypeId(chainedExpression.ResourceType)));

                        delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, referenceSourceTableAlias)
                            .Append(" = ").Append(Parameters.AddParameter(VLatest.ReferenceSearchParam.ReferenceResourceTypeId, Model.GetResourceTypeId(chainedExpression.TargetResourceType)));

                        if (tableExpression.ChainLevel == 1)
                        {
                            // if > 1, the intersection is handled by the JOIN
                            AppendIntersectionWithPredecessor(delimited, tableExpression, chainedExpression.Reversed ? referenceTargetResourceTableAlias : referenceSourceTableAlias);
                        }

                        if (tableExpression.DenormalizedPredicate != null && !denormalizedHandledBySecondJoin)
                        {
                            delimited.BeginDelimitedElement();
                            tableExpression.DenormalizedPredicate?.AcceptVisitor(DispatchingDenormalizedSearchParameterQueryGenerator.Instance, GetContext(chainedExpression.Reversed ? referenceSourceTableAlias : referenceTargetResourceTableAlias));
                        }

                        if (tableExpression.DenormalizedPredicateOnChainRoot != null)
                        {
                            delimited.BeginDelimitedElement();
                            tableExpression.DenormalizedPredicateOnChainRoot.AcceptVisitor(DispatchingDenormalizedSearchParameterQueryGenerator.Instance, GetContext(chainedExpression.Reversed ? referenceTargetResourceTableAlias : referenceSourceTableAlias));
                        }
                    }

                    break;
                case TableExpressionKind.Include:
                    var includeExpression = (IncludeExpression)tableExpression.NormalizedPredicate;

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
                        var resourceId = Model.GetResourceTypeId(includeExpression.ResourceType);
                        if (includeExpression.Reversed && includeExpression.Iterate)
                        {
                            if (includeExpression.TargetResourceType != null)
                            {
                                resourceId = Model.GetResourceTypeId(includeExpression.TargetResourceType);
                            }
                            else if (includeExpression.ReferenceSearchParameter?.TargetResourceTypes?.Count > 0)
                            {
                                resourceId = Model.GetResourceTypeId(includeExpression.ReferenceSearchParameter.TargetResourceTypes.ToList().First());
                            }
                        }

                        delimited.BeginDelimitedElement().Append(VLatest.ReferenceSearchParam.ResourceTypeId, table)
                        .Append(" = ").Append(Parameters.AddParameter(VLatest.ReferenceSearchParam.ResourceTypeId, resourceId));

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
                                    var targetType = includeExpression.ReferenceSearchParameter.TargetResourceTypes.First();

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
                        tableExpression.AcceptVisitor(this, context);
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
                case TableExpressionKind.IncludeLimit:
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
                case TableExpressionKind.IncludeUnionAll:
                    StringBuilder.AppendLine("SELECT Sid1, IsMatch, IsPartial ");
                    StringBuilder.Append("FROM ").AppendLine(_cteMainSelect);

                    foreach (var includeCte in _includeCteIds)
                    {
                        StringBuilder.AppendLine("UNION ALL");
                        StringBuilder.AppendLine("SELECT Sid1, IsMatch, IsPartial ");
                        StringBuilder.Append("FROM ").AppendLine(includeCte);
                    }

                    break;
                case TableExpressionKind.Sort:
                    if (tableExpression.ChainLevel != 0)
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

                    if (!string.IsNullOrEmpty(sortColumnName) && tableExpression.SearchParameterQueryGenerator != null)
                    {
                        StringBuilder.Append("SELECT ").Append(VLatest.Resource.ResourceSurrogateId, null).Append(" AS Sid1, ")
                            .Append(sortColumnName, null).AppendLine(" as SortValue")
                            .Append("FROM ").AppendLine(tableExpression.SearchParameterQueryGenerator.Table);

                        using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                        {
                            AppendHistoryClause(delimited);

                            if (tableExpression.DenormalizedPredicate != null)
                            {
                                delimited.BeginDelimitedElement();
                                tableExpression.DenormalizedPredicate?.AcceptVisitor(DispatchingDenormalizedSearchParameterQueryGenerator.Instance, GetContext());
                            }

                            if (tableExpression.NormalizedPredicate != null)
                            {
                                delimited.BeginDelimitedElement();
                                tableExpression.NormalizedPredicate.AcceptVisitor(tableExpression.SearchParameterQueryGenerator, GetContext());
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

                            AppendIntersectionWithPredecessor(delimited, tableExpression);
                        }
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(tableExpression.Kind.ToString());
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

        private void AppendIntersectionWithPredecessor(IndentedStringBuilder.DelimitedScope delimited, TableExpression tableExpression, string tableAlias = null)
        {
            int predecessorIndex = FindRestrictingPredecessorTableExpressionIndex();

            if (predecessorIndex >= 0)
            {
                delimited.BeginDelimitedElement();

                string columnToSelect = (tableExpression.Kind == TableExpressionKind.Chain ? tableExpression.ChainLevel - 1 : tableExpression.ChainLevel) == 0 ? "Sid1" : "Sid2";

                StringBuilder.Append(VLatest.Resource.ResourceSurrogateId, tableAlias).Append(" IN (SELECT ").Append(columnToSelect)
                             .Append(" FROM ").Append(TableExpressionName(predecessorIndex)).Append(")");
            }
        }

        private int FindRestrictingPredecessorTableExpressionIndex()
        {
            int FindImpl(int currentIndex)
            {
                TableExpression currentTableExpression = _rootExpression.TableExpressions[currentIndex];
                switch (currentTableExpression.Kind)
                {
                    case TableExpressionKind.NotExists:
                    case TableExpressionKind.Normal:
                    case TableExpressionKind.Chain:
                    case TableExpressionKind.Top:
                        return currentIndex - 1;
                    case TableExpressionKind.Concatenation:
                        return FindImpl(currentIndex - 1);
                    case TableExpressionKind.Sort:
                        return currentIndex - 1;
                    default:
                        throw new ArgumentOutOfRangeException(currentTableExpression.Kind.ToString());
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
        /// A visitor to determine if there are any references to the _id search parameter in an expression
        /// </summary>
        private class DenormalizedExpressionContainsIdParameterVisitor : DefaultExpressionVisitor<object, bool>
        {
            public static readonly DenormalizedExpressionContainsIdParameterVisitor Instance = new DenormalizedExpressionContainsIdParameterVisitor();

            private DenormalizedExpressionContainsIdParameterVisitor()
            : base((acc, curr) => acc || curr)
            {
            }

            public override bool VisitSearchParameter(SearchParameterExpression expression, object context) => expression.Parameter.Name == SearchParameterNames.Id;
        }
    }
}
