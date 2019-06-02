﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions
{
    internal class MissingSearchParamVisitor : SqlExpressionRewriterWithDefaultInitialContext<object>
    {
        internal static readonly MissingSearchParamVisitor Instance = new MissingSearchParamVisitor();

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

                    if (expression.NormalizedPredicates.Count == 1)
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

            return new SqlRootExpression(newTableExpressions, expression.DenormalizedPredicates);
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
