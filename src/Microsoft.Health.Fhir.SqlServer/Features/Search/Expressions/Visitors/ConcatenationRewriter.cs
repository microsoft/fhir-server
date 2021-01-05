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
            if (expression.TableExpressions.Count == 0)
            {
                return expression;
            }

            List<TableExpression> newTableExpressions = null;
            for (var i = 0; i < expression.TableExpressions.Count; i++)
            {
                TableExpression tableExpression = expression.TableExpressions[i];
                bool found = false;

                switch (tableExpression.Kind)
                {
                    case TableExpressionKind.Chain:
                    case TableExpressionKind.Include:
                    case TableExpressionKind.Sort:
                    case TableExpressionKind.All:
                        // The expressions contained within a ChainExpression, IncludeExpression, SortExpression, AllExpression
                        // have been promoted to TableExpressions in this list and are not considered.
                        break;

                    default:
                        if (_rewritingScout != null)
                        {
                            var newPredicate = tableExpression.Predicate.AcceptVisitor(_rewritingScout, null);
                            if (!ReferenceEquals(newPredicate, tableExpression.Predicate))
                            {
                                found = true;
                                tableExpression = new TableExpression(tableExpression.QueryGenerator, newPredicate, tableExpression.Kind, tableExpression.ChainLevel);
                            }
                        }
                        else
                        {
                            found = tableExpression.Predicate.AcceptVisitor(_booleanScout, null);
                        }

                        break;
                }

                if (found)
                {
                    EnsureAllocatedAndPopulated(ref newTableExpressions, expression.TableExpressions, i);

                    newTableExpressions.Add(tableExpression);
                    newTableExpressions.Add((TableExpression)tableExpression.AcceptVisitor(this, context));
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

            return new SqlRootExpression(newTableExpressions, expression.ResourceExpressions);
        }

        public override Expression VisitTable(TableExpression tableExpression, object context)
        {
            var predicate = tableExpression.Predicate.AcceptVisitor(this, context);

            Debug.Assert(predicate != tableExpression.Predicate, "expecting table expression to have been rewritten for concatenation");

            return new TableExpression(
                tableExpression.QueryGenerator,
                predicate,
                TableExpressionKind.Concatenation,
                tableExpression.ChainLevel);
        }
    }
}
