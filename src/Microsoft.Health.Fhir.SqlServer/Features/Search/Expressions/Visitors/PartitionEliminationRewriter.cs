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

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// The Resource and search parameter tables are partitioned by ResourceTypeId. This rewriter does two things:
    /// 1. Ensures that in the case of a system-wide search (/), we enumerate all types that the resources can be,
    ///    rather that leaving the search. (The SQL optimizer does not always employ partition elimination otherwise)
    /// 2. It expands out a primary key continuation token (<see cref="PrimaryKeyValue"/>) into a <see cref="PrimaryKeyRange"/>,
    ///    which includes the primary key but enumerates the subsequent resource types (depending on the sort order).
    /// </summary>
    internal class PartitionEliminationRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SchemaInformation _schemaInformation;
        private readonly SearchParameterInfo _resourceTypeSearchParameter;
        private SearchParameterExpression _allTypesExpression;

        public PartitionEliminationRewriter(
            ISqlServerFhirModel model,
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

        private SearchParameterExpression GetAllTypesExpression()
        {
            if (_allTypesExpression != null)
            {
                return _allTypesExpression;
            }

            string[] resourceTypes = new string[_model.ResourceTypeIdRange.highestId - _model.ResourceTypeIdRange.lowestId + 1];
            for (short i = 0, typeId = _model.ResourceTypeIdRange.lowestId; typeId <= _model.ResourceTypeIdRange.highestId; typeId++, i++)
            {
                resourceTypes[i] = _model.GetResourceTypeName(typeId);
            }

            _allTypesExpression = Expression.SearchParameter(_resourceTypeSearchParameter, Expression.In(FieldName.TokenCode, null, resourceTypes));

            return _allTypesExpression;
        }

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (_schemaInformation.Current < SchemaVersionConstants.PartitionedTables)
            {
                return expression;
            }

            // Look for primary key continuation token (PrimaryKeyParameter) or _type parameters

            int primaryKeyValueIndex = -1;
            for (var i = 0; i < expression.ResourceTableExpressions.Count; i++)
            {
                SearchParameterInfo parameter = expression.ResourceTableExpressions[i].Parameter;

                if (ReferenceEquals(parameter, SqlSearchParameters.PrimaryKeyParameter))
                {
                    primaryKeyValueIndex = i;
                }
            }

            if (primaryKeyValueIndex < 0)
            {
                return expression;
            }

            // There is a primary key continuation token.
            // Now look at the _type restrictions to construct a PrimaryKeyRange
            // that has only the allowed types.

            var primaryKeyParameter = (SearchParameterExpression)expression.ResourceTableExpressions[primaryKeyValueIndex];

            (short? singleAllowedResourceTypeId, BitArray allowedTypes) = TypeConstraintVisitor.Instance.Visit(expression, _model);

            var existingPrimaryKeyBinaryExpression = (BinaryExpression)primaryKeyParameter.Expression;
            var existingPrimaryKeyValue = (PrimaryKeyValue)existingPrimaryKeyBinaryExpression.Value;

            SearchParameterExpression newSearchParameterExpression;
            if (singleAllowedResourceTypeId != null || allowedTypes == null)
            {
                // we'll keep the existing _type parameter and just need to add a ResourceSurrogateId expression
                newSearchParameterExpression = Expression.SearchParameter(
                    SqlSearchParameters.ResourceSurrogateIdParameter,
                    new BinaryExpression(existingPrimaryKeyBinaryExpression.BinaryOperator, SqlFieldName.ResourceSurrogateId, null, existingPrimaryKeyValue.ResourceSurrogateId));
            }
            else
            {
                // Intersect allowed types with the direction of primary key parameter
                // e.g. if >, then eliminate all types that are <=
                switch (existingPrimaryKeyBinaryExpression.BinaryOperator)
                {
                    case BinaryOperator.GreaterThan:
                        for (int i = existingPrimaryKeyValue.ResourceTypeId; i >= 0; i--)
                        {
                            allowedTypes[i] = false;
                        }

                        break;
                    case BinaryOperator.LessThan:
                        for (int i = existingPrimaryKeyValue.ResourceTypeId; i < allowedTypes.Length; i++)
                        {
                            allowedTypes[i] = false;
                        }

                        break;
                    default:
                        throw new InvalidOperationException($"Unexpected operator {existingPrimaryKeyBinaryExpression.BinaryOperator}");
                }

                newSearchParameterExpression = Expression.SearchParameter(
                    primaryKeyParameter.Parameter,
                    new BinaryExpression(
                        existingPrimaryKeyBinaryExpression.BinaryOperator,
                        existingPrimaryKeyBinaryExpression.FieldName,
                        null,
                        new PrimaryKeyRange(existingPrimaryKeyValue, allowedTypes)));
            }

            var newResourceTableExpressions = new List<SearchParameterExpressionBase>();
            for (var i = 0; i < expression.ResourceTableExpressions.Count; i++)
            {
                if (i == primaryKeyValueIndex || // eliminate the existing primaryKey expression
                    (singleAllowedResourceTypeId == null && // if there are many possible types, the PrimaryKeyRange expression will already be constrained to those types
                     expression.ResourceTableExpressions[i] is SearchParameterExpression searchParameterExpression &&
                     searchParameterExpression.Parameter.Name == SearchParameterNames.ResourceType))
                {
                    continue;
                }

                newResourceTableExpressions.Add(expression.ResourceTableExpressions[i]);
            }

            newResourceTableExpressions.Add(newSearchParameterExpression);
            return new SqlRootExpression(expression.SearchParamTableExpressions, newResourceTableExpressions);
        }
    }
}
