// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.Core.Extensions;

public static class ExpressionExtensions
{
    public static bool ExtractIncludeAndChainedExpressions(
        this Expression inputExpression,
        out Expression expressionWithoutIncludesOrChained,
        out IReadOnlyList<IncludeExpression> includeExpressions,
        out IReadOnlyList<IncludeExpression> revIncludeExpressions,
        out IReadOnlyList<ChainedExpression> chainedExpressions)
    {
        switch (inputExpression)
        {
            case IncludeExpression ie when ie.Reversed:
                expressionWithoutIncludesOrChained = null;
                includeExpressions = Array.Empty<IncludeExpression>();
                revIncludeExpressions = new[] { ie };
                chainedExpressions = Array.Empty<ChainedExpression>();
                return true;
            case IncludeExpression ie:
                expressionWithoutIncludesOrChained = null;
                includeExpressions = new[] { ie };
                revIncludeExpressions = Array.Empty<IncludeExpression>();
                chainedExpressions = Array.Empty<ChainedExpression>();
                return true;
            case ChainedExpression ie:
                expressionWithoutIncludesOrChained = null;
                includeExpressions = Array.Empty<IncludeExpression>();
                revIncludeExpressions = Array.Empty<IncludeExpression>();
                chainedExpressions = new[] { ie };
                return true;
            case MultiaryExpression me when me.Expressions.Any(e => e is IncludeExpression || e is ChainedExpression):
                includeExpressions = me.Expressions.OfType<IncludeExpression>().Where(ie => !ie.Reversed).ToList();
                revIncludeExpressions = me.Expressions.OfType<IncludeExpression>().Where(ie => ie.Reversed).ToList();
                chainedExpressions = me.Expressions.OfType<ChainedExpression>().ToList();
                expressionWithoutIncludesOrChained = (me.Expressions.Count - (includeExpressions.Count + revIncludeExpressions.Count + chainedExpressions.Count)) switch
                {
                    0 => null,
                    1 => me.Expressions.Single(e => !(e is IncludeExpression || e is ChainedExpression)),
                    _ => new MultiaryExpression(me.MultiaryOperation, me.Expressions.Where(e => !(e is IncludeExpression || e is ChainedExpression)).ToList()),
                };
                return true;
            default:
                expressionWithoutIncludesOrChained = inputExpression;
                includeExpressions = Array.Empty<IncludeExpression>();
                revIncludeExpressions = Array.Empty<IncludeExpression>();
                chainedExpressions = Array.Empty<ChainedExpression>();
                return false;
        }
    }
}
