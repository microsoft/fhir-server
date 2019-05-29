// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal class ExpressionWithSqlRootRewriter : ExpressionRewriterWithDefaultInitialContext<int>
    {
        public static readonly ExpressionWithSqlRootRewriter Instance = new ExpressionWithSqlRootRewriter();

        public override Expression VisitMultiary(MultiaryExpression expression, int context)
        {
            if (expression.MultiaryOperation != MultiaryOperator.And)
            {
                throw new InvalidOperationException("Or is not supported as a top-level expression");
            }

            List<Expression> resourceLevelCriteria = null;
            List<TableExpression> joinedCriteria = null;

            for (var i = 0; i < expression.Expressions.Count; i++)
            {
                Expression childExpression = expression.Expressions[i];
                if (!IsResourceLevelPredicate(childExpression))
                {
                    if (joinedCriteria == null)
                    {
                        joinedCriteria = new List<TableExpression>();
                        resourceLevelCriteria = new List<Expression>();
                        for (int j = 0; j < i; j++)
                        {
                            resourceLevelCriteria.Add(expression.Expressions[j]);
                        }
                    }

                    joinedCriteria.Add(new TableExpression(childExpression));
                }
                else
                {
                    resourceLevelCriteria?.Add(expression);
                }
            }

            if (joinedCriteria == null)
            {
                SqlRootExpression.WithDenormalizedPredicates(expression.Expressions);
            }

            return new SqlRootExpression(
                joinedCriteria ?? (IReadOnlyList<TableExpression>)Array.Empty<TableExpression>(),
                resourceLevelCriteria ?? expression.Expressions);
        }

        public override Expression VisitSearchParameter(SearchParameterExpression expression, int context) => ConvertNonMultiary(expression);

        public override Expression VisitCompartment(CompartmentSearchExpression expression, int context) => ConvertNonMultiary(expression);

        public override Expression VisitMissingSearchParameter(MissingSearchParameterExpression expression, int context) => ConvertNonMultiary(expression);

        public override Expression VisitChained(ChainedExpression expression, int context) => ConvertNonMultiary(expression);

        private Expression ConvertNonMultiary(Expression expression)
        {
            return IsResourceLevelPredicate(expression)
                ? SqlRootExpression.WithDenormalizedPredicates(expression)
                : SqlRootExpression.WithNormalizedPredicates(new TableExpression(expression));
        }

        private static bool IsResourceLevelPredicate(Expression expression)
        {
            switch (expression)
            {
                case SearchParameterExpressionBase parameterExpression:
                    return IsResourceLevelPredicate(parameterExpression.Parameter);
                case CompartmentSearchExpression _:
                    return false;
                case ChainedExpression _:
                    throw new NotSupportedException();
                default:
                    throw new InvalidOperationException($"Unexpected expression {expression}");
            }
        }

        private static bool IsResourceLevelPredicate(SearchParameterInfo searchParameterInfo)
        {
            switch (searchParameterInfo.Name)
            {
                case SearchParameterNames.Id:
                case SearchParameterNames.LastUpdated:
                case SearchParameterNames.ResourceType:
                case SqlSearchParameters.ResourceSurrogateIdParameterName:
                    return true;
                default:
                    return false;
            }
        }
    }
}
