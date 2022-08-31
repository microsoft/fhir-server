// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Reorders table expressions by expected selectivity. Most selective are moved to the front.
    /// </summary>
    internal class SearchParamTableExpressionReorderer : SqlExpressionRewriterWithInitialContext<object>
    {
        public static readonly SearchParamTableExpressionReorderer Instance = new SearchParamTableExpressionReorderer();

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (expression.SearchParamTableExpressions.Count <= 1)
            {
                return expression;
            }

            List<SearchParamTableExpression> reorderedExpressions = expression.SearchParamTableExpressions.OrderByDescending(t =>
            {
                if (t.Kind == SearchParamTableExpressionKind.All)
                {
                    return 20;
                }

                if (t.Predicate is MissingSearchParameterExpression)
                {
                    return -10;
                }

                var order = t.Predicate?.AcceptVisitor(Scout.Instance, context);
                if (order != 0)
                {
                    return order;
                }

                switch (t.QueryGenerator)
                {
                    case ReferenceQueryGenerator _:
                        return 10;
                    case CompartmentQueryGenerator _:
                        return 10;
                    case IncludeQueryGenerator _:
                        return -20;
                    default:
                        return 0;
                }
            }).ToList();

            return new SqlRootExpression(reorderedExpressions, expression.ResourceTableExpressions);
        }

        private class Scout : DefaultExpressionVisitor<object, int>
        {
            internal static readonly Scout Instance = new Scout();

            private Scout()
                : base((accumulated, current) => current != 0 ? current : accumulated)
            {
            }

            public override int VisitNotExpression(NotExpression expression, object context)
            {
                return -15;
            }
        }
    }
}
