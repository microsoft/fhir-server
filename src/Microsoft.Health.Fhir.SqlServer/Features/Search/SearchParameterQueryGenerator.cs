// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data.SqlClient;
using System.Text.RegularExpressions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal abstract class SearchParameterQueryGenerator : IExpressionVisitor<SqlQueryGenerator, SqlQueryGenerator>
    {
        private static readonly Regex LikeEscapingRegex = new Regex("[%!\\[\\]_]", RegexOptions.Compiled);

        public SqlQueryGenerator VisitSearchParameter(SearchParameterExpression expression, SqlQueryGenerator context)
        {
            return expression.Expression.AcceptVisitor(this, context);
        }

        public virtual SqlQueryGenerator VisitBinary(BinaryExpression expression, SqlQueryGenerator context)
        {
            throw new System.NotImplementedException();
        }

        public SqlQueryGenerator VisitMissingField(MissingFieldExpression expression, SqlQueryGenerator context)
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
                expression.MultiaryOperation == MultiaryOperator.And ? " AND " : " OR ",
                expression.Expressions,
                (sb, childExpr) => childExpr.AcceptVisitor(this, context));

            if (expression.MultiaryOperation == MultiaryOperator.Or)
            {
                context.StringBuilder.AppendLine(")");
            }

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

        public SqlQueryGenerator VisitCompartment(CompartmentSearchExpression expression, SqlQueryGenerator context)
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

        protected void VisitSimpleBinary(BinaryExpression expression, SqlQueryGenerator context, Column column, object value)
        {
            context.StringBuilder.Append(column);

            VisitBinaryOperator(expression.BinaryOperator, context.StringBuilder);

            context.StringBuilder.Append(context.Parameters.AddParameter(column, value).ParameterName);
        }

        protected void VisitSimpleString(StringExpression expression, SqlQueryGenerator context, StringColumn column, string value)
        {
            context.StringBuilder.Append(column);

            VisitStringOperator(expression.StringOperator, context, column, value);
        }

        private static Column GetColumn(SearchParameterInfo searchParameterInfo, FieldName expressionFieldName)
        {
            switch (searchParameterInfo.Name)
            {
                case SearchParameterNames.Id:
                    return V1.Resource.ResourceId;
                case SearchParameterNames.LastUpdated:
                    return V1.Resource.LastUpdated;
                case SearchParameterNames.ResourceType:
                    return V1.Resource.ResourceTypeId;
                case SqlSearchParameters.ResourceSurrogateIdParameterName:
                    return V1.Resource.ResourceSurrogateId;
            }

            switch (expressionFieldName)
            {
                case FieldName.DateTimeStart:
                    return V1.DateTimeSearchParam.StartDateTime;
                case FieldName.DateTimeEnd:
                    return V1.DateTimeSearchParam.EndDateTime;
                case FieldName.Number:
                    return V1.NumberSearchParam.SingleValue; // TODO: low/high
                case FieldName.ParamName:
                    goto default;
                case FieldName.QuantityCode:
                    return V1.QuantitySearchParam.QuantityCodeId;
                case FieldName.QuantitySystem:
                    return V1.QuantitySearchParam.SystemId;
                case FieldName.Quantity:
                    return V1.QuantitySearchParam.SingleValue; // TODO: low/high
                case FieldName.ReferenceBaseUri:
                    return V1.ReferenceSearchParam.BaseUri;
                case FieldName.ReferenceResourceType:
                    return V1.ReferenceSearchParam.ReferenceResourceTypeId;
                case FieldName.ReferenceResourceId:
                    return V1.ReferenceSearchParam.ReferenceResourceId;
                case FieldName.String:
                    return V1.StringSearchParam.Text; // // TODO: overflow
                case FieldName.TokenCode:
                    return V1.TokenSearchParam.Code;
                case FieldName.TokenSystem:
                    return V1.TokenSearchParam.SystemId;
                case FieldName.TokenText:
                    return V1.TokenText.Text;
                case FieldName.Uri:
                    return V1.UriSearchParam.Uri;
                case SqlFieldName.ResourceSurrogateId:
                    return V1.Resource.ResourceSurrogateId;
                case SqlFieldName.ResourceTypeId:
                    return V1.Resource.ResourceTypeId;
                case SqlFieldName.LastUpdated:
                    return V1.Resource.LastUpdated;
                default:
                    throw new ArgumentOutOfRangeException(expressionFieldName.ToString());
            }
        }
    }

#pragma warning disable SA1402 // File may only contain a single type

    internal class ResourceTypeIdParameterQueryGenerator : SearchParameterQueryGenerator
    {
        public override SqlQueryGenerator VisitBinary(BinaryExpression expression, SqlQueryGenerator context)
        {
            VisitSimpleBinary(expression, context, V1.Resource.ResourceTypeId, context.Model.GetResourceTypeId((string)expression.Value));
            return context;
        }
    }

    internal class ResourceSurrogateIdParameterQueryGenerator : SearchParameterQueryGenerator
    {
        public override SqlQueryGenerator VisitBinary(BinaryExpression expression, SqlQueryGenerator context)
        {
            VisitSimpleBinary(expression, context, V1.Resource.ResourceSurrogateId, expression.Value);
            return context;
        }
    }

    internal class ResourceIdParameterQueryGenerator : SearchParameterQueryGenerator
    {
        public override SqlQueryGenerator VisitString(StringExpression expression, SqlQueryGenerator context)
        {
            VisitSimpleString(expression, context, V1.Resource.ResourceId, expression.Value);

            return context;
        }
    }
#pragma warning restore SA1402 // File may only contain a single type
}
