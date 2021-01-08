// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Flattens chained expressions into <see cref="SqlRootExpression"/>'s <see cref="SqlRootExpression.SearchParamTableExpressions"/> list.
    /// The expression within a chained expression is promoted to a top-level table expression, but we keep track of the height
    /// via the <see cref="SearchParamTableExpression.ChainLevel"/>.
    /// </summary>
    internal class ChainFlatteningRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        private readonly SearchParamTableExpressionQueryGeneratorFactory _searchParamTableExpressionQueryGeneratorFactory;

        public ChainFlatteningRewriter(SearchParamTableExpressionQueryGeneratorFactory searchParamTableExpressionQueryGeneratorFactory)
        {
            EnsureArg.IsNotNull(searchParamTableExpressionQueryGeneratorFactory, nameof(searchParamTableExpressionQueryGeneratorFactory));
            _searchParamTableExpressionQueryGeneratorFactory = searchParamTableExpressionQueryGeneratorFactory;
        }

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            List<SearchParamTableExpression> newTableExpressions = null;
            for (var i = 0; i < expression.SearchParamTableExpressions.Count; i++)
            {
                SearchParamTableExpression searchParamTableExpression = expression.SearchParamTableExpressions[i];
                if (searchParamTableExpression.Kind != SearchParamTableExpressionKind.Chain)
                {
                    newTableExpressions?.Add(searchParamTableExpression);
                    continue;
                }

                EnsureAllocatedAndPopulated(ref newTableExpressions, expression.SearchParamTableExpressions, i);

                ProcessChainedExpression((ChainedExpression)searchParamTableExpression.Predicate, newTableExpressions, 1);
            }

            if (newTableExpressions == null)
            {
                return expression;
            }

            return new SqlRootExpression(newTableExpressions, expression.ResourceTableExpressions);
        }

        private void ProcessChainedExpression(ChainedExpression chainedExpression, List<SearchParamTableExpression> tableExpressions, int chainLevel)
        {
            SearchParamTableExpressionQueryGenerator queryGenerator = chainedExpression.Expression.AcceptVisitor(_searchParamTableExpressionQueryGeneratorFactory, null);

            Expression expressionOnTarget = queryGenerator == null ? chainedExpression.Expression : null;

            var sqlChainLinkExpression = new SqlChainLinkExpression(
                chainedExpression.ResourceTypes,
                chainedExpression.ReferenceSearchParameter,
                chainedExpression.TargetResourceTypes,
                chainedExpression.Reversed,
                expressionOnTarget: expressionOnTarget);

            tableExpressions.Add(
                new SearchParamTableExpression(
                    ChainLinkQueryGenerator.Instance,
                    sqlChainLinkExpression,
                    SearchParamTableExpressionKind.Chain,
                    chainLevel));

            if (chainedExpression.Expression is ChainedExpression nestedChainedExpression)
            {
                ProcessChainedExpression(nestedChainedExpression, tableExpressions, chainLevel + 1);
            }
            else if (queryGenerator != null)
            {
                tableExpressions.Add(
                    new SearchParamTableExpression(
                        queryGenerator,
                        chainedExpression.Expression,
                        SearchParamTableExpressionKind.Normal,
                        chainLevel));
            }
        }
    }
}
