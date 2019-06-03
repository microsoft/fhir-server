﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data.SqlClient;
using System.Text.RegularExpressions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal abstract class SearchParameterQueryGenerator : DefaultExpressionVisitor<SqlQueryGenerator, SqlQueryGenerator>
    {
        private const string DefaultCaseInsensitiveCollation = "Latin1_General_100_CI_AI_SC";
        private const string DefaultCaseSensitiveCollation = "Latin1_General_100_CS_AS";

        private static readonly Regex LikeEscapingRegex = new Regex("[%!\\[\\]_]", RegexOptions.Compiled);

        public override SqlQueryGenerator VisitSearchParameter(SearchParameterExpression expression, SqlQueryGenerator context)
        {
            short searchParamId = context.Model.GetSearchParamId(expression.Parameter.Url);
            SmallIntColumn searchParamIdColumn = V1.SearchParam.SearchParamId;

            context.StringBuilder
                .Append(searchParamIdColumn)
                .Append(" = ")
                .AppendLine(context.Parameters.AddParameter(searchParamIdColumn, searchParamId).ParameterName)
                .Append("AND ");

            return expression.Expression.AcceptVisitor(this, context);
        }

        public override SqlQueryGenerator VisitMissingSearchParameter(MissingSearchParameterExpression expression, SqlQueryGenerator context)
        {
            short searchParamId = context.Model.GetSearchParamId(expression.Parameter.Url);
            SmallIntColumn searchParamIdColumn = V1.SearchParam.SearchParamId;

            context.StringBuilder
                .Append(searchParamIdColumn)
                .Append(" = ")
                .AppendLine(context.Parameters.AddParameter(searchParamIdColumn, searchParamId).ParameterName);

            return context;
        }

        public override SqlQueryGenerator VisitMultiary(MultiaryExpression expression, SqlQueryGenerator context)
        {
            if (expression.MultiaryOperation == MultiaryOperator.Or)
            {
                context.StringBuilder.Append('(');
            }

            context.StringBuilder.AppendDelimited(
                sb => sb.AppendLine().Append(expression.MultiaryOperation == MultiaryOperator.And ? "AND " : "OR "),
                expression.Expressions,
                (sb, childExpr) => childExpr.AcceptVisitor(this, context));

            if (expression.MultiaryOperation == MultiaryOperator.Or)
            {
                context.StringBuilder.Append(")");
            }

            context.StringBuilder.AppendLine();

            return context;
        }

        private static string EscapeValueForLike(string value)
        {
            return LikeEscapingRegex.Replace(value, "!$0");
        }

        protected SqlQueryGenerator VisitSimpleBinary(BinaryOperator binaryOperator, SqlQueryGenerator context, Column column, int? componentIndex, object value)
        {
            context.StringBuilder.Append(column).Append(componentIndex + 1);

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

            context.StringBuilder.Append(context.Parameters.AddParameter(column, value).ParameterName);

            return context;
        }

        protected SqlQueryGenerator VisitSimpleString(StringExpression expression, SqlQueryGenerator context, StringColumn column, string value)
        {
            context.StringBuilder.Append(column).Append(expression.ComponentIndex + 1);

            switch (expression.StringOperator)
            {
                case StringOperator.Contains:
                    SqlParameter containsParameter = context.Parameters.AddParameter(column, $"%{EscapeValueForLike(value)}%");
                    context.StringBuilder.Append(" LIKE ").Append(containsParameter.ParameterName).Append(" ESCAPE '!'");
                    break;
                case StringOperator.EndsWith:
                    SqlParameter endWithParameter = context.Parameters.AddParameter(column, $"%{EscapeValueForLike(value)}");
                    context.StringBuilder.Append(" LIKE ").Append(endWithParameter.ParameterName).Append(" ESCAPE '!'");
                    break;
                case StringOperator.Equals:
                    SqlParameter equalsParameter = context.Parameters.AddParameter(column, value);
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
                    SqlParameter startsWithParameter = context.Parameters.AddParameter(column, $"{EscapeValueForLike(value)}%");
                    context.StringBuilder.Append(" LIKE ").Append(startsWithParameter.ParameterName).Append(" ESCAPE '!'");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(expression.StringOperator.ToString());
            }

            if (column.IsAcentSensitive == null || column.IsCaseSensitive == null ||
                column.IsAcentSensitive == expression.IgnoreCase ||
                column.IsCaseSensitive == expression.IgnoreCase)
            {
                context.StringBuilder.Append(" COLLATE ").Append(expression.IgnoreCase ? DefaultCaseInsensitiveCollation : DefaultCaseSensitiveCollation);
            }

            return context;
        }

        protected static SqlQueryGenerator VisitMissingFieldImpl(MissingFieldExpression expression, SqlQueryGenerator context, FieldName expectedFieldName, Column column)
        {
            if (expression.FieldName != expectedFieldName)
            {
                throw new InvalidOperationException($"Unexpected missing field {expression.FieldName}");
            }

            context.StringBuilder.Append(column).Append(expression.ComponentIndex + 1).Append(" IS NULL");
            return context;
        }
    }
}
