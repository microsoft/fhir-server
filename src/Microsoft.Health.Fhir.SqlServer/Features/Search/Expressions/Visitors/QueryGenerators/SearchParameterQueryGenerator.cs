// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal abstract class SearchParameterQueryGenerator : DefaultExpressionVisitor<SearchParameterQueryGeneratorContext, SearchParameterQueryGeneratorContext>
    {
        private const string DefaultCaseInsensitiveCollation = "Latin1_General_100_CI_AI_SC";
        private const string DefaultCaseSensitiveCollation = "Latin1_General_100_CS_AS";

        private static readonly Regex LikeEscapingRegex = new Regex("[%!\\[\\]_]", RegexOptions.Compiled);

        public override SearchParameterQueryGeneratorContext VisitSearchParameter(SearchParameterExpression expression, SearchParameterQueryGeneratorContext context)
        {
            SearchParameterQueryGenerator delegatedGenerator = GetSearchParameterQueryGeneratorIfResourceColumnSearchParameter(expression);
            if (delegatedGenerator != null)
            {
                // This is a search parameter over a column that exists on the Resource table or both the Resource table and search parameter tables.
                // Delegate to the visitor specific to it.
                return expression.Expression.AcceptVisitor(delegatedGenerator, context);
            }

            short searchParamId = context.Model.GetSearchParamId(expression.Parameter.Url);
            SmallIntColumn searchParamIdColumn = VLatest.SearchParam.SearchParamId;

            context.StringBuilder
                .Append(searchParamIdColumn, context.TableAlias)
                .Append(" = ")
                .AppendLine(context.Parameters.AddParameter(searchParamIdColumn, searchParamId, true).ParameterName)
                .Append("AND ");

            return expression.Expression.AcceptVisitor(this, context);
        }

        public override SearchParameterQueryGeneratorContext VisitSortParameter(SortExpression expression, SearchParameterQueryGeneratorContext context)
        {
            short searchParamId = context.Model.GetSearchParamId(expression.Parameter.Url);
            var searchParamIdColumn = VLatest.SearchParam.SearchParamId;

            context.StringBuilder
                .Append(searchParamIdColumn, context.TableAlias)
                .Append(" = ")
                .Append(context.Parameters.AddParameter(searchParamIdColumn, searchParamId, true).ParameterName)
                .Append(" ");

            return context;
        }

        public override SearchParameterQueryGeneratorContext VisitMissingSearchParameter(MissingSearchParameterExpression expression, SearchParameterQueryGeneratorContext context)
        {
            SearchParameterQueryGenerator delegatedGenerator = GetSearchParameterQueryGeneratorIfResourceColumnSearchParameter(expression);
            if (delegatedGenerator != null)
            {
                // This is a search parameter over a column that exists on the Resource table or both the Resource table and search parameter tables.
                // Delegate to the visitor specific to it.
                return expression.AcceptVisitor(delegatedGenerator, context);
            }

            Debug.Assert(!expression.IsMissing, "IsMissing=true expressions should have been rewritten");

            short searchParamId = context.Model.GetSearchParamId(expression.Parameter.Url);
            SmallIntColumn searchParamIdColumn = VLatest.SearchParam.SearchParamId;

            context.StringBuilder
                .Append(searchParamIdColumn, context.TableAlias)
                .Append(" = ")
                .Append(context.Parameters.AddParameter(searchParamIdColumn, searchParamId, true).ParameterName)
                .Append(" ");

            return context;
        }

        public override SearchParameterQueryGeneratorContext VisitNotExpression(NotExpression expression, SearchParameterQueryGeneratorContext context)
        {
            context.StringBuilder.Append("NOT ");
            return base.VisitNotExpression(expression, context);
        }

        public override SearchParameterQueryGeneratorContext VisitMultiary(MultiaryExpression expression, SearchParameterQueryGeneratorContext context)
        {
            bool isOrMultinaryOperator = expression.MultiaryOperation == MultiaryOperator.Or;

            if (isOrMultinaryOperator)
            {
                context.StringBuilder.Append('(');
            }

            context.StringBuilder.AppendDelimited(
                sb => sb.AppendLine().Append(expression.MultiaryOperation == MultiaryOperator.And ? "AND " : "OR "),
                expression.Expressions,
                (sb, childExpr) =>
                {
                    if (isOrMultinaryOperator)
                    {
                        context.StringBuilder.Append('(');
                    }

                    childExpr.AcceptVisitor(this, context);

                    if (isOrMultinaryOperator)
                    {
                        context.StringBuilder.Append(')');
                    }
                });

            if (isOrMultinaryOperator)
            {
                context.StringBuilder.Append(')');
            }

            context.StringBuilder.Append(" "); // Replaced CR by space keeping code "protection".

            return context;
        }

        protected static SearchParameterQueryGenerator GetSearchParameterQueryGeneratorIfResourceColumnSearchParameter(SearchParameterExpressionBase searchParameter)
        {
            switch (searchParameter.Parameter.Code)
            {
                case SearchParameterNames.Id:
                    return ResourceIdParameterQueryGenerator.Instance;
                case SearchParameterNames.ResourceType:
                    return ResourceTypeIdParameterQueryGenerator.Instance;
                case SqlSearchParameters.ResourceSurrogateIdParameterName:
                    return ResourceSurrogateIdParameterQueryGenerator.Instance;
                case SqlSearchParameters.PrimaryKeyParameterName:
                    return PrimaryKeyRangeParameterQueryGenerator.Instance;
#if DEBUG
                case SearchParameterNames.LastUpdated:
                    throw new InvalidOperationException($"Expression with {SearchParameterNames.LastUpdated} parameter should have been rewritten to use {SqlSearchParameters.ResourceSurrogateIdParameterName}.");
#endif
                default:
                    return null;
            }
        }

        private static bool TryEscapeValueForLike(ref string value)
        {
            var escapedValue = LikeEscapingRegex.Replace(value, "!$0");
            if (escapedValue != value)
            {
                value = escapedValue;
                return true;
            }

            return false;
        }

        protected static SearchParameterQueryGeneratorContext VisitSimpleBinary(BinaryOperator binaryOperator, SearchParameterQueryGeneratorContext context, Column column, int? componentIndex, object value, bool includeInParameterHash = true)
        {
            AppendColumnName(context, column, componentIndex);

            switch (binaryOperator)
            {
                case BinaryOperator.Equal:
                    context.StringBuilder.Append(" = ");
                    break;
                case BinaryOperator.GreaterThan:
                    context.StringBuilder.Append(" > ");
                    break;
                case BinaryOperator.GreaterThanOrEqual:
                    context.StringBuilder.Append(" >= ");
                    break;
                case BinaryOperator.LessThan:
                    context.StringBuilder.Append(" < ");
                    break;
                case BinaryOperator.LessThanOrEqual:
                    context.StringBuilder.Append(" <= ");
                    break;
                case BinaryOperator.NotEqual:
                    context.StringBuilder.Append(" <> ");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(binaryOperator.ToString());
            }

            context.StringBuilder.Append(context.Parameters.AddParameter(column, value, includeInParameterHash).ParameterName);

            return context;
        }

        protected static SearchParameterQueryGeneratorContext VisitSimpleString(StringExpression expression, SearchParameterQueryGeneratorContext context, StringColumn column, string value)
        {
            if (expression.StringOperator != StringOperator.LeftSideStartsWith)
            {
                AppendColumnName(context, column, expression);
            }

            bool needsEscaping = false;
            switch (expression.StringOperator)
            {
                case StringOperator.Contains:
                    needsEscaping = TryEscapeValueForLike(ref value);
                    SqlParameter containsParameter = context.Parameters.AddParameter(column, $"%{value}%", true);
                    context.StringBuilder.Append(" LIKE ").Append(containsParameter.ParameterName);
                    break;
                case StringOperator.EndsWith:
                    needsEscaping = TryEscapeValueForLike(ref value);
                    SqlParameter endWithParameter = context.Parameters.AddParameter(column, $"%{value}", true);
                    context.StringBuilder.Append(" LIKE ").Append(endWithParameter.ParameterName);
                    break;
                case StringOperator.Equals:
                    SqlParameter equalsParameter = context.Parameters.AddParameter(column, value, true);
                    context.StringBuilder.Append(" = ").Append(equalsParameter.ParameterName);
                    break;
                case StringOperator.NotContains:
                    context.StringBuilder.Append(" NOT ");
                    goto case StringOperator.Contains;
                case StringOperator.NotEndsWith:
                    context.StringBuilder.Append(" NOT ");
                    goto case StringOperator.EndsWith;
                case StringOperator.NotStartsWith:
                    context.StringBuilder.Append(" NOT ");
                    goto case StringOperator.StartsWith;
                case StringOperator.StartsWith:
                    needsEscaping = TryEscapeValueForLike(ref value);
                    SqlParameter startsWithParameter = context.Parameters.AddParameter(column, $"{value}%", true);
                    context.StringBuilder.Append(" LIKE ").Append(startsWithParameter.ParameterName);
                    break;
                case StringOperator.LeftSideStartsWith:
                    needsEscaping = TryEscapeValueForLike(ref value);
                    SqlParameter leftParameter = context.Parameters.AddParameter(column, $"{value}", true);
                    context.StringBuilder.Append(leftParameter.ParameterName).Append(" LIKE ");
                    AppendColumnName(context, column, expression);
                    context.StringBuilder.Append("+'%'");
                    break;

                default:
                    throw new ArgumentOutOfRangeException(expression.StringOperator.ToString());
            }

            if (needsEscaping)
            {
                context.StringBuilder.Append(" ESCAPE '!'");
            }

            if (column.IsAcentSensitive == null || column.IsCaseSensitive == null ||
                column.IsAcentSensitive == expression.IgnoreCase ||
                column.IsCaseSensitive == expression.IgnoreCase)
            {
                if (!expression.IgnoreCase && expression.StringOperator == StringOperator.Equals && column.IsAcentSensitive != null && column.IsCaseSensitive != null)
                {
                    // We are doing a case/accent sensitive query over a column that is case/accent insensitive.
                    // We can improve efficiency of the query by including an accent/case insensitive predicate
                    // in addition to the sensitive one. This allows the optimizer choose an index seek.

                    context.StringBuilder.Append(" AND ");
                    AppendColumnName(context, column, expression);
                    SqlParameter equalsParameter = context.Parameters.AddParameter(column, value, true);
                    context.StringBuilder.Append(" = ").Append(equalsParameter.ParameterName);
                }

                context.StringBuilder.Append(" COLLATE ").Append(expression.IgnoreCase ? DefaultCaseInsensitiveCollation : DefaultCaseSensitiveCollation);
            }

            return context;
        }

        protected static SearchParameterQueryGeneratorContext VisitSimpleIn<T>(SearchParameterQueryGeneratorContext context, Column column, IReadOnlyList<T> values)
        {
            context.StringBuilder.Append(column, context.TableAlias);
            context.StringBuilder.Append(" IN (");

            for (int index = 0; index < values.Count; index++)
            {
                T item = values[index];

                context.StringBuilder.Append(context.Parameters.AddParameter(column, item, true));

                if (index < values.Count - 1)
                {
                    context.StringBuilder.Append(",");
                }
            }

            context.StringBuilder.Append(") "); // Replaced CR by space keeping code "protection".

            return context;
        }

        protected static SearchParameterQueryGeneratorContext VisitMissingFieldImpl(MissingFieldExpression expression, SearchParameterQueryGeneratorContext context, FieldName expectedFieldName, Column column)
        {
            if (expression.FieldName != expectedFieldName)
            {
                throw new InvalidOperationException($"Unexpected missing field {expression.FieldName}");
            }

            AppendColumnName(context, column, expression).Append(" IS NULL");
            return context;
        }

        protected static IndentedStringBuilder AppendColumnName(SearchParameterQueryGeneratorContext context, Column column, IFieldExpression expression)
        {
            return AppendColumnName(context, column, expression.ComponentIndex);
        }

        protected static IndentedStringBuilder AppendColumnName(SearchParameterQueryGeneratorContext context, Column column, int? componentIndex)
        {
            return context.StringBuilder.Append(column, context.TableAlias).Append(componentIndex + 1);
        }
    }
}
