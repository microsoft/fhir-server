// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;

/// <summary>
/// Policy for rewriting search expressions for single-point search parameters.
/// </summary>
internal class SinglePointSearchParameterRewritePolicy
{
    /// <summary>
    /// Internal enum describing rewrite patterns for single-point search parameters.
    /// </summary>
    private enum SinglePointRewritePattern
    {
        /// <summary>
        /// The pattern is not supported for rewriting.
        /// </summary>
        Unsupported = 0,

        /// <summary>
        /// The pattern is an equality comparison.
        /// </summary>
        Equality = 1,

        /// <summary>
        /// The pattern is a greater-than comparison.
        /// </summary>
        GreaterThan = 2,

        /// <summary>
        /// The pattern is a greater-than-or-equal comparison.
        /// </summary>
        GreaterThanOrEqual = 3,

        /// <summary>
        /// The pattern is a less-than comparison.
        /// </summary>
        LessThan = 4,

        /// <summary>
        /// The pattern is a less-than-or-equal comparison.
        /// </summary>
        LessThanOrEqual = 5,
    }

    /// <summary>
    /// Determines the rewrite decision for a search expression with a given search parameter and comparison operator.
    /// </summary>
    /// <param name="searchParameterInfo">The search parameter info.</param>
    /// <param name="binaryOperator">The binary operator.</param>
    /// <returns>The rewrite decision.</returns>
    public static SinglePointRewriteDecision GetRewriteDecision(SearchParameterInfo searchParameterInfo, BinaryOperator binaryOperator)
    {
        // Check if the parameter is allowlisted
        var behavior = SinglePointSearchParameterRegistry.GetBehavior(searchParameterInfo);
        if (behavior == SinglePointSearchBehavior.None)
        {
            return SinglePointRewriteDecision.NoRewrite;
        }

        // Determine the rewrite pattern based on the binary operator
        var pattern = GetRewritePattern(binaryOperator);

        return pattern switch
        {
            SinglePointRewritePattern.Equality => SinglePointRewriteDecision.RewriteToEndDateTimeEquality,
            SinglePointRewritePattern.GreaterThan or
                SinglePointRewritePattern.GreaterThanOrEqual or
                SinglePointRewritePattern.LessThan or
                SinglePointRewritePattern.LessThanOrEqual => SinglePointRewriteDecision.UseExistingExpression,
            _ => SinglePointRewriteDecision.NoRewrite,
        };
    }

    /// <summary>
    /// Maps a binary operator to a rewrite pattern.
    /// </summary>
    private static SinglePointRewritePattern GetRewritePattern(BinaryOperator binaryOperator)
    {
        return binaryOperator switch
        {
            BinaryOperator.Equal => SinglePointRewritePattern.Equality,
            BinaryOperator.GreaterThan => SinglePointRewritePattern.GreaterThan,
            BinaryOperator.GreaterThanOrEqual => SinglePointRewritePattern.GreaterThanOrEqual,
            BinaryOperator.LessThan => SinglePointRewritePattern.LessThan,
            BinaryOperator.LessThanOrEqual => SinglePointRewritePattern.LessThanOrEqual,
            _ => SinglePointRewritePattern.Unsupported,
        };
    }
}
