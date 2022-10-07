// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Turns an expression with a :not modifier into a <see cref="TableExpressionKind.NotExists"/>
    /// table expression with the condition negated
    /// </summary>
    internal class NotExpressionRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        internal static readonly NotExpressionRewriter Instance = new NotExpressionRewriter();

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (expression.SearchParamTableExpressions.Count == 0)
            {
                return expression;
            }

            List<SearchParamTableExpression> newTableExpressions = null;
            for (var i = 0; i < expression.SearchParamTableExpressions.Count; i++)
            {
                SearchParamTableExpression tableExpression = expression.SearchParamTableExpressions[i];

                // process only normalized predicates. Ignore Sort as it has its own visitor.
                if (tableExpression.Kind != SearchParamTableExpressionKind.Sort && tableExpression.Predicate?.AcceptVisitor(Scout.Instance, context) == true)
                {
                    EnsureAllocatedAndPopulated(ref newTableExpressions, expression.SearchParamTableExpressions, i);

                    // If this is the first expression, we need to add another expression before it
                    if (i == 0)
                    {
                        // seed with all resources so that we have something to restrict
                        newTableExpressions.Add(
                            new SearchParamTableExpression(
                                tableExpression.QueryGenerator,
                                null,
                                SearchParamTableExpressionKind.Normal));
                    }

                    newTableExpressions.Add((SearchParamTableExpression)tableExpression.AcceptVisitor(this, context));
                }
                else
                {
                    newTableExpressions?.Add(tableExpression);
                }
            }

            if (newTableExpressions == null)
            {
                return expression;
            }

            return new SqlRootExpression(newTableExpressions, expression.ResourceTableExpressions);
        }

        public override Expression VisitTable(SearchParamTableExpression tableExpression, object context)
        {
            var visitedPredicate = tableExpression.Predicate.AcceptVisitor(this, context);

            return new SearchParamTableExpression(
                tableExpression.QueryGenerator,
                visitedPredicate,
                SearchParamTableExpressionKind.NotExists);
        }

        public override Expression VisitNotExpression(NotExpression expression, object context)
        {
            return expression.Expression;
        }

        private class Scout : DefaultExpressionVisitor<object, bool>
        {
            internal static readonly Scout Instance = new Scout();

            private Scout()
                : base((accumulated, current) => current || accumulated)
            {
            }

            public override bool VisitNotExpression(NotExpression expression, object context)
            {
                return true;
            }
        }
    }
}
