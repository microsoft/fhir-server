// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;

/// <summary>
/// Policy for deciding on rewrite strategies for single-point search parameters.
/// Consults the allowlist registry and determines the appropriate rewrite decision based on
/// the parameter and the normalized AST rewrite pattern.
/// </summary>
internal class SinglePointSearchParameterRewritePolicy
{
    private readonly SinglePointSearchParameterRegistry _registry;

    /// <summary>
    /// Initializes a new instance of the <see cref="SinglePointSearchParameterRewritePolicy"/> class.
    /// </summary>
    /// <param name="registry">The allowlist registry for single-point search parameters.</param>
    public SinglePointSearchParameterRewritePolicy(SinglePointSearchParameterRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Determines the rewrite decision for a search parameter with a given rewrite pattern.
    /// </summary>
    /// <param name="searchParameterInfo">The search parameter info.</param>
    /// <param name="pattern">The normalized rewrite pattern.</param>
    /// <returns>The rewrite decision.</returns>
    public SinglePointRewriteDecision Decide(SearchParameterInfo searchParameterInfo, SinglePointRewritePattern pattern)
    {
        // Check if the parameter is allowlisted
        if (!_registry.TryGetBehavior(searchParameterInfo, out var behavior))
        {
            return SinglePointRewriteDecision.NoRewrite;
        }

        if (behavior == SinglePointSearchBehavior.None)
        {
            return SinglePointRewriteDecision.NoRewrite;
        }

        // Decide based on the rewrite pattern
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
}
