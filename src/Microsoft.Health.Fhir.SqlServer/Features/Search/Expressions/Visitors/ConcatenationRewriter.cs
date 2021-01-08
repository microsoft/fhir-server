// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// An abstract rewriter to rewrite a table expression into a concatenation of two table expressions.
    /// </summary>
    internal abstract class ConcatenationRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        private readonly IExpressionVisitor<object, bool> _booleanScout;
        private readonly ExpressionRewriter<object> _rewritingScout;

        protected ConcatenationRewriter(IExpressionVisitor<object, bool> scout)
        {
            EnsureArg.IsNotNull(scout, nameof(scout));
            _booleanScout = scout;
        }

        protected ConcatenationRewriter(ExpressionRewriter<object> scout)
        {
            EnsureArg.IsNotNull(scout, nameof(scout));
            _rewritingScout = scout;
        }

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
                bool found = false;

                switch (searchParamTableExpression.Kind)
                {
                    case SearchParamTableExpressionKind.Chain:
                    case SearchParamTableExpressionKind.Include:
                    case SearchParamTableExpressionKind.Sort:
                    case SearchParamTableExpressionKind.All:
                        // The expressions contained within a ChainExpression, IncludeExpression, SortExpression, AllExpression
                        // have been promoted to SearchParamTableExpressions in this list and are not considered.
                        break;

                    default:
                        if (_rewritingScout != null)
                        {
                            var newPredicate = searchParamTableExpression.Predicate.AcceptVisitor(_rewritingScout, null);
                            if (!ReferenceEquals(newPredicate, searchParamTableExpression.Predicate))
                            {
                                found = true;
                                searchParamTableExpression = new SearchParamTableExpression(searchParamTableExpression.QueryGenerator, newPredicate, searchParamTableExpression.Kind, searchParamTableExpression.ChainLevel);
                            }
                        }
                        else
                        {
                            found = searchParamTableExpression.Predicate.AcceptVisitor(_booleanScout, null);
                        }

                        break;
                }

                if (found)
                {
                    EnsureAllocatedAndPopulated(ref newTableExpressions, expression.SearchParamTableExpressions, i);

                    newTableExpressions.Add(searchParamTableExpression);
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

            Debug.Assert(predicate != searchParamTableExpression.Predicate, "expecting table expression to have been rewritten for concatenation");

            return new SearchParamTableExpression(
                searchParamTableExpression.QueryGenerator,
                predicate,
                SearchParamTableExpressionKind.Concatenation,
                searchParamTableExpression.ChainLevel);
        }
    }
}
