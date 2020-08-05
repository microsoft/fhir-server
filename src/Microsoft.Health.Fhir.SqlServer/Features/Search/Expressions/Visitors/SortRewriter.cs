// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal class SortRewriter : SqlExpressionRewriter<SearchOptions>
    {
        private readonly NormalizedSearchParameterQueryGeneratorFactory _normalizedSearchParameterQueryGeneratorFactory;

        public SortRewriter(NormalizedSearchParameterQueryGeneratorFactory normalizedSearchParameterQueryGeneratorFactory)
        {
            _normalizedSearchParameterQueryGeneratorFactory = normalizedSearchParameterQueryGeneratorFactory;
        }

        public override Expression VisitSqlRoot(SqlRootExpression expression, SearchOptions context)
        {
            if (context.CountOnly)
            {
                return expression;
            }

            if (context.Sort.Count == 0)
            {
                return expression;
            }

            var queryGenerator = _normalizedSearchParameterQueryGeneratorFactory.GetNormalizedSearchParameterQueryGenerator(context.Sort[0].searchParameterInfo);

            var newNormalizedPredicates = new List<TableExpression>(expression.TableExpressions.Count + 1);
            newNormalizedPredicates.AddRange(expression.TableExpressions);

            newNormalizedPredicates.Add(new TableExpression(queryGenerator, new SortParameterExpression(context.Sort[0].searchParameterInfo), null, TableExpressionKind.Sort));
            //// newNormalizedPredicates.Add(new TableExpression(queryGenerator, null, null, TableExpressionKind.Sort));

            return new SqlRootExpression(newNormalizedPredicates, expression.DenormalizedExpressions);
        }
    }
}
