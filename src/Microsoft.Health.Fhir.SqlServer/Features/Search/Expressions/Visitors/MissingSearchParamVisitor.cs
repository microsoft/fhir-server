// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Turns an expression with a :missing=true search parameter expression and turns it into a
    /// <see cref="SearchParamTableExpressionKind.NotExists"/> table expression with the condition negated
    /// </summary>
    internal class MissingSearchParamVisitor : SqlExpressionRewriterWithInitialContext<object>
    {
        internal static readonly MissingSearchParamVisitor Instance = new MissingSearchParamVisitor();

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (expression.SearchParamTableExpressions.Count == 0)
            {
                return expression;
            }

            List<SearchParamTableExpression> newTableExpressions = null;
            for (var i = 0; i < expression.SearchParamTableExpressions.Count; i++)
            {
                SearchParamTableExpression searchParamTableExpression = expression.SearchParamTableExpressions[i];

                // Ignore Sort as it has its own visitor.
                if (searchParamTableExpression.Kind != SearchParamTableExpressionKind.Sort && searchParamTableExpression.Predicate?.AcceptVisitor(Scout.Instance, null) == true)
                {
                    EnsureAllocatedAndPopulated(ref newTableExpressions, expression.SearchParamTableExpressions, i);

                    // If this is the first expression, we need to add another expression before it
                    if (i == 0)
                    {
                        // seed with all resources so that we have something to restrict
                        newTableExpressions.Add(
                            new SearchParamTableExpression(
                                searchParamTableExpression.QueryGenerator,
                                null,
                                SearchParamTableExpressionKind.All));
                    }

                    newTableExpressions.Add((SearchParamTableExpression)searchParamTableExpression.AcceptVisitor(this, context));
                }
                else
                {
                    newTableExpressions?.Add(searchParamTableExpression);
                }
            }

            if (newTableExpressions == null)
            {
                return expression;
            }

            return new SqlRootExpression(newTableExpressions, expression.ResourceTableExpressions);
        }

        public override Expression VisitTable(SearchParamTableExpression searchParamTableExpression, object context)
        {
            var predicate = searchParamTableExpression.Predicate.AcceptVisitor(this, context);

            return new SearchParamTableExpression(
                searchParamTableExpression.QueryGenerator,
                predicate,
                SearchParamTableExpressionKind.NotExists);
        }

        public override Expression VisitMissingSearchParameter(MissingSearchParameterExpression expression, object context)
        {
            if (expression.IsMissing)
            {
                return Expression.MissingSearchParameter(expression.Parameter, false);
            }

            return expression;
        }

        private class Scout : DefaultSqlExpressionVisitor<object, bool>
        {
            internal static readonly Scout Instance = new Scout();

            private Scout()
                : base((accumulated, current) => accumulated || current)
            {
            }

            public override bool VisitMissingSearchParameter(MissingSearchParameterExpression expression, object context)
            {
                return expression.IsMissing;
            }
        }
    }
}
