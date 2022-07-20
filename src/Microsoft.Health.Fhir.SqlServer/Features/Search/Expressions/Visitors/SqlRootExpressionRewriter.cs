// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Constructs a <see cref="SqlRootExpression"/> by partitioning an expression into expressions over search parameter tables and expressions over the Resource table
    /// </summary>
    internal class SqlRootExpressionRewriter : ExpressionRewriterWithInitialContext<int>
    {
        private readonly SearchParamTableExpressionQueryGeneratorFactory _searchParamTableExpressionQueryGeneratorFactory;

        public SqlRootExpressionRewriter(SearchParamTableExpressionQueryGeneratorFactory searchParamTableExpressionQueryGeneratorFactory)
        {
            EnsureArg.IsNotNull(searchParamTableExpressionQueryGeneratorFactory, nameof(searchParamTableExpressionQueryGeneratorFactory));
            _searchParamTableExpressionQueryGeneratorFactory = searchParamTableExpressionQueryGeneratorFactory;
        }

        public override Expression VisitMultiary(MultiaryExpression expression, int context)
        {
            if (expression.MultiaryOperation != MultiaryOperator.And)
            {
                throw new InvalidOperationException("Or is not supported as a top-level expression");
            }

            List<SearchParameterExpressionBase> resourceExpressions = null;
            List<SearchParamTableExpression> tableExpressions = null;

            for (var i = 0; i < expression.Expressions.Count; i++)
            {
                Expression childExpression = expression.Expressions[i];

                if (TryGetSearchParamTableExpressionQueryGenerator(childExpression, out SearchParamTableExpressionQueryGenerator tableExpressionGenerator, out SearchParamTableExpressionKind tableExpressionKind))
                {
                    EnsureAllocatedAndPopulatedChangeType(ref resourceExpressions, expression.Expressions, i);
                    EnsureAllocatedAndPopulated(ref tableExpressions, Array.Empty<SearchParamTableExpression>(), 0);

                    tableExpressions.Add(new SearchParamTableExpression(tableExpressionGenerator, childExpression, tableExpressionKind, tableExpressionKind == SearchParamTableExpressionKind.Chain ? 1 : 0));
                }
                else
                {
                    resourceExpressions?.Add((SearchParameterExpressionBase)childExpression);
                }
            }

            if (tableExpressions == null)
            {
                SearchParameterExpressionBase[] castedResourceExpressions = new SearchParameterExpressionBase[expression.Expressions.Count];

                for (var i = 0; i < expression.Expressions.Count; i++)
                {
                    castedResourceExpressions[i] = (SearchParameterExpressionBase)expression.Expressions[i];
                }

                return SqlRootExpression.WithResourceTableExpressions(castedResourceExpressions);
            }

            if (resourceExpressions == null)
            {
                return SqlRootExpression.WithSearchParamTableExpressions(tableExpressions);
            }

            return new SqlRootExpression(tableExpressions, resourceExpressions);
        }

        public override Expression VisitUnion(UnionExpression expression, int context) => ConvertNonMultiary(expression);

        public override Expression VisitSearchParameter(SearchParameterExpression expression, int context) => ConvertNonMultiary(expression);

        public override Expression VisitCompartment(CompartmentSearchExpression expression, int context) => ConvertNonMultiary(expression);

        public override Expression VisitMissingSearchParameter(MissingSearchParameterExpression expression, int context) => ConvertNonMultiary(expression);

        public override Expression VisitChained(ChainedExpression expression, int context) => ConvertNonMultiary(expression);

        private Expression ConvertNonMultiary(Expression expression)
        {
            if (TryGetSearchParamTableExpressionQueryGenerator(expression, out var generator, out var kind))
            {
                return SqlRootExpression.WithSearchParamTableExpressions(new SearchParamTableExpression(generator, predicate: expression, kind, chainLevel: kind == SearchParamTableExpressionKind.Chain ? 1 : 0));
            }
            else
            {
                return SqlRootExpression.WithResourceTableExpressions((SearchParameterExpressionBase)expression);
            }
        }

        private bool TryGetSearchParamTableExpressionQueryGenerator(Expression expression, out SearchParamTableExpressionQueryGenerator searchParamTableExpressionGenerator, out SearchParamTableExpressionKind kind)
        {
            searchParamTableExpressionGenerator = expression.AcceptVisitor(_searchParamTableExpressionQueryGeneratorFactory);
            switch (searchParamTableExpressionGenerator)
            {
                case ChainLinkQueryGenerator _:
                    kind = SearchParamTableExpressionKind.Chain;
                    break;
                case IncludeQueryGenerator _:
                    kind = SearchParamTableExpressionKind.Include;
                    break;
                default:
                    kind = SearchParamTableExpressionKind.Normal;
                    break;
            }

            return searchParamTableExpressionGenerator != null;
        }
    }
}
