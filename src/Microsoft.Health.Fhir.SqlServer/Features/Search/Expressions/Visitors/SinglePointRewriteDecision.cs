// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;

/// <summary>
/// Describes the decision to rewrite a search expression for a single-point search parameter.
/// </summary>
internal enum SinglePointRewriteDecision
{
    /// <summary>
    /// No rewrite should be applied.
    /// </summary>
    NoRewrite = 0,

    /// <summary>
    /// Use the existing expression without rewriting.
    /// </summary>
    UseExistingExpression = 1,

    /// <summary>
    /// Rewrite the expression to an end-date-time equality comparison.
    /// </summary>
    RewriteToEndDateTimeEquality = 2,
}
