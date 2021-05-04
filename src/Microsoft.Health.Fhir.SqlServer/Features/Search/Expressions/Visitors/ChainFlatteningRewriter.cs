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

        /// <summary>
        /// Chained expression can be created either by search service or member-match service. In first case chain expression would contain only one nested expression
        /// because this is how '_has' works. `_has:Observation:patient:code=1234-5` as example. In that case we carry on, visit expression, and created proper table expression.
        /// For member-match service we can pass multiple restrain expression to chained expression which we put behind 'And' expression.
        /// 'And' visitor unfortunetelly can pick up only one queryGenerator, so it will pick first one in 'And' list and if expression inside 'And' are for different tables,
        /// that would lead to incorrect table expressions. So we handle that case separatly and create table generator for each expression in `And` expression.
        /// </summary>
        private void ProcessChainedExpression(ChainedExpression chainedExpression, List<SearchParamTableExpression> tableExpressions, int chainLevel)
        {
            if (chainedExpression.Expression is MultiaryExpression multiaryExpression && multiaryExpression.MultiaryOperation == MultiaryOperator.And)
            {
                HandleAndExpression(chainedExpression, tableExpressions, chainLevel, multiaryExpression);
            }
            else
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

        /// <summary>
        /// And expression inside chained expression wouldn't be handled properly because we can have different types of paramaters to filter on.
        /// But acceptVisitor method returns only one table generator (first we encounter).
        /// This method takes table generator for each subexpression and create table expression for it.
        /// </summary>
        private void HandleAndExpression(ChainedExpression chainedExpression, List<SearchParamTableExpression> tableExpressions, int chainLevel, MultiaryExpression multiaryExpression)
        {
            var chainLinkExpression = new SqlChainLinkExpression(
                chainedExpression.ResourceTypes,
                chainedExpression.ReferenceSearchParameter,
                chainedExpression.TargetResourceTypes,
                chainedExpression.Reversed);

            tableExpressions.Add(new SearchParamTableExpression(
                                  ChainLinkQueryGenerator.Instance,
                                  chainLinkExpression,
                                  SearchParamTableExpressionKind.Chain,
                                  chainLevel));

            foreach (var expression in multiaryExpression.Expressions)
            {
                var handler = expression.AcceptVisitor(_searchParamTableExpressionQueryGeneratorFactory, null);
                tableExpressions.Add(
                    new SearchParamTableExpression(
                        handler,
                        expression,
                        SearchParamTableExpressionKind.Normal,
                        chainLevel));
            }
        }
    }
}
