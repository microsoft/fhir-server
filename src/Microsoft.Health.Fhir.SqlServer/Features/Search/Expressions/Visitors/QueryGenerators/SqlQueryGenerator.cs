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

            if (searchOptions.CountOnly)
            {
                StringBuilder.AppendLine("SELECT COUNT(*)");
            }
            else
            {
                StringBuilder.Append("SELECT ");

                if (expression.TableExpressions.Count == 0)
                {
                    StringBuilder.Append("TOP (").Append(Parameters.AddParameter(context.MaxItemCount + 1)).Append(") ");
                }

                StringBuilder.Append("r.").Append(V1.Resource.ResourceTypeId).Append(", ")
                    .Append("r.").Append(V1.Resource.ResourceId).Append(", ")
                    .Append("r.").Append(V1.Resource.Version).Append(", ")
                    .Append("r.").Append(V1.Resource.IsDeleted).Append(", ")
                    .Append("r.").Append(V1.Resource.ResourceSurrogateId).Append(", ")
                    .Append("r.").Append(V1.Resource.LastUpdated).Append(", ")
                    .Append("r.").Append(V1.Resource.RequestMethod).Append(", ")
                    .Append("r.").AppendLine(V1.Resource.RawResource);
            }

            StringBuilder.Append("FROM ").Append(V1.Resource).AppendLine(" r");

            using (var delimitedClause = StringBuilder.BeginDelimitedWhereClause())
            {
                if (expression.TableExpressions.Count > 0)
                {
                    delimitedClause.BeginDelimitedElement();
                    StringBuilder.Append("r.").Append(V1.Resource.ResourceSurrogateId).Append(" IN (SELECT Sid1 FROM ").Append(TableExpressionName(_tableExpressionCounter)).Append(")");
                }

                foreach (var denormalizedPredicate in expression.DenormalizedExpressions)
                {
                    delimitedClause.BeginDelimitedElement();
                    denormalizedPredicate.AcceptVisitor(this, context);
                }

                if (expression.TableExpressions.Count == 0)
                {
                    AppendHistoryClause(delimitedClause);
                    AppendDeletedClause(delimitedClause);
                }
            }

            if (!searchOptions.CountOnly)
            {
                StringBuilder.Append("ORDER BY r.").Append(V1.Resource.ResourceSurrogateId).AppendLine(" ASC");
            }

            StringBuilder.Append("OPTION(RECOMPILE)");

            return null;
        }

        private string TableExpressionName(int id) => "cte" + id;

        public object VisitTable(TableExpression tableExpression, SearchOptions context)
        {
            switch (tableExpression.Kind)
            {
                case TableExpressionKind.Normal:

                    StringBuilder.Append("SELECT ").Append(V1.Resource.ResourceSurrogateId).AppendLine(" AS Sid1")
                        .Append("FROM ").AppendLine(tableExpression.SearchParameterQueryGenerator.Table);

                    using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                    {
                        AppendHistoryClause(delimited);

                        int predecessorIndex = FindRestrictingPredecessorTableExpressionIndex();

                        if (predecessorIndex >= 0)
                        {
                            delimited.BeginDelimitedElement();
                            StringBuilder.Append(V1.Resource.ResourceSurrogateId).Append(" IN (SELECT Sid1 FROM ").Append(TableExpressionName(predecessorIndex)).Append(")");
                        }

                        if (tableExpression.DenormalizedPredicate != null)
                        {
                            delimited.BeginDelimitedElement();
                            tableExpression.DenormalizedPredicate?.AcceptVisitor(this, context);
                        }

                        if (tableExpression.NormalizedPredicate != null)
                        {
                            delimited.BeginDelimitedElement();
                            tableExpression.NormalizedPredicate.AcceptVisitor(tableExpression.SearchParameterQueryGenerator, this);
                        }
                    }

                    break;

                case TableExpressionKind.Concatenation:
                    StringBuilder.Append("SELECT Sid1 FROM ").AppendLine(TableExpressionName(_tableExpressionCounter - 1));
                    StringBuilder.AppendLine("UNION ALL");

                    goto case TableExpressionKind.Normal;

                case TableExpressionKind.All:
                    StringBuilder.Append("SELECT ").Append(V1.Resource.ResourceSurrogateId).AppendLine(" AS Sid1")
                        .Append("FROM ").AppendLine(V1.Resource);

                    using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                    {
                        AppendHistoryClause(delimited);
                        AppendDeletedClause(delimited);
                        if (tableExpression.DenormalizedPredicate != null)
                        {
                            delimited.BeginDelimitedElement();
                            tableExpression.DenormalizedPredicate?.AcceptVisitor(this, context);
                        }
                    }

                    break;

                case TableExpressionKind.NotExists:
                    StringBuilder.Append("SELECT Sid1 FROM ").AppendLine(TableExpressionName(_tableExpressionCounter - 1));
                    StringBuilder.AppendLine("WHERE Sid1 NOT IN").AppendLine("(");

                    using (StringBuilder.Indent())
                    {
                        StringBuilder.Append("SELECT ").AppendLine(V1.Resource.ResourceSurrogateId)
                            .Append("FROM ").AppendLine(tableExpression.SearchParameterQueryGenerator.Table);
                        using (var delimited = StringBuilder.BeginDelimitedWhereClause())
                        {
                            AppendHistoryClause(delimited);

                            if (tableExpression.DenormalizedPredicate != null)
                            {
                                delimited.BeginDelimitedElement();
                                tableExpression.DenormalizedPredicate?.AcceptVisitor(this, context);
                            }

                            delimited.BeginDelimitedElement();
                            tableExpression.NormalizedPredicate.AcceptVisitor(tableExpression.SearchParameterQueryGenerator, this);
                        }
                    }

                    StringBuilder.AppendLine(")");
                    break;

                case TableExpressionKind.Top:
                    StringBuilder.Append("SELECT DISTINCT TOP (").Append(Parameters.AddParameter(context.MaxItemCount + 1)).AppendLine(") Sid1 ")
                        .Append("FROM ").AppendLine(TableExpressionName(_tableExpressionCounter - 1))
                        .AppendLine("ORDER BY Sid1 ASC");

                    break;
                default:
                    throw new ArgumentOutOfRangeException(tableExpression.Kind.ToString());
            }

            return null;
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
                        return currentIndex - 1;
                    case TableExpressionKind.Concatenation:
                        return FindImpl(currentIndex - 1);

                    default:
                        throw new ArgumentOutOfRangeException(currentTableExpression.Kind.ToString());
                }
            }

            return FindImpl(_tableExpressionCounter);
        }

        private void AppendDeletedClause(in IndentedStringBuilder.DelimitedScope delimited)
        {
            if (!_isHistorySearch)
            {
                delimited.BeginDelimitedElement().Append(V1.Resource.IsDeleted).Append(" = 0");
            }
        }

        private void AppendHistoryClause(in IndentedStringBuilder.DelimitedScope delimited)
        {
            if (!_isHistorySearch)
            {
                delimited.BeginDelimitedElement().Append(V1.Resource.IsHistory).Append(" = 0");
            }
        }

        public override object VisitSearchParameter(SearchParameterExpression expression, SearchOptions context)
        {
            expression.AcceptVisitor(GetSearchParameterQueryGenerator(expression), this);
            return null;
        }

        public override object VisitMissingSearchParameter(MissingSearchParameterExpression expression, SearchOptions context)
        {
            expression.AcceptVisitor(GetSearchParameterQueryGenerator(expression), this);
            return null;
        }

        public override object VisitMultiary(MultiaryExpression expression, SearchOptions context)
        {
            if (expression.MultiaryOperation == MultiaryOperator.Or)
            {
                StringBuilder.Append('(');
            }

            StringBuilder.AppendDelimited(
                expression.MultiaryOperation == MultiaryOperator.And ? " AND " : " OR ",
                expression.Expressions,
                (sb, childExpr) => childExpr.AcceptVisitor(this, context));

            if (expression.MultiaryOperation == MultiaryOperator.Or)
            {
                StringBuilder.AppendLine(")");
            }

            return context;
        }

        private SearchParameterQueryGenerator GetSearchParameterQueryGenerator(SearchParameterExpressionBase searchParameter)
        {
            switch (searchParameter.Parameter.Name)
            {
                case SearchParameterNames.Id:
                    return new ResourceIdParameterQueryGenerator();
                case SearchParameterNames.LastUpdated:
                    return new LastUpdatedParameterQueryGenerator();
                case SearchParameterNames.ResourceType:
                    return new ResourceTypeIdParameterQueryGenerator();
                case SqlSearchParameters.ResourceSurrogateIdParameterName:
                    return new ResourceSurrogateIdParameterQueryGenerator();
                default:
                    throw new NotSupportedException(searchParameter.Parameter.Name);
            }
        }
    }
}
