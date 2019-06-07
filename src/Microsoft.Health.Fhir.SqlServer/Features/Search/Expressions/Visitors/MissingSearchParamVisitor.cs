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
    /// <see cref="TableExpressionKind.NotExists"/> table expression with the condition negated
    /// </summary>
    internal class MissingSearchParamVisitor : SqlExpressionRewriterWithInitialContext<object>
    {
        internal static readonly MissingSearchParamVisitor Instance = new MissingSearchParamVisitor();

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

                if (tableExpression.NormalizedPredicate.AcceptVisitor(Scout.Instance, null))
                {
                    EnsureAllocatedAndPopulated(ref newTableExpressions, expression.TableExpressions, i);

                    if (expression.TableExpressions.Count == 1)
                    {
                        // seed with all resources so that we have something to restrict
                        newTableExpressions.Add(
                            new TableExpression(
                                tableExpression.SearchParameterQueryGenerator,
                                null,
                                tableExpression.DenormalizedPredicate,
                                TableExpressionKind.All));
                    }

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

            return new SqlRootExpression(newTableExpressions, expression.DenormalizedExpressions);
        }

        public override Expression VisitTable(TableExpression tableExpression, object context)
        {
            var normalizedPredicate = tableExpression.NormalizedPredicate.AcceptVisitor(this, context);

            return new TableExpression(
                tableExpression.SearchParameterQueryGenerator,
                normalizedPredicate,
                tableExpression.DenormalizedPredicate,
                TableExpressionKind.NotExists);
        }

        public override Expression VisitMissingSearchParameter(MissingSearchParameterExpression expression, object context)
        {
            if (expression.IsMissing)
            {
                return Expression.MissingSearchParameter(expression.Parameter, false);
            }

            return expression;
        }

        private class Scout : DefaultExpressionVisitor<object, bool>
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
