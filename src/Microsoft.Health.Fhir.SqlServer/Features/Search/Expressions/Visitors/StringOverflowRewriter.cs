// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal class StringOverflowRewriter : SqlExpressionRewriterWithDefaultInitialContext<object>
    {
        internal static readonly StringOverflowRewriter Instance = new StringOverflowRewriter();

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (expression.NormalizedPredicates.Count == 0)
            {
                return expression;
            }

            List<TableExpression> newTableExpressions = null;
            for (var i = 0; i < expression.NormalizedPredicates.Count; i++)
            {
                TableExpression tableExpression = expression.NormalizedPredicates[i];

                if (tableExpression.NormalizedPredicate.AcceptVisitor(Scout.Instance, null))
                {
                    if (newTableExpressions == null)
                    {
                        newTableExpressions = new List<TableExpression>();
                        for (int j = 0; j < i; j++)
                        {
                            newTableExpressions.Add(expression.NormalizedPredicates[j]);
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

            return new SqlRootExpression(newTableExpressions, expression.DenormalizedPredicates);
        }

        public override Expression VisitTable(TableExpression tableExpression, object context)
        {
            var normalizedPredicate = tableExpression.NormalizedPredicate.AcceptVisitor(this, context);

            return new TableExpression(
                tableExpression.SearchParameterQueryGenerator,
                normalizedPredicate,
                tableExpression.DenormalizedPredicate,
                TableExpressionKind.Concatenation);
        }

        public override Expression VisitString(StringExpression expression, object context)
        {
            return new StringExpression(expression.StringOperator, SqlFieldName.TextOverflow, expression.ComponentIndex, expression.Value, expression.IgnoreCase);
        }

        private class Scout : DefaultExpressionVisitor<object, bool>
        {
            internal static readonly Scout Instance = new Scout();

            private Scout()
                : base((accumulated, current) => accumulated || current)
            {
            }

            public override bool VisitString(StringExpression expression, object context)
            {
                switch (expression.StringOperator)
                {
                    case StringOperator.Equals:
                    case StringOperator.NotStartsWith:
                    case StringOperator.StartsWith:
                        if (expression.Value.Length < V1.StringSearchParam.Text.Metadata.MaxLength / 2)
                        {
                            return false;
                        }

                        return true;

                    default:
                        return true;
                }
            }
        }
    }
}
