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
using Microsoft.Health.CosmosDb.Features.Queries;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries
{
    internal class ExpressionQueryBuilder : IExpressionVisitorWithInitialContext<ExpressionQueryBuilder.Context, object>
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

        private static readonly Dictionary<string, string> CompartmentTypeToParamName = new Dictionary<string, string>
        {
            { KnownCompartmentTypes.Device, KnownResourceWrapperProperties.Device },
            { KnownCompartmentTypes.Encounter, KnownResourceWrapperProperties.Encounter },
            { KnownCompartmentTypes.Patient, KnownResourceWrapperProperties.Patient },
            { KnownCompartmentTypes.Practitioner, KnownResourceWrapperProperties.Practitioner },
            { KnownCompartmentTypes.RelatedPerson, KnownResourceWrapperProperties.RelatedPerson },
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

        Context IExpressionVisitorWithInitialContext<Context, object>.InitialContext => new Context(instanceVariableName: SearchValueConstants.RootAliasName, fieldNameOverride: null);

        public object VisitSearchParameter(SearchParameterExpression expression, Context context)
        {
            if (expression.Parameter.Name == SearchParameterNames.ResourceType)
            {
                // We do not currently support specifying the system for the _type parameter value.
                // We would need to add it to the document, but for now it seems pretty unlikely that it will
                // be specified when searching.
                expression.Expression.AcceptVisitor(this, context.WithFieldNameOverride(SearchValueConstants.RootResourceTypeName));
            }
            else if (expression.Parameter.Name == SearchParameterNames.LastUpdated)
            {
                // For LastUpdate queries, the LastModified property on the root is
                // more performant than the searchIndices _lastUpdated.st and _lastUpdate.et
                // we will override the mapping for that
                expression.Expression.AcceptVisitor(this, context.WithFieldNameOverride(SearchValueConstants.LastModified));
            }
            else
            {
                AppendSubquery(expression.Parameter.Name, expression.Expression, context);
            }

            _queryBuilder.AppendLine();

            return null;
        }

        public object VisitMissingSearchParameter(MissingSearchParameterExpression expression, Context context)
        {
            if (expression.Parameter.Name == SearchParameterNames.ResourceType)
            {
                // this will always be present
                _queryBuilder.Append(expression.IsMissing ? "false" : "true");
            }
            else
            {
                AppendSubquery(expression.Parameter.Name, null, negate: expression.IsMissing, context: context);
            }

            _queryBuilder.AppendLine();

            return null;
        }

        private void AppendSubquery(string parameterName, Expression expression, Context context, bool negate = false)
        {
            if (negate)
            {
                _queryBuilder.Append("NOT ");
            }

            _queryBuilder.Append("EXISTS (SELECT VALUE ")
                .Append(SearchValueConstants.SearchIndexAliasName)
                .Append(" FROM ")
                .Append(SearchValueConstants.SearchIndexAliasName)
                .Append(" IN ")
                .Append(SearchValueConstants.RootAliasName)
                .Append(".")
                .Append(KnownResourceWrapperProperties.SearchIndices)
                .Append(" WHERE ");

            context = context.WithInstanceVariableName(SearchValueConstants.SearchIndexAliasName);

            VisitBinary(GetMappedValue(FieldNameMapping, FieldName.ParamName), BinaryOperator.Equal, parameterName, context);

            if (expression != null)
            {
                _queryBuilder.Append(" AND ");

                expression.AcceptVisitor(this, context);
            }

            _queryBuilder.Append(")");
        }

        public object VisitBinary(BinaryExpression expression, Context context)
        {
            VisitBinary(GetFieldName(expression, context), expression.BinaryOperator, expression.Value, context);
            return null;
        }

        public object VisitChained(ChainedExpression expression, Context context)
        {
            // TODO: This will be removed once it's implemented.
            throw new SearchOperationNotSupportedException("ChainedExpression is not supported.");
        }

        public object VisitMissingField(MissingFieldExpression expression, Context context)
        {
            _queryBuilder
                .Append("NOT IS_DEFINED(")
                .Append(context.InstanceVariableName).Append(".").Append(GetFieldName(expression, context))
                .Append(")");
            return null;
        }

        public object VisitMultiary(MultiaryExpression expression, Context context)
        {
            MultiaryOperator op = expression.MultiaryOperation;
            IReadOnlyList<Expression> expressions = expression.Expressions;
            string operation;

            switch (op)
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
                        op);

                    Debug.Fail(message);

                    throw new InvalidOperationException(message);
                }
            }

            if (op == MultiaryOperator.Or)
            {
                _queryBuilder.Append("(");
            }

            for (int i = 0; i < expressions.Count; i++)
            {
                // Output each expression.
                expressions[i].AcceptVisitor(this, context);

                if (i != expressions.Count - 1)
                {
                    if (!char.IsWhiteSpace(_queryBuilder[_queryBuilder.Length - 1]))
                    {
                        _queryBuilder.Append(" ");
                    }

                    _queryBuilder.Append(operation).Append(" ");
                }
            }

            if (op == MultiaryOperator.Or)
            {
                _queryBuilder.Append(")");
            }

            return null;
        }

        public object VisitString(StringExpression expression, Context context)
        {
            string fieldName = GetFieldName(expression, context);

            if (expression.IgnoreCase)
            {
                fieldName = SearchValueConstants.NormalizedPrefix + fieldName;
            }

            string value = expression.IgnoreCase ? expression.Value.ToUpperInvariant() : expression.Value;

            if (expression.StringOperator == StringOperator.Equals)
            {
                _queryBuilder
                    .Append(context.InstanceVariableName).Append(".").Append(fieldName)
                    .Append(" = ")
                    .Append(AddParameterMapping(value));
            }
            else
            {
                _queryBuilder
                    .Append(GetMappedValue(StringOperatorMapping, expression.StringOperator))
                    .Append("(")
                    .Append(context.InstanceVariableName).Append(".").Append(fieldName)
                    .Append(", ")
                    .Append(AddParameterMapping(value))
                    .Append(")");
            }

            return null;
        }

        public object VisitCompartment(CompartmentSearchExpression expression, Context context)
        {
            AppendArrayContainsFilter(GetCompartmentIndicesParamName(expression.CompartmentType), expression.CompartmentId);
            return null;
        }

        private static string GetCompartmentIndicesParamName(string compartmentType)
        {
            Debug.Assert(CompartmentTypeToParamName.ContainsKey(compartmentType), $"CompartmentType {compartmentType} should have a corresponding index param");
            return $"{KnownResourceWrapperProperties.CompartmentIndices}.{CompartmentTypeToParamName[compartmentType]}";
        }

        private void VisitBinary(string fieldName, BinaryOperator op, object value, Context state)
        {
            string paramName = AddParameterMapping(value);

            _queryBuilder
                .Append(state.InstanceVariableName).Append(".").Append(fieldName)
                .Append(" ")
                .Append(GetMappedValue(BinaryOperatorMapping, op))
                .Append(" ")
                .Append(paramName);
        }

        private string GetFieldName(IFieldExpression fieldExpression, Context state)
        {
            if (state.FieldNameOverride != null)
            {
                return state.FieldNameOverride;
            }

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

        private void AppendArrayContainsFilter(string name, string value)
        {
            _queryBuilder
                .Append("ARRAY_CONTAINS(")
                .Append(SearchValueConstants.RootAliasName).Append(".").Append(name)
                .Append(", ")
                .Append(_queryParameterManager.AddOrGetParameterMapping(value))
                .AppendLine(")");
        }

        /// <summary>
        /// Context that is passed through the visit.
        /// </summary>
        internal struct Context
        {
            public Context(string instanceVariableName, string fieldNameOverride)
            {
                InstanceVariableName = instanceVariableName;
                FieldNameOverride = fieldNameOverride;
            }

            public string InstanceVariableName { get; }

            public string FieldNameOverride { get; }

            public Context WithInstanceVariableName(string instanceVariableName)
            {
                return new Context(instanceVariableName: instanceVariableName, fieldNameOverride: FieldNameOverride);
            }

            public Context WithFieldNameOverride(string fieldNameOverride)
            {
                return new Context(instanceVariableName: InstanceVariableName, fieldNameOverride: fieldNameOverride);
            }
        }
    }
}
