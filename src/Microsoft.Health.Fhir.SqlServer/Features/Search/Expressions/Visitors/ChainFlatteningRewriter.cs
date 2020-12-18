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

                ProcessChainedExpression((ChainedExpression)tableExpression.NormalizedPredicate, newTableExpressions, 1);
            }

            if (newTableExpressions == null)
            {
                return expression;
            }

            return new SqlRootExpression(newTableExpressions, expression.ResourceExpressions);
        }

        private void ProcessChainedExpression(ChainedExpression chainedExpression, List<TableExpression> tableExpressions, int chainLevel)
        {
            NormalizedSearchParameterQueryGenerator queryGenerator = chainedExpression.Expression.AcceptVisitor(_normalizedSearchParameterQueryGeneratorFactory, null);

            Expression expressionOnTarget = queryGenerator == null ? chainedExpression.Expression : null;

            var sqlChainLinkExpression = new SqlChainLinkExpression(
                chainedExpression.ResourceType,
                chainedExpression.ReferenceSearchParameter,
                chainedExpression.TargetResourceType,
                chainedExpression.Reversed,
                expressionOnTarget: expressionOnTarget);

            tableExpressions.Add(
                new TableExpression(
                    ChainAnchorQueryGenerator.Instance,
                    sqlChainLinkExpression,
                    null,
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
                        null,
                        TableExpressionKind.Normal,
                        chainLevel));
            }
        }
    }
}
