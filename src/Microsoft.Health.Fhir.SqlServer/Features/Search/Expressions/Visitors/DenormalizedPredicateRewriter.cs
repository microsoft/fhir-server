// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal class DenormalizedPredicateRewriter : ExpressionRewriterWithDefaultInitialContext<object>, ISqlExpressionVisitor<object, Expression>
    {
        public static readonly DenormalizedPredicateRewriter Instance = new DenormalizedPredicateRewriter();

        public Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (expression.NormalizedPredicates.Count == 0 || expression.DenormalizedPredicates.Count == 0)
            {
                return expression;
            }

            Expression extractedDemormalizedExpression = null;
            List<Expression> newDenormalizedPredicates = null;

            for (int i = 0; i < expression.DenormalizedPredicates.Count; i++)
            {
                Expression currentExpression = expression.DenormalizedPredicates[i];

                if (currentExpression is SearchParameterExpression searchParameterExpression)
                {
                    switch (searchParameterExpression.Parameter.Name)
                    {
                        case SqlSearchParameters.ResourceSurrogateIdParameterName:
                        case SearchParameterNames.ResourceType:
                            extractedDemormalizedExpression = extractedDemormalizedExpression == null ? currentExpression : Expression.And(extractedDemormalizedExpression, currentExpression);
                            if (newDenormalizedPredicates == null)
                            {
                                newDenormalizedPredicates = new List<Expression>();
                                for (int j = 0; j < i; j++)
                                {
                                    newDenormalizedPredicates.Add(expression.DenormalizedPredicates[j]);
                                }
                            }

                            break;
                        default:
                            newDenormalizedPredicates?.Add(expression);
                            break;
                    }
                }
            }

            if (extractedDemormalizedExpression == null)
            {
                return expression;
            }

            TableExpression firstTableExpression = expression.NormalizedPredicates[0];

            Expression newDenormalizedPredicate = firstTableExpression.DenormalizedPredicate == null
                ? extractedDemormalizedExpression
                : Expression.And(firstTableExpression.DenormalizedPredicate, extractedDemormalizedExpression);

            firstTableExpression = new TableExpression(firstTableExpression.TableHandler, firstTableExpression.NormalizedPredicate, newDenormalizedPredicate);

            var normalizedPredicates = expression.NormalizedPredicates.ToList();
            normalizedPredicates[0] = firstTableExpression;

            return new SqlRootExpression(normalizedPredicates, newDenormalizedPredicates);
        }

        public Expression VisitTable(TableExpression tableExpression, object context)
        {
            throw new InvalidOperationException();
        }
    }
}
