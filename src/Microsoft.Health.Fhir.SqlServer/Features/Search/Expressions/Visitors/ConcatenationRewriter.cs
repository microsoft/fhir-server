﻿// -------------------------------------------------------------------------------------------------
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
        private readonly IExpressionVisitor<object, bool> _scout;

        protected ConcatenationRewriter(IExpressionVisitor<object, bool> scout)
        {
            EnsureArg.IsNotNull(scout, nameof(scout));
            _scout = scout;
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

                if (tableExpression.NormalizedPredicate.AcceptVisitor(_scout, null))
                {
                    if (newTableExpressions == null)
                    {
                        newTableExpressions = new List<TableExpression>();
                        for (int j = 0; j < i; j++)
                        {
                            newTableExpressions.Add(expression.TableExpressions[j]);
                        }
                    }

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

            return new SqlRootExpression(newTableExpressions, expression.DenormalizedExpressions);
        }

        public override Expression VisitTable(TableExpression tableExpression, object context)
        {
            var normalizedPredicate = tableExpression.NormalizedPredicate.AcceptVisitor(this, context);

            Debug.Assert(normalizedPredicate != tableExpression.NormalizedPredicate, "expecting table expression to have been rewritten for concatenation");

            return new TableExpression(
                tableExpression.SearchParameterQueryGenerator,
                normalizedPredicate,
                tableExpression.DenormalizedPredicate,
                TableExpressionKind.Concatenation);
        }
    }
}
