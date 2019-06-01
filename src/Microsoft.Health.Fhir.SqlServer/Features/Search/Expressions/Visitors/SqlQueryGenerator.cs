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

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal class SqlQueryGenerator : ISqlExpressionVisitor<object, object>
    {
        private readonly bool _isHistorySearch;
        private int _tableExpressionCounter;
        private int _totalTableExpressions;

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

        public object VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (!(context is SearchOptions searchOptions))
            {
                throw new ArgumentException($"Argument should be of type {nameof(SearchOptions)}", nameof(context));
            }

            if (expression.NormalizedPredicates.Count > 0)
            {
                _totalTableExpressions = expression.NormalizedPredicates.Count;

                StringBuilder.Append("WITH ");

                StringBuilder.AppendDelimited($",{Environment.NewLine}", expression.NormalizedPredicates, (sb, tableExpression) =>
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
                StringBuilder.Append("SELECT TOP ").Append(searchOptions.MaxItemCount + 1).Append(' ')
                    .Append("r.").Append(V1.Resource.ResourceTypeId).Append(", ")
                    .Append("r.").Append(V1.Resource.ResourceId).Append(", ")
                    .Append("r.").Append(V1.Resource.Version).Append(", ")
                    .Append("r.").Append(V1.Resource.IsHistory).Append(", ")
                    .Append("r.").Append(V1.Resource.IsDeleted).Append(", ")
                    .Append("r.").Append(V1.Resource.ResourceSurrogateId).Append(", ")
                    .Append("r.").Append(V1.Resource.LastUpdated).Append(", ")
                    .Append("r.").Append(V1.Resource.RequestMethod).Append(", ")
                    .Append("r.").AppendLine(V1.Resource.RawResource);
            }

            StringBuilder.Append("FROM ").Append(V1.Resource).AppendLine(" r");

            using (var delimitedClause = StringBuilder.DelimitWhereClause())
            {
                if (expression.NormalizedPredicates.Count > 0)
                {
                    delimitedClause.BeginDelimitedElement();
                    StringBuilder.Append("r.").Append(V1.Resource.ResourceSurrogateId).Append(" IN (SELECT Sid1 FROM ").Append(TableExpressionName(_tableExpressionCounter)).Append(")");
                }

                foreach (var denormalizedPredicate in expression.DenormalizedPredicates)
                {
                    delimitedClause.BeginDelimitedElement();
                    denormalizedPredicate.AcceptVisitor(this, context);
                }

                AppendHistoryClause(delimitedClause);
                AppendDeletedClause(delimitedClause);
            }

            if (!searchOptions.CountOnly)
            {
                StringBuilder.Append("ORDER BY r.").Append(V1.Resource.ResourceSurrogateId).Append(" ASC");
            }

            return null;
        }

        private string TableExpressionName(int id) => "cte" + id;

        public object VisitTable(TableExpression tableExpression, object context)
        {
            var searchOptions = (SearchOptions)context;

            StringBuilder.Append("SELECT DISTINCT ");

            bool withTopClause = _tableExpressionCounter == _totalTableExpressions && !searchOptions.CountOnly;

            if (withTopClause)
            {
                StringBuilder.Append("TOP ").Append(searchOptions.MaxItemCount + 1).Append(" ");
            }

            StringBuilder.Append(V1.Resource.ResourceSurrogateId).AppendLine(" AS Sid1")
                .Append("FROM ");

            if (tableExpression.NormalizedPredicate is MissingSearchParameterExpression missing && missing.IsMissing)
            {
                if (_tableExpressionCounter == 1)
                {
                    StringBuilder.AppendLine().AppendLine("(");

                    using (StringBuilder.Indent())
                    {
                        StringBuilder.Append("SELECT ").AppendLine(V1.Resource.ResourceSurrogateId).Append("FROM ").AppendLine(V1.Resource);
                        using (var delimited = StringBuilder.DelimitWhereClause())
                        {
                            AppendHistoryClause(delimited);
                            AppendDeletedClause(delimited);
                            if (tableExpression.DenormalizedPredicate != null)
                            {
                                delimited.BeginDelimitedElement();
                                tableExpression.DenormalizedPredicate?.AcceptVisitor(this, context);
                            }
                        }
                    }

                    StringBuilder.Append(")");
                }
                else
                {
                    StringBuilder.Append(TableExpressionName(_tableExpressionCounter - 1));
                }

                // give an alias to the derived table or cte
                StringBuilder.AppendLine(" t");

                StringBuilder.Append("WHERE t.").Append(V1.Resource.ResourceSurrogateId).AppendLine(" NOT IN");
                StringBuilder.AppendLine("(");

                using (StringBuilder.Indent())
                {
                    StringBuilder.Append("SELECT ").AppendLine(V1.Resource.ResourceSurrogateId)
                        .Append("FROM ").AppendLine(tableExpression.TableHandler.Table);
                    using (var delimited = StringBuilder.DelimitWhereClause())
                    {
                        AppendHistoryClause(delimited);

                        if (tableExpression.DenormalizedPredicate != null)
                        {
                            delimited.BeginDelimitedElement();
                            tableExpression.DenormalizedPredicate?.AcceptVisitor(this, context);
                        }

                        delimited.BeginDelimitedElement();
                        missing.AcceptVisitor(tableExpression.TableHandler, this);
                    }
                }

                StringBuilder.AppendLine(")");

                if (withTopClause)
                {
                    StringBuilder.Append("ORDER BY t.").Append(V1.Resource.ResourceSurrogateId).AppendLine(" ASC");
                }
            }
            else
            {
                StringBuilder.AppendLine(tableExpression.TableHandler.Table);

                using (var delimited = StringBuilder.DelimitWhereClause())
                {
                    AppendHistoryClause(delimited);

                    if (_tableExpressionCounter > 1)
                    {
                        delimited.BeginDelimitedElement();
                        StringBuilder.Append(V1.Resource.ResourceSurrogateId).Append(" IN (SELECT Sid1 FROM ").Append(TableExpressionName(_tableExpressionCounter - 1)).Append(")");
                    }

                    if (tableExpression.DenormalizedPredicate != null)
                    {
                        delimited.BeginDelimitedElement();
                        tableExpression.DenormalizedPredicate?.AcceptVisitor(this, context);
                    }

                    if (tableExpression.NormalizedPredicate != null)
                    {
                        delimited.BeginDelimitedElement();
                        tableExpression.NormalizedPredicate.AcceptVisitor(tableExpression.TableHandler, this);
                    }
                }

                if (withTopClause)
                {
                    StringBuilder.Append("ORDER BY ").Append(V1.Resource.ResourceSurrogateId).AppendLine(" ASC");
                }
            }

            return null;
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

        public object VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            expression.AcceptVisitor(GetSearchParameterQueryGenerator(expression), this);
            return null;
        }

        public object VisitMissingSearchParameter(MissingSearchParameterExpression expression, object context)
        {
            expression.AcceptVisitor(GetSearchParameterQueryGenerator(expression), this);
            return null;
        }

        public object VisitMultiary(MultiaryExpression expression, object context)
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

        public object VisitCompartment(CompartmentSearchExpression expression, object context) => throw new NotImplementedException();

        public object VisitBinary(BinaryExpression expression, object context) => throw new NotImplementedException();

        public object VisitString(StringExpression expression, object context) => throw new NotImplementedException();

        public object VisitChained(ChainedExpression expression, object context) => throw new NotImplementedException();

        public object VisitMissingField(MissingFieldExpression expression, object context) => throw new NotImplementedException();
    }
}
