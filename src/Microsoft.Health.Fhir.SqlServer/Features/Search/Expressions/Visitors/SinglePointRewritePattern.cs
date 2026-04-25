// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;

/// <summary>
/// Describes the rewrite pattern for a single-point search parameter.
/// Used by the rewriter to normalize AST shape into a pattern before consulting the policy.
/// </summary>
internal enum SinglePointRewritePattern
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
