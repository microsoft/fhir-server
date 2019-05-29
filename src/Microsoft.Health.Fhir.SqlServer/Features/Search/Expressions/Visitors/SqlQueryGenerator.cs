// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal class SqlQueryGenerator : ISqlExpressionVisitor<object, object>
    {
        private readonly IndentedStringBuilder _sb;
        private readonly SqlQueryParameterManager _parameters;
        private int _tableExpressionCounter;

        public SqlQueryGenerator(IndentedStringBuilder sb, SqlQueryParameterManager parameters)
        {
            EnsureArg.IsNotNull(sb, nameof(sb));
            EnsureArg.IsNotNull(parameters, nameof(parameters));

            _sb = sb;
            _parameters = parameters;
        }

        public object VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (!(context is SearchOptions searchOptions))
            {
                throw new ArgumentException($"Argument should be of type {nameof(SearchOptions)}", nameof(context));
            }

            if (expression.NormalizedPredicates.Count > 0)
            {
                _sb.Append(";WITH ");

                _sb.AppendDelimited($",{Environment.NewLine}", expression.DenormalizedPredicates, (sb, tableExpression) =>
                {
                    sb.Append(TableExpressionName(++_tableExpressionCounter)).AppendLine(" AS").AppendLine("(");

                    using (sb.Indent())
                    {
                        // do stuff
                    }

                    sb.AppendLine(")");
                });
            }

            if (searchOptions.CountOnly)
            {
                _sb.AppendLine("SELECT COUNT_BIG(*)");
            }
            else
            {
                _sb.Append("SELECT TOP ").Append(searchOptions.MaxItemCount).Append(' ')
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

            _sb.Append("FROM ").Append(V1.Resource).AppendLine(" r");

            using (_sb.DelimitWhereClause())
            {
                if (expression.NormalizedPredicates.Count > 0)
                {
                    _sb.BeginDelimitedElement();
                    _sb.Append("r.").Append(V1.Resource.ResourceSurrogateId).Append(" IN (SELECT DISTINCT Sid1 FROM ").Append(TableExpressionName(_tableExpressionCounter)).Append(")");
                }

                foreach (var denormalizedPredicate in expression.DenormalizedPredicates)
                {
                    _sb.BeginDelimitedElement();
                    denormalizedPredicate.AcceptVisitor(this, context);
                }

                _sb.BeginDelimitedElement().Append(V1.Resource.IsHistory).Append(" = 0");
            }

            if (!searchOptions.CountOnly)
            {
                _sb.AppendLine("ORDER BY r.").Append(V1.Resource.ResourceId).Append("ASC");
            }

            return null;
        }

        private string TableExpressionName(int id)
        {
            return "cte" + id;
        }

        public object VisitTable(TableExpression tableExpression, object context)
        {
            throw new System.NotImplementedException();
        }

        public object VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            return expression.Expression.AcceptVisitor(this, expression.Parameter);
        }

        public object VisitBinary(BinaryExpression expression, object context)
        {
            var searchParameterInfo = (SearchParameterInfo)context;

            Column column = FieldNameToColumn(expression);
            _sb.Append(column)
                .Append(expression.ComponentIndex)
                .Append(' ');

            switch (expression.BinaryOperator)
            {
                case BinaryOperator.Equal:
                    _sb.Append("=");
                    break;
                case BinaryOperator.GreaterThan:
                    _sb.Append(">");
                    break;
                case BinaryOperator.GreaterThanOrEqual:
                    _sb.Append(">=");
                    break;
                case BinaryOperator.LessThan:
                    _sb.Append("<");
                    break;
                case BinaryOperator.LessThanOrEqual:
                    _sb.Append("<=");
                    break;
                case BinaryOperator.NotEqual:
                    _sb.Append("<>");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(expression.BinaryOperator.ToString());
            }

            _sb.Append(' ').Append(_parameters.AddParameter(column, expression.Value).ParameterName);

            return null;
        }

        private static Column FieldNameToColumn(BinaryExpression expression)
        {
            switch (expression.FieldName)
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
                    throw new ArgumentOutOfRangeException(expression.FieldName.ToString());
            }
        }

        public object VisitChained(ChainedExpression expression, object context)
        {
            throw new System.NotImplementedException();
        }

        public object VisitMissingField(MissingFieldExpression expression, object context)
        {
            throw new System.NotImplementedException();
        }

        public object VisitMissingSearchParameter(MissingSearchParameterExpression expression, object context)
        {
            throw new System.NotImplementedException();
        }

        public object VisitMultiary(MultiaryExpression expression, object context)
        {
            throw new System.NotImplementedException();
        }

        public object VisitString(StringExpression expression, object context)
        {
            throw new System.NotImplementedException();
        }

        public object VisitCompartment(CompartmentSearchExpression expression, object context)
        {
            throw new System.NotImplementedException();
        }
    }
}
