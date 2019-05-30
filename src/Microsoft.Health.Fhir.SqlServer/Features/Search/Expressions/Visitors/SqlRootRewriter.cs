// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.NormalizedTableHandlers;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal class SqlRootRewriter : ExpressionRewriterWithDefaultInitialContext<int>
    {
        private readonly NormalizedTableHandlerFactory _normalizedTableHandlerFactory;

        public SqlRootRewriter(NormalizedTableHandlerFactory normalizedTableHandlerFactory)
        {
            EnsureArg.IsNotNull(normalizedTableHandlerFactory, nameof(normalizedTableHandlerFactory));
            _normalizedTableHandlerFactory = normalizedTableHandlerFactory;
        }

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
                if (TryGetNormalizedTableHandler(childExpression, out var tableHandler))
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

                    joinedCriteria.Add(new TableExpression(tableHandler, childExpression));
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
            return TryGetNormalizedTableHandler(expression, out var tableHandler)
                ? SqlRootExpression.WithNormalizedPredicates(new TableExpression(tableHandler, expression))
                : SqlRootExpression.WithDenormalizedPredicates(expression);
        }

        private bool TryGetNormalizedTableHandler(Expression expression, out NormalizedTableHandler tableHandler)
        {
            tableHandler = expression.AcceptVisitor(_normalizedTableHandlerFactory);
            return tableHandler != null;
        }
    }
}
