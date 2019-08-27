// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal class IncludeDenormalizedRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        public static readonly IncludeDenormalizedRewriter Instance = new IncludeDenormalizedRewriter();

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (expression.TableExpressions.Count == 0)
            {
                return expression;
            }

            // If the only table expressions are includes, then we need to hoist denormalized expressions to the top.
            bool onlyIncludes = expression.TableExpressions.All(te => te.Kind == TableExpressionKind.Include);
            if (onlyIncludes)
            {
                var newNormalizedPredicates = new List<TableExpression>(expression.TableExpressions.Count + 1);
                newNormalizedPredicates.AddRange(expression.TableExpressions);

                Expression denormalizedExpression = expression.DenormalizedExpressions.Count > 1 ?
                    new MultiaryExpression(MultiaryOperator.And, expression.DenormalizedExpressions)
                    : expression.DenormalizedExpressions[0];

                var newNormalExpression = new TableExpression(null, null, denormalizedExpression, TableExpressionKind.All);
                newNormalizedPredicates.Add(newNormalExpression);

                return new SqlRootExpression(newNormalizedPredicates, Array.Empty<Expression>());
            }

            return expression;
        }
    }
}
