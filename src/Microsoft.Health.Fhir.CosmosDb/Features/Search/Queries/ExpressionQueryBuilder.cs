// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries
{
    internal class ExpressionQueryBuilder : IExpressionVisitor
    {
        private static readonly Dictionary<BinaryOperator, string> BinaryOperatorMapping = new Dictionary<BinaryOperator, string>()
        {
            { BinaryOperator.Equal, "=" },
            { BinaryOperator.GreaterThan, ">" },
            { BinaryOperator.GreaterThanOrEqual, ">=" },
            { BinaryOperator.LessThan, "<" },
            { BinaryOperator.LessThanOrEqual, "<=" },
            { BinaryOperator.NotEqual, "!=" },
        };

        private static readonly Dictionary<FieldName, string> FieldNameMapping = new Dictionary<FieldName, string>()
        {
            { FieldName.DateTimeEnd, SearchValueConstants.DateTimeEndName },
            { FieldName.DateTimeStart, SearchValueConstants.DateTimeStartName },
            { FieldName.Number, SearchValueConstants.NumberName },
            { FieldName.ParamName, SearchValueConstants.ParamName },
            { FieldName.Quantity, SearchValueConstants.QuantityName },
            { FieldName.QuantityCode, SearchValueConstants.CodeName },
            { FieldName.QuantitySystem, SearchValueConstants.SystemName },
            { FieldName.Reference, SearchValueConstants.ReferenceName },
            { FieldName.ReferenceBaseUri, SearchValueConstants.ReferenceBaseUriName },
            { FieldName.ReferenceResourceId, SearchValueConstants.ReferenceResourceIdName },
            { FieldName.ReferenceResourceType, SearchValueConstants.ReferenceResourceTypeName },
            { FieldName.String, SearchValueConstants.StringName },
            { FieldName.TokenCode, SearchValueConstants.CodeName },
            { FieldName.TokenSystem, SearchValueConstants.SystemName },
            { FieldName.TokenText, SearchValueConstants.TextName },
            { FieldName.Uri, SearchValueConstants.UriName },
        };

        private static readonly Dictionary<StringOperator, string> StringOperatorMapping = new Dictionary<StringOperator, string>()
        {
            { StringOperator.Contains, "CONTAINS" },
            { StringOperator.EndsWith, "ENDSWITH" },
            { StringOperator.NotContains, "NOT CONTAINS" },
            { StringOperator.NotEndsWith, "NOT ENDSWITH" },
            { StringOperator.NotStartsWith, "NOT STARTSWITH" },
            { StringOperator.StartsWith, "STARTSWITH" },
        };

        private readonly StringBuilder _queryBuilder;
        private readonly QueryParameterManager _queryParameterManager;

        internal ExpressionQueryBuilder(
            StringBuilder queryBuilder,
            QueryParameterManager queryParameterManager)
        {
            EnsureArg.IsNotNull(queryBuilder, nameof(queryBuilder));
            EnsureArg.IsNotNull(queryParameterManager, nameof(queryParameterManager));

            _queryBuilder = queryBuilder;
            _queryParameterManager = queryParameterManager;
        }

        public void Visit(SearchParameterExpression expression)
        {
            throw new NotImplementedException();
        }

        public void AppendSubquery(Expression expression)
        {
            _queryBuilder.Append("EXISTS (SELECT VALUE ")
                .Append(SearchValueConstants.SearchIndexAliasName)
                .Append(" FROM ")
                .Append(SearchValueConstants.SearchIndexAliasName)
                .Append(" IN ")
                .Append(SearchValueConstants.RootAliasName)
                .Append(".")
                .Append(KnownResourceWrapperProperties.SearchIndices)
                .Append(" WHERE ");

            expression.AcceptVisitor(this);

            _queryBuilder.AppendLine(")");
        }

        public void Visit(BinaryExpression expression)
        {
            string paramName = AddParameterMapping(expression.Value);

            _queryBuilder
                .Append(SearchValueConstants.SearchIndexAliasName).Append(".").Append(GetFieldName(expression))
                .Append(" ")
                .Append(GetMappedValue(BinaryOperatorMapping, expression.BinaryOperator))
                .Append(" ")
                .Append(paramName);
        }

        public void Visit(ChainedExpression expression)
        {
            // TODO: This will be removed once it's impelmented.
            throw new SearchOperationNotSupportedException("ChainedExpression is not supported.");
        }

        public void Visit(MissingFieldExpression expression)
        {
            _queryBuilder
                .Append("NOT IS_DEFINED(")
                .Append(SearchValueConstants.SearchIndexAliasName).Append(".").Append(GetFieldName(expression))
                .Append(")");
        }

        public void Visit(MissingSearchParameterExpression expression)
        {
            // TODO: This will be removed once it's impelmented.
            throw new SearchOperationNotSupportedException("MissingParamExpression is not supported.");
        }

        public void Visit(MultiaryExpression expression)
        {
            string operation;

            switch (expression.MultiaryOperation)
            {
                case MultiaryOperator.And:
                    operation = "AND";
                    break;

                case MultiaryOperator.Or:
                    operation = "OR";
                    break;

                default:
                    {
                        string message = string.Format(
                            CultureInfo.InvariantCulture,
                            Resources.UnhandledEnumValue,
                            nameof(MultiaryOperator),
                            expression.MultiaryOperation);

                        Debug.Fail(message);

                        throw new InvalidOperationException(message);
                    }
            }

            IReadOnlyList<Expression> expressions = expression.Expressions;

            _queryBuilder.Append("(");

            for (int i = 0; i < expressions.Count; i++)
            {
                // Output each expression.
                expressions[i].AcceptVisitor(this);

                if (i != expressions.Count - 1)
                {
                    _queryBuilder.Append(" ").Append(operation).Append(" ");
                }
            }

            _queryBuilder.Append(")");
        }

        public void Visit(StringExpression expression)
        {
            string fieldName = GetFieldName(expression);

            if (expression.IgnoreCase)
            {
                fieldName = SearchValueConstants.NormalizedPrefix + fieldName;
            }

            string value = expression.IgnoreCase ?
                expression.Value.ToUpperInvariant() :
                expression.Value;

            if (expression.StringOperator == StringOperator.Equals)
            {
                _queryBuilder
                    .Append(SearchValueConstants.SearchIndexAliasName).Append(".").Append(fieldName)
                    .Append(" = ")
                    .Append(AddParameterMapping(value));
            }
            else
            {
                _queryBuilder
                    .Append(GetMappedValue(StringOperatorMapping, expression.StringOperator))
                    .Append("(")
                    .Append(SearchValueConstants.SearchIndexAliasName).Append(".").Append(fieldName)
                    .Append(", ")
                    .Append(AddParameterMapping(value))
                    .Append(")");
            }
        }

        private static string GetFieldName(IFieldExpression fieldExpression)
        {
            string fieldNameInString = GetMappedValue(FieldNameMapping, fieldExpression.FieldName);

            if (fieldExpression.ComponentIndex == null)
            {
                return fieldNameInString;
            }

            return $"{fieldNameInString}_{fieldExpression.ComponentIndex.Value}";
        }

        private static string GetMappedValue<T>(Dictionary<T, string> mapping, T key)
        {
            if (mapping.TryGetValue(key, out string value))
            {
                return value;
            }

            string message = string.Format(Resources.UnhandledEnumValue, typeof(T).Name, key);

            Debug.Fail(message);

            throw new InvalidOperationException(message);
        }

        private string AddParameterMapping(object value)
        {
            // Date has to be handled specially since it's output using 'o' specifier
            // when serialized and comparison is done as a string so we will have
            // to make sure that date is serialized here in the same way.
            switch (value)
            {
                case DateTime dt:
                    value = dt.ToString("o", CultureInfo.InvariantCulture);
                    break;

                case DateTimeOffset dto:
                    value = dto.ToString("o", CultureInfo.InvariantCulture);
                    break;
            }

            return _queryParameterManager.AddOrGetParameterMapping(value);
        }
    }
}
