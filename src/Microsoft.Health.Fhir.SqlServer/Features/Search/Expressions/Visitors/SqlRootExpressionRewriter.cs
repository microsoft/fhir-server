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
        private readonly TableExpressionQueryGeneratorFactory _tableExpressionQueryGeneratorFactory;

        public SqlRootExpressionRewriter(TableExpressionQueryGeneratorFactory tableExpressionQueryGeneratorFactory)
        {
            EnsureArg.IsNotNull(tableExpressionQueryGeneratorFactory, nameof(tableExpressionQueryGeneratorFactory));
            _tableExpressionQueryGeneratorFactory = tableExpressionQueryGeneratorFactory;
        }

        public override Expression VisitMultiary(MultiaryExpression expression, int context)
        {
            if (expression.MultiaryOperation != MultiaryOperator.And)
            {
                throw new InvalidOperationException("Or is not supported as a top-level expression");
            }

            List<SearchParameterExpressionBase> resourceExpressions = null;
            List<TableExpression> tableExpressions = null;

            for (var i = 0; i < expression.Expressions.Count; i++)
            {
                Expression childExpression = expression.Expressions[i];

                if (TryGetTableExpressionQueryGenerator(childExpression, out var tableExpressionGenerator, out var tableExpressionKind))
                {
                    EnsureAllocatedAndPopulatedChangeType(ref resourceExpressions, expression.Expressions, i);
                    EnsureAllocatedAndPopulated(ref tableExpressions, Array.Empty<TableExpression>(), 0);

                    tableExpressions.Add(new TableExpression(tableExpressionGenerator, childExpression, tableExpressionKind, tableExpressionKind == TableExpressionKind.Chain ? 1 : 0));
                }
                else
                {
                    resourceExpressions?.Add((SearchParameterExpressionBase)childExpression);
                }
            }

            if (tableExpressions == null)
            {
                resourceExpressions = new List<SearchParameterExpressionBase>(expression.Expressions.Count);
                foreach (Expression resourceExpression in expression.Expressions)
                {
                    resourceExpressions.Add((SearchParameterExpressionBase)resourceExpression);
                }

                return SqlRootExpression.WithResourceExpressions(resourceExpressions);
            }

            if (resourceExpressions == null)
            {
                return SqlRootExpression.WithTableExpressions(tableExpressions);
            }

            return new SqlRootExpression(tableExpressions, resourceExpressions);
        }

        public override Expression VisitSearchParameter(SearchParameterExpression expression, int context) => ConvertNonMultiary(expression);

        public override Expression VisitCompartment(CompartmentSearchExpression expression, int context) => ConvertNonMultiary(expression);

        public override Expression VisitMissingSearchParameter(MissingSearchParameterExpression expression, int context) => ConvertNonMultiary(expression);

        public override Expression VisitChained(ChainedExpression expression, int context)
        {
            return ConvertNonMultiary(expression);
        }

        private Expression ConvertNonMultiary(Expression expression)
        {
            return TryGetTableExpressionQueryGenerator(expression, out var generator, out var kind)
                ? SqlRootExpression.WithTableExpressions(new TableExpression(generator, predicate: expression, kind, chainLevel: kind == TableExpressionKind.Chain ? 1 : 0))
                : SqlRootExpression.WithResourceExpressions((SearchParameterExpressionBase)expression);
        }

        private bool TryGetTableExpressionQueryGenerator(Expression expression, out TableExpressionQueryGenerator tableExpressionGenerator, out TableExpressionKind kind)
        {
            tableExpressionGenerator = expression.AcceptVisitor(_tableExpressionQueryGeneratorFactory);
            switch (tableExpressionGenerator)
            {
                case ChainAnchorQueryGenerator _:
                    kind = TableExpressionKind.Chain;
                    break;
                case IncludeQueryGenerator _:
                    kind = TableExpressionKind.Include;
                    break;
                default:
                    kind = TableExpressionKind.Normal;
                    break;
            }

            return tableExpressionGenerator != null;
        }
    }
}
