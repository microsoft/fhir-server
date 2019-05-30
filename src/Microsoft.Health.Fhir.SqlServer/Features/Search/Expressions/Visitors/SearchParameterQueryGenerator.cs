// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data.SqlClient;
using System.Text.RegularExpressions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal abstract class SearchParameterQueryGenerator : IExpressionVisitor<SqlQueryGenerator, SqlQueryGenerator>
    {
        private static readonly Regex LikeEscapingRegex = new Regex("[%!\\[\\]_]", RegexOptions.Compiled);

        public virtual SqlQueryGenerator VisitSearchParameter(SearchParameterExpression expression, SqlQueryGenerator context)
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

        public virtual SqlQueryGenerator VisitBinary(BinaryExpression expression, SqlQueryGenerator context)
        {
            throw new System.NotImplementedException();
        }

        public virtual SqlQueryGenerator VisitMissingField(MissingFieldExpression expression, SqlQueryGenerator context)
        {
            throw new System.NotSupportedException();
        }

        public SqlQueryGenerator VisitMissingSearchParameter(MissingSearchParameterExpression expression, SqlQueryGenerator context)
        {
            throw new System.NotImplementedException();
        }

        public SqlQueryGenerator VisitMultiary(MultiaryExpression expression, SqlQueryGenerator context)
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

        public virtual SqlQueryGenerator VisitString(StringExpression expression, SqlQueryGenerator context)
        {
            throw new System.NotImplementedException();
        }

        public SqlQueryGenerator VisitChained(ChainedExpression expression, SqlQueryGenerator context)
        {
            throw new System.NotSupportedException();
        }

        public virtual SqlQueryGenerator VisitCompartment(CompartmentSearchExpression expression, SqlQueryGenerator context)
        {
            throw new System.NotSupportedException();
        }

        private static string EscapeValueForLike(string value)
        {
            return LikeEscapingRegex.Replace(value, "!$0");
        }

        protected static void VisitStringOperator(StringOperator stringOperator, SqlQueryGenerator context, StringColumn column, string value)
        {
            switch (stringOperator)
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
                    throw new ArgumentOutOfRangeException(stringOperator.ToString());
            }
        }

        protected static void VisitBinaryOperator(BinaryOperator binaryOperator, IndentedStringBuilder sb)
        {
            switch (binaryOperator)
            {
                case BinaryOperator.Equal:
                    sb.Append(" = ");
                    break;
                case BinaryOperator.GreaterThan:
                    sb.Append(" > ");
                    break;
                case BinaryOperator.GreaterThanOrEqual:
                    sb.Append(" >= ");
                    break;
                case BinaryOperator.LessThan:
                    sb.Append(" < ");
                    break;
                case BinaryOperator.LessThanOrEqual:
                    sb.Append(" <= ");
                    break;
                case BinaryOperator.NotEqual:
                    sb.Append(" <> ");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(binaryOperator.ToString());
            }
        }

        protected void VisitSimpleBinary(BinaryOperator binaryOperator, SqlQueryGenerator context, Column column, object value)
        {
            context.StringBuilder.Append(column);

            VisitBinaryOperator(binaryOperator, context.StringBuilder);

            context.StringBuilder.Append(context.Parameters.AddParameter(column, value).ParameterName);
        }

        protected void VisitSimpleString(StringExpression expression, SqlQueryGenerator context, StringColumn column, string value)
        {
            context.StringBuilder.Append(column);

            VisitStringOperator(expression.StringOperator, context, column, value);
        }
    }

#pragma warning disable SA1402 // File may only contain a single type

    internal class DenormalizedSearchParameterQueryGenerator : SearchParameterQueryGenerator
    {
        public override SqlQueryGenerator VisitSearchParameter(SearchParameterExpression expression, SqlQueryGenerator context)
        {
            return expression.Expression.AcceptVisitor(this, context);
        }
    }

    internal class ResourceTypeIdParameterQueryGenerator : DenormalizedSearchParameterQueryGenerator
    {
        public override SqlQueryGenerator VisitBinary(BinaryExpression expression, SqlQueryGenerator context)
        {
            VisitSimpleBinary(expression.BinaryOperator, context, V1.Resource.ResourceTypeId, context.Model.GetResourceTypeId((string)expression.Value));
            return context;
        }
    }

    internal class ResourceSurrogateIdParameterQueryGenerator : DenormalizedSearchParameterQueryGenerator
    {
        public override SqlQueryGenerator VisitBinary(BinaryExpression expression, SqlQueryGenerator context)
        {
            VisitSimpleBinary(expression.BinaryOperator, context, V1.Resource.ResourceSurrogateId, expression.Value);
            return context;
        }
    }

    internal class ResourceIdParameterQueryGenerator : DenormalizedSearchParameterQueryGenerator
    {
        public override SqlQueryGenerator VisitString(StringExpression expression, SqlQueryGenerator context)
        {
            VisitSimpleString(expression, context, V1.Resource.ResourceId, expression.Value);

            return context;
        }
    }
#pragma warning restore SA1402 // File may only contain a single type
}
