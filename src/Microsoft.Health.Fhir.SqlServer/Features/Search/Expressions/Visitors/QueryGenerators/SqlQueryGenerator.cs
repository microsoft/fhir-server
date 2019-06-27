// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class SqlQueryGenerator : DefaultExpressionVisitor<SearchOptions, object>, ISqlExpressionVisitor<SearchOptions, object>
    {
        private readonly bool _isHistorySearch;
        private int _tableExpressionCounter = -1;
        private SqlRootExpression _rootExpression;

        public SqlQueryGenerator(IndentedStringBuilder sb, SqlQueryParameterManager parameters, SqlServerFhirModel model, bool isHistorySearch)
        {
            EnsureArg.IsNotNull(sb, nameof(sb));
            EnsureArg.IsNotNull(parameters, nameof(parameters));
            EnsureArg.IsNotNull(model, nameof(model));

            StringBuilder = sb;
            Parameters = parameters;
            Model = model;
            _isHistorySearch = isHistorySearch;
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

            if (searchOptions.CountOnly)
            {
                StringBuilder.AppendLine("SELECT COUNT(DISTINCT ").Append(V1.Resource.ResourceSurrogateId, resourceTableAlias).Append(")");
            }
            else
            {
                StringBuilder.Append("SELECT ");

                if (expression.TableExpressions.Count == 0)
                {
                    StringBuilder.Append("TOP (").Append(Parameters.AddParameter(context.MaxItemCount + 1)).Append(") ");
                }

                StringBuilder.Append(V1.Resource.ResourceTypeId, resourceTableAlias).Append(", ")
                    .Append(V1.Resource.ResourceId, resourceTableAlias).Append(", ")
                    .Append(V1.Resource.Version, resourceTableAlias).Append(", ")
                    .Append(V1.Resource.IsDeleted, resourceTableAlias).Append(", ")
                    .Append(V1.Resource.ResourceSurrogateId, resourceTableAlias).Append(", ")
                    .Append(V1.Resource.RequestMethod, resourceTableAlias).Append(", ")
                    .AppendLine(V1.Resource.RawResource, resourceTableAlias);
            }

            StringBuilder.Append("FROM ").Append(V1.Resource).AppendLine(" r");

            using (var delimitedClause = StringBuilder.BeginDelimitedWhereClause())
            {
                if (expression.TableExpressions.Count > 0)
                {
                    delimitedClause.BeginDelimitedElement();
                    StringBuilder.Append(V1.Resource.ResourceSurrogateId, resourceTableAlias).Append(" IN (SELECT Sid1 FROM ").Append(TableExpressionName(_tableExpressionCounter)).Append(")");
                }

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
                StringBuilder.Append("ORDER BY ").Append(V1.Resource.ResourceSurrogateId, resourceTableAlias).AppendLine(" ASC");
            }

            StringBuilder.Append("OPTION(RECOMPILE)");

            return null;
        }

        private static string TableExpressionName(int id) => "cte" + id;

        public object VisitTable(TableExpression tableExpression, SearchOptions context)
        {
            switch (tableExpression.Kind)
            {
                case TableExpressionKind.Normal:

                    if (tableExpression.ChainLevel == 0)
                    {
                        StringBuilder.Append("SELECT ").Append(V1.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid1")
                            .Append("FROM ").AppendLine(tableExpression.SearchParameterQueryGenerator.Table);
                    }
                    else
                    {
                        StringBuilder.Append("SELECT Sid1, ").Append(V1.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid2")
                            .Append("FROM ").AppendLine(tableExpression.SearchParameterQueryGenerator.Table)
                            .Append("INNER JOIN ").AppendLine(TableExpressionName(FindRestrictingPredecessorTableExpressionIndex()));

                        using (var delimited = StringBuilder.BeginDelimitedOnClause())
                        {
                            delimited.BeginDelimitedElement().Append(V1.Resource.ResourceSurrogateId, null).Append(" = ").Append("Sid2");
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
                    StringBuilder.Append("SELECT ").Append(V1.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid1")
                        .Append("FROM ").AppendLine(V1.Resource);

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
                        StringBuilder.Append("SELECT ").AppendLine(V1.Resource.ResourceSurrogateId, null)
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
                    StringBuilder.Append("SELECT DISTINCT TOP (").Append(Parameters.AddParameter(context.MaxItemCount + 1)).AppendLine(") Sid1 ")
                        .Append("FROM ").AppendLine(TableExpressionName(_tableExpressionCounter - 1))
                        .AppendLine("ORDER BY Sid1 ASC");

                    break;

                case TableExpressionKind.Chain:
                    var chainedExpression = (ChainedExpression)tableExpression.NormalizedPredicate;

                    string referenceTableAlias = "ref";
                    string resourceTableAlias = "r";

                    StringBuilder.Append("SELECT ");
                    if (tableExpression.ChainLevel == 1)
                    {
                        StringBuilder.Append(V1.ReferenceSearchParam.ResourceSurrogateId, referenceTableAlias).Append(" AS ").Append(chainedExpression.Reversed ? "Sid2" : "Sid1").Append(", ");
                    }
                    else
                    {
                        StringBuilder.Append("Sid1, ");
                    }

                    StringBuilder.Append(V1.Resource.ResourceSurrogateId, chainedExpression.Reversed && tableExpression.ChainLevel > 1 ? referenceTableAlias : resourceTableAlias).Append(" AS ").AppendLine(chainedExpression.Reversed && tableExpression.ChainLevel == 1 ? "Sid1 " : "Sid2 ")
                        .Append("FROM ").Append(V1.ReferenceSearchParam).Append(' ').AppendLine(referenceTableAlias)
                        .Append("INNER JOIN ").Append(V1.Resource).Append(' ').AppendLine(resourceTableAlias);

                    using (var delimited = StringBuilder.BeginDelimitedOnClause())
                    {
                        delimited.BeginDelimitedElement().Append(V1.ReferenceSearchParam.ReferenceResourceTypeId, referenceTableAlias)
                            .Append(" = ").Append(V1.Resource.ResourceTypeId, resourceTableAlias);

                        delimited.BeginDelimitedElement().Append(V1.ReferenceSearchParam.ReferenceResourceId, referenceTableAlias)
                            .Append(" = ").Append(V1.Resource.ResourceId, resourceTableAlias);
                    }

                    // Denormalized predicates on reverse chains need to be applied via a join to the resource table
                    if (tableExpression.DenormalizedPredicate != null && chainedExpression.Reversed)
                    {
                        StringBuilder.Append("INNER JOIN ").Append(V1.Resource).Append(' ').AppendLine("r2");

                        using (var delimited = StringBuilder.BeginDelimitedOnClause())
                        {
                            delimited.BeginDelimitedElement().Append(V1.ReferenceSearchParam.ResourceSurrogateId, referenceTableAlias)
                                .Append(" = ").Append(V1.Resource.ResourceSurrogateId, "r2");

                            delimited.BeginDelimitedElement();
                            tableExpression.DenormalizedPredicate?.AcceptVisitor(DispatchingDenormalizedSearchParameterQueryGenerator.Instance, GetContext("r2"));
                        }
                    }

                    if (tableExpression.ChainLevel > 1)
                    {
                        StringBuilder.Append("INNER JOIN ").AppendLine(TableExpressionName(FindRestrictingPredecessorTableExpressionIndex()));

                        using (var delimited = StringBuilder.BeginDelimitedOnClause())
                        {
                            delimited.BeginDelimitedElement().Append(V1.Resource.ResourceSurrogateId, chainedExpression.Reversed ? resourceTableAlias : referenceTableAlias).Append(" = ").Append("Sid2");
                        }
                    }

                    using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                    {
                        delimited.BeginDelimitedElement().Append(V1.ReferenceSearchParam.SearchParamId, referenceTableAlias)
                            .Append(" = ").Append(Parameters.AddParameter(V1.ReferenceSearchParam.SearchParamId, Model.GetSearchParamId(chainedExpression.ReferenceSearchParameter.Url)));

                        AppendHistoryClause(delimited, resourceTableAlias);
                        AppendHistoryClause(delimited, referenceTableAlias);

                        delimited.BeginDelimitedElement().Append(V1.ReferenceSearchParam.ResourceTypeId, referenceTableAlias)
                            .Append(" = ").Append(Parameters.AddParameter(V1.ReferenceSearchParam.ResourceTypeId, Model.GetResourceTypeId(chainedExpression.ResourceType)));

                        delimited.BeginDelimitedElement().Append(V1.ReferenceSearchParam.ReferenceResourceTypeId, referenceTableAlias)
                            .Append(" = ").Append(Parameters.AddParameter(V1.ReferenceSearchParam.ReferenceResourceTypeId, Model.GetResourceTypeId(chainedExpression.TargetResourceType)));

                        if (tableExpression.ChainLevel == 1)
                        {
                            // if > 1, the intersection is handled by the JOIN
                            AppendIntersectionWithPredecessor(delimited, tableExpression, chainedExpression.Reversed ? resourceTableAlias : referenceTableAlias);
                        }

                        if (tableExpression.DenormalizedPredicate != null && !chainedExpression.Reversed)
                        {
                            delimited.BeginDelimitedElement();
                            tableExpression.DenormalizedPredicate?.AcceptVisitor(DispatchingDenormalizedSearchParameterQueryGenerator.Instance, GetContext(resourceTableAlias));
                        }
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(tableExpression.Kind.ToString());
            }

            return null;
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

                StringBuilder.Append(V1.Resource.ResourceSurrogateId, tableAlias).Append(" IN (SELECT ").Append(columnToSelect)
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
                        return currentIndex - 1;
                    case TableExpressionKind.Concatenation:
                        return FindImpl(currentIndex - 1);

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
                delimited.BeginDelimitedElement().Append(V1.Resource.IsDeleted, tableAlias).Append(" = 0");
            }
        }

        private void AppendHistoryClause(in IndentedStringBuilder.DelimitedScope delimited, string tableAlias = null)
        {
            if (!_isHistorySearch)
            {
                delimited.BeginDelimitedElement();

                StringBuilder.Append(V1.Resource.IsHistory, tableAlias).Append(" = 0");
            }
        }
    }
}
