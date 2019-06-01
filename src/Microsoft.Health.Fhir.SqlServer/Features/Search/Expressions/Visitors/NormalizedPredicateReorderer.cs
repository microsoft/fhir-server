﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal class NormalizedPredicateReorderer : SqlExpressionRewriterWithDefaultInitialContext<object>
    {
        public static readonly NormalizedPredicateReorderer Instance = new NormalizedPredicateReorderer();

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (expression.NormalizedPredicates.Count == 1)
            {
                return expression;
            }

            List<TableExpression> reorderedExpressions = expression.NormalizedPredicates.OrderByDescending(t =>
            {
                switch (t.SearchParameterQueryGenerator)
                {
                    case ReferenceSearchParameterQueryGenerator _:
                        return 10;
                    case CompartmentSearchParameterQueryGenerator _:
                        return 10;
                    default:
                        return 0;
                }
            }).ToList();

            return new SqlRootExpression(reorderedExpressions, expression.DenormalizedPredicates);
        }
    }
}
