// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.SqlServer.Management.Dmf;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal class ContinuationTokenSimplifier : SqlExpressionRewriterWithInitialContext<object>
    {
        private readonly SqlServerFhirModel _model;
        private readonly SchemaInformation _schemaInformation;
        private readonly SearchParameterInfo _resourceTypeSearchParameter;
        private SearchParameterExpression _allTypesExpression;

        public ContinuationTokenSimplifier(
            SqlServerFhirModel model,
            SchemaInformation schemaInformation,
            ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver searchParameterDefinitionManagerResolver)
        {
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(searchParameterDefinitionManagerResolver, nameof(searchParameterDefinitionManagerResolver));

            _model = model;
            _schemaInformation = schemaInformation;
            _resourceTypeSearchParameter = searchParameterDefinitionManagerResolver.Invoke().GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.ResourceType);
        }

        private SearchParameterExpression AllTypesExpression
        {
            get
            {
                if (_allTypesExpression != null)
                {
                    return _allTypesExpression;
                }

                var typeExpressions = new Expression[_model.ResourceTypeIdRange.highestId - _model.ResourceTypeIdRange.lowestId + 1];

                for (short i = 0, typeId = _model.ResourceTypeIdRange.lowestId; typeId <= _model.ResourceTypeIdRange.highestId; typeId++, i++)
                {
                    typeExpressions[i] = Expression.StringEquals(FieldName.TokenCode, null, _model.GetResourceTypeName(typeId), false);
                }

                _allTypesExpression = Expression.SearchParameter(
                    _resourceTypeSearchParameter,
                    Expression.Or(typeExpressions));

                return _allTypesExpression;
            }
        }

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            var allowedTypes = new BitArray(_model.ResourceTypeIdRange.highestId + 1, true);
            for (int i = 0; i < _model.ResourceTypeIdRange.lowestId; i++)
            {
                allowedTypes[i] = false;
            }

            int keySetIndex = -1;
            bool hasTypeRestriction = false;
            for (var i = 0; i < expression.ResourceTableExpressions.Count; i++)
            {
                SearchParameterInfo parameter = expression.ResourceTableExpressions[i].Parameter;

                if (ReferenceEquals(parameter, SqlSearchParameters.ResourceTypeIdResourceSurrogateKeySetParameter))
                {
                    keySetIndex = i;
                }
                else if (ReferenceEquals(parameter, _resourceTypeSearchParameter))
                {
                    hasTypeRestriction = true;
                }
            }

            if (hasTypeRestriction && keySetIndex < 0)
            {
                return expression;
            }

            if (!hasTypeRestriction && keySetIndex < 0)
            {
                if (_schemaInformation.Current < SchemaVersionConstants.PartitionedTables)
                {
                    return expression;
                }

                var updatedResourceTableExpressions = new List<SearchParameterExpressionBase>(expression.ResourceTableExpressions.Count + 1);
                updatedResourceTableExpressions.AddRange(expression.ResourceTableExpressions);
                updatedResourceTableExpressions.Add(AllTypesExpression);

                return new SqlRootExpression(expression.SearchParamTableExpressions, updatedResourceTableExpressions);
            }

            var existingKeySetParameter = (SearchParameterExpression)expression.ResourceTableExpressions[keySetIndex];

            if (existingKeySetParameter == null)
            {
                return expression;
            }

            int bitCriteria = 0;
            foreach (SearchParameterExpressionBase resourceExpression in expression.ResourceTableExpressions)
            {
                if (resourceExpression is SearchParameterExpression searchParameterExpression && resourceExpression.Parameter.Name == SearchParameterNames.ResourceType)
                {
                    switch (searchParameterExpression.Expression)
                    {
                        case StringExpression stringExpression:
                            bitCriteria++;
                            var thisType = new BitArray(allowedTypes.Length, false);
                            thisType[_model.GetResourceTypeId(stringExpression.Value)] = true;
                            allowedTypes.And(thisType);
                            break;
                        case MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } multiaryExpression:
                            var theseTypes = new BitArray(allowedTypes.Length, false);
                            foreach (Expression childExpression in multiaryExpression.Expressions)
                            {
                                switch (childExpression)
                                {
                                    case StringExpression stringExpression:
                                        bitCriteria++;
                                        theseTypes[_model.GetResourceTypeId(stringExpression.Value)] = true;
                                        break;
                                    default:
                                        throw new InvalidOperandException($"Unexpected expression {childExpression}");
                                }
                            }

                            allowedTypes.And(theseTypes);
                            break;
                        default:
                            throw new InvalidOperandException($"Unexpected expression {searchParameterExpression.Expression}");
                    }
                }
            }

            var existingBinaryKeySetExpression = (BinaryExpression)existingKeySetParameter.Expression;
            var existingKeySetValue = (KeySetValue)existingBinaryKeySetExpression.Value;

            SearchParameterExpression newSearchParameterExpression;
            if (bitCriteria == 1 && allowedTypes[existingKeySetValue.CurrentPositionResourceTypeId])
            {
                newSearchParameterExpression = Expression.SearchParameter(
                    SqlSearchParameters.ResourceSurrogateIdParameter,
                    new BinaryExpression(existingBinaryKeySetExpression.BinaryOperator, SqlFieldName.ResourceSurrogateId, null, existingKeySetValue.CurrentPositionResourceSurrogateId));
            }
            else
            {
                switch (existingBinaryKeySetExpression.BinaryOperator)
                {
                    case BinaryOperator.GreaterThan:
                        for (int i = existingKeySetValue.CurrentPositionResourceTypeId; i >= 0; i--)
                        {
                            allowedTypes[i] = false;
                        }

                        break;
                    case BinaryOperator.LessThan:
                        for (int i = existingKeySetValue.CurrentPositionResourceTypeId; i < allowedTypes.Length; i++)
                        {
                            allowedTypes[i] = false;
                        }

                        break;
                    default:
                        throw new InvalidOperationException($"Unexpected operator {existingBinaryKeySetExpression.BinaryOperator}");
                }

                newSearchParameterExpression = Expression.SearchParameter(
                    existingKeySetParameter.Parameter,
                    new BinaryExpression(existingBinaryKeySetExpression.BinaryOperator, existingBinaryKeySetExpression.FieldName, null, existingKeySetValue with { NextResourceTypeIds = allowedTypes }));
            }

            SearchParameterExpressionBase[] newResourceTableExpressions = null;
            EnsureAllocatedAndPopulated(ref newResourceTableExpressions, expression.ResourceTableExpressions, expression.ResourceTableExpressions.Count);
            newResourceTableExpressions[keySetIndex] = newSearchParameterExpression;

            return new SqlRootExpression(expression.SearchParamTableExpressions, newResourceTableExpressions);
        }
    }
}
