// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Flattens chained expressions into <see cref="SqlRootExpression"/>'s <see cref="SqlRootExpression.TableExpressions"/> list.
    /// The expression within a chained expression is promoted to a top-level table expression, but we keep track of the height
    /// via the <see cref="TableExpression.ChainLevel"/>.
    /// </summary>
    internal class ChainFlatteningRewriter : SqlExpressionRewriterWithInitialContext<(TableExpression containingTableExpression, int chainLevel)>
    {
        private readonly NormalizedSearchParameterQueryGeneratorFactory _normalizedSearchParameterQueryGeneratorFactory;

        public ChainFlatteningRewriter(NormalizedSearchParameterQueryGeneratorFactory normalizedSearchParameterQueryGeneratorFactory)
        {
            EnsureArg.IsNotNull(normalizedSearchParameterQueryGeneratorFactory, nameof(normalizedSearchParameterQueryGeneratorFactory));
            _normalizedSearchParameterQueryGeneratorFactory = normalizedSearchParameterQueryGeneratorFactory;
        }

        public override Expression VisitChained(ChainedExpression expression, (TableExpression containingTableExpression, int chainLevel) context)
        {
            TableExpression thisTableExpression;
            if (expression.Expression is ChainedExpression)
            {
                thisTableExpression = context.containingTableExpression ??
                                      new TableExpression(
                                          ChainAnchorQueryGenerator.Instance,
                                          expression,
                                          null,
                                          TableExpressionKind.Chain,
                                          context.chainLevel);

                Expression visitedExpression = expression.Expression.AcceptVisitor(this, (null, context.chainLevel + 1));

                switch (visitedExpression)
                {
                    case TableExpression child:
                        return Expression.And(thisTableExpression, child);
                    case MultiaryExpression multiary when multiary.MultiaryOperation == MultiaryOperator.And:
                        var tableExpressions = new List<TableExpression> { thisTableExpression };
                        tableExpressions.AddRange(multiary.Expressions.Cast<TableExpression>());
                        return Expression.And(tableExpressions);
                    default:
                        throw new InvalidOperationException("Unexpected return type");
                }
            }

            NormalizedSearchParameterQueryGenerator normalizedParameterQueryGenerator = expression.Expression.AcceptVisitor(_normalizedSearchParameterQueryGeneratorFactory);

            thisTableExpression = context.containingTableExpression;

            if (thisTableExpression == null || normalizedParameterQueryGenerator == null)
            {
                thisTableExpression = new TableExpression(
                    ChainAnchorQueryGenerator.Instance,
                    expression,
                    denormalizedPredicate: normalizedParameterQueryGenerator == null ? expression.Expression : null,
                    TableExpressionKind.Chain,
                    context.chainLevel);
            }

            if (normalizedParameterQueryGenerator == null)
            {
                return thisTableExpression;
            }

            var childTableExpression = new TableExpression(normalizedParameterQueryGenerator, expression.Expression, null, TableExpressionKind.Normal, context.chainLevel);

            return Expression.And(thisTableExpression, childTableExpression);
        }

        public override Expression VisitSqlRoot(SqlRootExpression expression, (TableExpression containingTableExpression, int chainLevel) context)
        {
            List<TableExpression> newTableExpressions = null;
            for (var i = 0; i < expression.TableExpressions.Count; i++)
            {
                TableExpression tableExpression = expression.TableExpressions[i];
                if (tableExpression.Kind != TableExpressionKind.Chain)
                {
                    newTableExpressions?.Add(tableExpression);
                    continue;
                }

                Expression visitedNormalizedPredicate = tableExpression.NormalizedPredicate.AcceptVisitor(this, (tableExpression, tableExpression.ChainLevel));
                switch (visitedNormalizedPredicate)
                {
                    case TableExpression convertedExpression:
                        EnsureAllocatedAndPopulated(ref newTableExpressions, expression.TableExpressions, i);
                        newTableExpressions.Add(convertedExpression);
                        break;
                    case MultiaryExpression multiary when multiary.MultiaryOperation == MultiaryOperator.And:
                        EnsureAllocatedAndPopulated(ref newTableExpressions, expression.TableExpressions, i);

                        newTableExpressions.AddRange(multiary.Expressions.Cast<TableExpression>());
                        break;
                }
            }

            if (newTableExpressions == null)
            {
                return expression;
            }

            return new SqlRootExpression(newTableExpressions, expression.DenormalizedExpressions);
        }
    }
}
