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
    /// Flattens chained expressions into <see cref="SqlRootExpression"/>'s <see cref="SqlRootExpression.TableExpressions"/> list.
    /// The expression within a chained expression is promoted to a top-level table expression, but we keep track of the height
    /// via the <see cref="TableExpression.ChainLevel"/>.
    /// </summary>
    internal class ChainFlatteningRewriter : SqlExpressionRewriterWithInitialContext<(TableExpression containingTableExpression, int chainLevel)>
    {
        private readonly TableExpressionQueryGeneratorFactory _tableExpressionQueryGeneratorFactory;

        public ChainFlatteningRewriter(TableExpressionQueryGeneratorFactory tableExpressionQueryGeneratorFactory)
        {
            EnsureArg.IsNotNull(tableExpressionQueryGeneratorFactory, nameof(tableExpressionQueryGeneratorFactory));
            _tableExpressionQueryGeneratorFactory = tableExpressionQueryGeneratorFactory;
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

                EnsureAllocatedAndPopulated(ref newTableExpressions, expression.TableExpressions, i);

                ProcessChainedExpression((ChainedExpression)tableExpression.Predicate, newTableExpressions, 1);
            }

            if (newTableExpressions == null)
            {
                return expression;
            }

            return new SqlRootExpression(newTableExpressions, expression.ResourceExpressions);
        }

        private void ProcessChainedExpression(ChainedExpression chainedExpression, List<TableExpression> tableExpressions, int chainLevel)
        {
            TableExpressionQueryGenerator queryGenerator = chainedExpression.Expression.AcceptVisitor(_tableExpressionQueryGeneratorFactory, null);

            Expression expressionOnTarget = queryGenerator == null ? chainedExpression.Expression : null;

            var sqlChainLinkExpression = new SqlChainLinkExpression(
                chainedExpression.ResourceTypes,
                chainedExpression.ReferenceSearchParameter,
                chainedExpression.TargetResourceTypes,
                chainedExpression.Reversed,
                expressionOnTarget: expressionOnTarget);

            tableExpressions.Add(
                new TableExpression(
                    ChainAnchorQueryGenerator.Instance,
                    sqlChainLinkExpression,
                    TableExpressionKind.Chain,
                    chainLevel));

            if (chainedExpression.Expression is ChainedExpression nestedChainedExpression)
            {
                ProcessChainedExpression(nestedChainedExpression, tableExpressions, chainLevel + 1);
            }
            else if (queryGenerator != null)
            {
                tableExpressions.Add(
                    new TableExpression(
                        queryGenerator,
                        chainedExpression.Expression,
                        TableExpressionKind.Normal,
                        chainLevel));
            }
        }
    }
}
